#!/usr/bin/env python3
"""WI-0195 blinded shadow collection comparison on hash-verified local proxies.

Local-only loopback server; private comparison IDs, revisions and choices never
leave this machine. Finish & Report cleanly exits after reviews are complete.
"""
from __future__ import annotations

import argparse
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import hashlib
import json
import os
from pathlib import Path
import secrets
import threading
from urllib.parse import parse_qs, unquote, urlsplit
import webbrowser

from collection_shadow_simulator import (
    AUDIT_PROTOCOL, BLIND_PROTOCOL, LABEL_PROTOCOL, REVIEW_CHOICES,
    load, private_path, sha,
)

HTML = """<!doctype html>
<html lang="en"><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>Photo Identity — Blinded collection shadow review</title>
<style>
:root{color-scheme:dark;font-family:system-ui}body{margin:0;background:#111;color:#eee}
main{max-width:1580px;margin:auto;padding:12px}.small{color:#bcbcbc;font-size:13px}
header,.toolbar{display:flex;gap:10px;align-items:center;justify-content:space-between;flex-wrap:wrap}
#stage{display:grid;grid-template-columns:1fr 1fr;gap:10px;margin:14px 0}
.panel{padding:8px;background:#191919;border:1px solid #555;border-radius:10px;min-height:45vh}
.photos{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:6px}
.photos img{width:100%;max-height:34vh;object-fit:contain;background:#050505}
button{padding:12px;border-radius:7px;border:1px solid #666;background:#242424;color:#eee;font:inherit;cursor:pointer}
button.selected{outline:3px solid #ddd}.choices{display:flex;gap:8px;flex-wrap:wrap}
@media(max-width:850px){#stage{grid-template-columns:1fr}.photos img{max-height:25vh}}
</style></head><body><main>
<header><div><strong>Blinded collection comparison</strong>
<div class="small">Research-only shadow. No policy names, raw quality scores, source paths, or thresholds are shown.</div>
</div><div id="status">Loading…</div></header>
<div id="stage">
<section class="panel"><h3>Selection A</h3><div class="photos" id="left"></div></section>
<section class="panel"><h3>Selection B</h3><div class="photos" id="right"></div></section></div>
<div class="choices">
<button data-choice="left">1 · Prefer A</button>
<button data-choice="right">2 · Prefer B</button>
<button data-choice="same">3 · No preference</button>
<button data-choice="unsure">4 · Unsure</button></div>
<div class="toolbar" style="margin-top:14px">
<button id="previous">← Previous</button><button id="next">Next →</button>
<button id="unrated">Next unreviewed</button>
<button id="finish">Finish & Report</button>
</div>
<p class="small">Choose which selection you would prefer for the same collection.
Consider meaningful-photo loss, repetition, overall viewing quality and relevance.
The displayed photos come only from existing durable local review proxies.
Use keys 1–4. Reviews are saved privately after each choice.
Finish & Report exits the local server and returns the aggregate report to PowerShell;
you can also stop and resume later.</p>
</main><script>
const token="__TOKEN__";
const headers={"X-PhotoIdentity-Shadow":token,"Content-Type":"application/json"};
let state=null,busy=false;const status=document.getElementById("status");
function showImages(element,ids){
  element.replaceChildren();
  for(const revision of ids){
    const img=document.createElement("img");img.alt="Collection candidate";
    img.src="/api/image/"+encodeURIComponent(revision)+"?token="+encodeURIComponent(token);
    element.appendChild(img);
  }
}
async function load(index){
 const response=await fetch("/api/state?index="+index,{headers});
 if(!response.ok)throw new Error("Could not load collection comparison");
 state=await response.json();
 showImages(document.getElementById("left"),state.leftRevisions);
 showImages(document.getElementById("right"),state.rightRevisions);
 status.textContent=(state.index+1)+"/"+state.total+" · reviewed "+state.reviewed+" · remaining "+state.remaining;
 document.querySelectorAll("[data-choice]").forEach(b=>b.classList.toggle("selected",state.choice===b.dataset.choice));
}
async function save(choice){
 if(!state||busy)return;busy=true;
 try{
  const response=await fetch("/api/review/"+state.comparisonId,{
   method:"POST",headers,body:JSON.stringify({choice})});
  if(!response.ok)throw new Error("Could not save comparison");
  const updated=await response.json();
  await load(updated.nextUnreviewedIndex??updated.index);
 }catch(e){status.textContent=e.message;}finally{busy=false;}
}
document.querySelectorAll("[data-choice]").forEach(b=>b.onclick=()=>save(b.dataset.choice));
document.getElementById("previous").onclick=()=>load(Math.max(0,state.index-1));
document.getElementById("next").onclick=()=>load(Math.min(state.total-1,state.index+1));
document.getElementById("unrated").onclick=()=>load(state.nextUnreviewedIndex??state.index);
document.getElementById("finish").onclick=async()=>{
 const response=await fetch("/api/finish",{method:"POST",headers});
 if(!response.ok){status.textContent="Finish requires all comparisons to be reviewed";return;}
 status.textContent="Finished — aggregate report prints in PowerShell.";
};
document.addEventListener("keydown",e=>{
 if(e.key>="1"&&e.key<="4"){e.preventDefault();save(["left","right","same","unsure"][+e.key-1]);}
});
load(0).catch(e=>status.textContent=e.message);
</script></body></html>"""


def load_session(blind_file, audit_file, proxy_file, review_file):
    blind, audit, mapping = load(blind_file), load(audit_file), load(proxy_file)
    if (blind.get("protocol") != BLIND_PROTOCOL or
            audit.get("protocol") != AUDIT_PROTOCOL or
            sha(blind) != audit.get("blindSha256")):
        raise ValueError("Blind shadow audit signature mismatch")
    if (not isinstance(mapping, dict) or mapping.get("protocol") !=
            "wi0195-private-shadow-proxies-v1" or
            mapping.get("blindFileSha256") !=
            hashlib.sha256(blind_file.read_bytes()).hexdigest() or
            mapping.get("catalogReadOnly") is not True or
            mapping.get("noOriginalsHydrated") is not True):
        raise ValueError("Private proxy map does not match blind comparison")
    root = Path(mapping["proxyRoot"]).resolve(strict=True)
    if not root.is_dir():
        raise ValueError("Local proxy root unavailable")
    images = {}
    for record in mapping["photos"]:
        if (not isinstance(record, dict) or set(record) !=
                {"revisionId", "relativePath", "sha256", "byteLength"}):
            raise ValueError("Unexpected proxy mapping")
        ident = record["revisionId"].casefold()
        rel = record["relativePath"]
        if (not isinstance(rel, str) or not rel or
                Path(rel).is_absolute() or
                ".." in rel.replace("\\", "/").split("/") or
                ":" in rel or ident in images or
                type(record["byteLength"]) is not int or
                not 1 <= record["byteLength"] <= 20_000_000):
            raise ValueError("Unsafe proxy metadata")
        dest = (root / rel).resolve(strict=True)
        if not dest.is_relative_to(root) or not dest.is_file():
            raise ValueError("Proxy resolved outside root")
        digest = record["sha256"]
        if not isinstance(digest, str) or len(digest) != 64 or any(
                c not in "0123456789abcdef" for c in digest):
            raise ValueError("Invalid proxy SHA256")
        images[ident] = (dest, digest, record["byteLength"])
    comparisons = blind["comparisons"]
    identities = {row["comparisonId"] for row in comparisons}
    expected_photos = {rev.casefold()
                       for row in comparisons
                       for side in ("leftRevisions", "rightRevisions")
                       for rev in row[side]}
    if (len(identities) != len(comparisons) or
            set(images) != expected_photos or len(comparisons) > 40):
        raise ValueError("Proxy map and blind scenario membership mismatch")

    saved = {}
    if review_file.exists():
        review = load(review_file)
        if (not isinstance(review, dict) or
                set(review) != {"schemaVersion", "protocol", "blindSha256", "reviews"}
                or review["schemaVersion"] != 1 or
                review["protocol"] != LABEL_PROTOCOL or
                review["blindSha256"] != sha(blind) or
                not isinstance(review["reviews"], list)):
            raise ValueError("Private review does not match blind cohort")
        for row in review["reviews"]:
            if (not isinstance(row, dict) or
                    set(row) != {"comparisonId", "choice"} or
                    row["comparisonId"] not in identities or
                    row["choice"] not in REVIEW_CHOICES or
                    row["comparisonId"] in saved):
                raise ValueError("Malformed or duplicate private review")
            saved[row["comparisonId"]] = row["choice"]
    return {"blind": blind, "images": images, "saved": saved,
            "path": review_file, "lock": threading.Lock(),
            "sha": sha(blind)}


def read_proxy(session, revision):
    if revision.casefold() not in session["images"]:
        raise ValueError("Unknown revision")
    path, expected, length = session["images"][revision.casefold()]
    data = path.read_bytes()
    if len(data) != length or hashlib.sha256(data).hexdigest() != expected:
        raise ValueError("Durable proxy SHA/size mismatch")
    return data


def public_state(session, index):
    comps = session["blind"]["comparisons"]
    index = max(0, min(index, len(comps) - 1))
    row = comps[index]
    next_index = next((
        (index + offset) % len(comps) for offset in range(1, len(comps)+1)
        if comps[(index + offset) % len(comps)]["comparisonId"]
        not in session["saved"]), None)
    return {
        "index": index, "total": len(comps),
        "reviewed": len(session["saved"]), "remaining": len(comps)-len(session["saved"]),
        "nextUnreviewedIndex": next_index,
        "comparisonId": row["comparisonId"],
        "leftRevisions": row["leftRevisions"],
        "rightRevisions": row["rightRevisions"],
        "choice": session["saved"].get(row["comparisonId"]),
    }


def save_review(session, ident, choice):
    if (ident not in {x["comparisonId"] for x in session["blind"]["comparisons"]}
            or choice not in REVIEW_CHOICES):
        raise ValueError("Unknown choice")
    with session["lock"]:
        updated = dict(session["saved"])
        updated[ident] = choice
        reviews = {"schemaVersion": 1, "protocol": LABEL_PROTOCOL,
                   "blindSha256": session["sha"],
                   "reviews": [
                       {"comparisonId": row["comparisonId"],
                        "choice": updated[row["comparisonId"]]}
                       for row in session["blind"]["comparisons"]
                       if row["comparisonId"] in updated]}
        dest = session["path"]
        dest.parent.mkdir(parents=True, exist_ok=True)
        if dest.exists():
            # Protect against external edits or incorrect restart.
            if load(dest).get("blindSha256") != session["sha"]:
                raise ValueError("Private review was replaced")
            temporary = dest.with_name(dest.name + ".tmp-" + secrets.token_hex(6))
            try:
                with temporary.open("xb") as stream:
                    stream.write((json.dumps(reviews, indent=2) + "\n").encode())
                os.replace(temporary, dest)
            finally:
                temporary.unlink(missing_ok=True)
        else:
            with dest.open("xb") as stream:
                stream.write((json.dumps(reviews, indent=2) + "\n").encode())
        session["saved"] = updated
        return len(updated)


def handler_for(session, token):
    class Handler(BaseHTTPRequestHandler):
        server_version = "PhotoIdentityShadow/1"
        def log_message(self, *_):
            pass
        def respond(self, data, content_type, status=200):
            self.send_response(status)
            self.send_header("Content-Type", content_type)
            self.send_header("Cache-Control", "no-store")
            self.send_header("Referrer-Policy", "no-referrer")
            self.send_header("X-Frame-Options", "DENY")
            self.send_header("X-Content-Type-Options", "nosniff")
            self.send_header("Content-Length", str(len(data)))
            self.end_headers()
            self.wfile.write(data)
        def allowed(self, parsed, image=False):
            supplied = self.headers.get("X-PhotoIdentity-Shadow")
            if image and not supplied:
                supplied = parse_qs(parsed.query).get("token", [None])[0]
            return secrets.compare_digest(str(supplied or ""), token)
        def do_GET(self):
            parsed = urlsplit(self.path)
            if parsed.path == "/":
                return self.respond(HTML.replace("__TOKEN__", token).encode(),
                                    "text/html; charset=utf-8")
            if not self.allowed(parsed, image=parsed.path.startswith("/api/image/")):
                return self.respond(b"Unauthorized", "text/plain", 403)
            try:
                if parsed.path == "/api/state":
                    index = int(parse_qs(parsed.query).get("index", ["0"])[0])
                    return self.respond(json.dumps(public_state(session, index)).encode(),
                                        "application/json")
                if parsed.path.startswith("/api/image/"):
                    revision = unquote(parsed.path[len("/api/image/"):])
                    data = read_proxy(session, revision)
                    return self.respond(data, "image/jpeg")
            except (OSError, TypeError, ValueError, KeyError):
                return self.respond(b"Invalid request", "text/plain", 400)
            return self.respond(b"Not found", "text/plain", 404)
        def do_POST(self):
            parsed = urlsplit(self.path)
            if not self.allowed(parsed):
                return self.respond(b"Unauthorized", "text/plain", 403)
            if parsed.path == "/api/finish":
                if len(session["saved"]) != len(session["blind"]["comparisons"]):
                    return self.respond(b"Incomplete", "text/plain", 409)
                self.respond(b"{}", "application/json")
                threading.Thread(target=self.server.shutdown, daemon=True).start()
                return
            if not parsed.path.startswith("/api/review/"):
                return self.respond(b"Not found", "text/plain", 404)
            try:
                size = int(self.headers.get("Content-Length", "-1"))
                if size < 2 or size > 2048:
                    raise ValueError("Review size exceeded")
                body = json.loads(self.rfile.read(size))
                if not isinstance(body, dict) or set(body) != {"choice"}:
                    raise ValueError("Unexpected review fields")
                ident = parsed.path[len("/api/review/"):]
                save_review(session, ident, body["choice"])
                index = next(i for i, c in enumerate(session["blind"]["comparisons"])
                             if c["comparisonId"] == ident)
                self.respond(json.dumps(public_state(session, index)).encode(),
                             "application/json")
            except (ValueError, TypeError, OSError, KeyError, StopIteration):
                self.respond(b"Invalid review", "text/plain", 400)
    return Handler


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--blind", required=True)
    parser.add_argument("--audit", required=True)
    parser.add_argument("--proxies", required=True)
    parser.add_argument("--reviews", required=True)
    parser.add_argument("--port", type=int, default=5186)
    parser.add_argument("--no-open", action="store_true")
    args = parser.parse_args()
    if not 1024 <= args.port <= 65535:
        parser.error("Port outside local reserved range")
    try:
        paths = [private_path(p, required=i < 3)
                 for i, p in enumerate(
                     (args.blind, args.audit, args.proxies, args.reviews))]
        if len(set(paths)) != 4:
            raise ValueError("Review files must be distinct")
        session = load_session(*paths)
        token = secrets.token_urlsafe(32)
        with ThreadingHTTPServer(("127.0.0.1", args.port),
                                 handler_for(session, token)) as server:
            url = "http://127.0.0.1:" + str(args.port)
            print("Blinded research-only collection comparison: " + url)
            print("Complete all comparisons, then click Finish & Report.")
            if not args.no_open:
                webbrowser.open(url)
            try:
                server.serve_forever()
            except KeyboardInterrupt:
                pass
    except (ValueError, OSError, KeyError, TypeError) as err:
        raise SystemExit("WI-0195 blinded reviewer failed (" +
                         type(err).__name__ + ")") from None


if __name__ == "__main__":
    main()
