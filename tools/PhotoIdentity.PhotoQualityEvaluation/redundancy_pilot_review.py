#!/usr/bin/env python3
"""WI-0194 local loopback calibration-only pair reviewer; never serves held-out.

Private paths, photo identities and labels stay on this machine. Existing
durable proxy hashes are verified on every read; originals are never opened.
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
from urllib.parse import parse_qs, urlsplit
import webbrowser

from redundancy_pilot_package import (
    BLIND_PROTOCOL, LABELS, LABELS_PROTOCOL, fingerprint, load_json,
    private_file, validate_blind, validate_labels,
)

from redundancy_validation_package import (
    BLIND_PROTOCOL as HELDOUT_BLIND_PROTOCOL,
    LABELS_PROTOCOL as HELDOUT_LABELS_PROTOCOL,
    validate_holdout_blind, validate_review_file,
)

MAP_PROTOCOL = "wi0194-private-pilot-proxies-v1"


def load_session(blind_path, mapping_path, labels_path):
    blind = load_json(blind_path)
    heldout = blind.get("protocol") == HELDOUT_BLIND_PROTOCOL
    ids, revisions = (
        validate_holdout_blind(blind) if heldout
        else validate_blind(blind))
    pilot_sha = fingerprint(blind_path.read_bytes())
    mapping = load_json(mapping_path)
    if (not isinstance(mapping, dict) or
            set(mapping) != {"schemaVersion", "protocol", "pilotSha256",
                             "proxyRoot", "proxyProfile", "photos",
                             "catalogReadOnly", "noOriginalsHydrated"}
            or mapping["schemaVersion"] != 1
            or mapping["protocol"] != MAP_PROTOCOL
            or mapping["pilotSha256"] != pilot_sha
            or mapping["catalogReadOnly"] is not True
            or mapping["noOriginalsHydrated"] is not True
            or not isinstance(mapping["photos"], list) or
            not isinstance(mapping["proxyProfile"], str)):
        raise ValueError("Proxy evidence is not bound to the blind pilot")
    root = Path(mapping["proxyRoot"]).resolve(strict=True)
    if not root.is_dir():
        raise ValueError("The local proxy root is unavailable")

    photos = {}
    for row in mapping["photos"]:
        if not isinstance(row, dict) or set(row) != {
            "revisionId", "relativePath", "sha256", "byteLength"
        }:
            raise ValueError("Invalid proxy mapping record")
        ident = row["revisionId"].casefold()
        relative = row["relativePath"]
        digest = row["sha256"]
        length = row["byteLength"]
        if (ident in photos or not isinstance(relative, str) or
                not relative or Path(relative).is_absolute() or
                any(chunk == ".." for chunk in relative.replace("\\", "/").split("/"))
                or not isinstance(digest, str) or len(digest) != 64 or
                any(c not in "0123456789abcdef" for c in digest) or
                type(length) is not int or not 1 <= length <= 20_000_000):
            raise ValueError("Invalid proxy mapping values")
        # Real-path containment also prevents symlink escapes.
        full = (root / relative).resolve(strict=True)
        if not full.is_relative_to(root) or not full.is_file():
            raise ValueError("Proxy mapping escaped its derivative root")
        photos[ident] = (full, digest, length)
    if set(photos) != revisions:
        raise ValueError("Proxy mapping does not cover exactly the blind-pilot photos")

    if labels_path.exists():
        saved = load_json(labels_path)
        if heldout:
            validate_review_file(saved, pilot_sha, ids)
        else:
            validate_labels(saved, pilot_sha, ids)
        received = {
            row["pairId"]: {"label": row["label"], "notes": row["notes"]}
            for row in saved["reviews"]
        }
    else:
        received = {}
    pair_ids = [x["pairId"] for x in blind["pairs"]]
    return {
        "blind": blind, "photos": photos, "labels": received,
        "pair_ids": pair_ids, "pilot_sha": pilot_sha,
        "labels_path": labels_path, "lock": threading.Lock(),
        "heldout": heldout,
        "labels_protocol": HELDOUT_LABELS_PROTOCOL if heldout else LABELS_PROTOCOL,
    }


def read_verified_proxy(session, pair_id, side):
    pair = next((p for p in session["blind"]["pairs"]
                 if p["pairId"] == pair_id), None)
    if pair is None or side not in ("left", "right"):
        raise ValueError("Unknown pilot image")
    revision = pair[side + "RevisionId"].casefold()
    path, expected_sha, expected_len = session["photos"][revision]
    data = path.read_bytes()
    if len(data) != expected_len or hashlib.sha256(data).hexdigest() != expected_sha:
        raise ValueError("Review-proxy bytes no longer match their durable digest")
    return data


def store_review(session, pair_id, label, notes):
    if (pair_id not in session["pair_ids"] or label not in LABELS or
            not isinstance(notes, str) or len(notes) > 1000):
        raise ValueError("Invalid calibration-only review")
    with session["lock"]:
        saved = dict(session["labels"])
        saved[pair_id] = {"label": label, "notes": notes}
        value = {
            "schemaVersion": 1, "protocol": session["labels_protocol"],
            "pilotSha256": session["pilot_sha"],
            "reviews": [{
                "pairId": pid, "label": saved[pid]["label"],
                "notes": saved[pid]["notes"],
            } for pid in session["pair_ids"] if pid in saved]
        }
        destination = session["labels_path"]
        dest_bytes = (json.dumps(value, indent=2, allow_nan=False) + "\n").encode()
        if destination.exists():
            # Ensure a separate existing review file has not been substituted.
            if session["heldout"]:
                validate_review_file(load_json(destination), session["pilot_sha"],
                                     set(session["pair_ids"]))
            else:
                validate_labels(load_json(destination), session["pilot_sha"],
                                set(session["pair_ids"]))
            temporary = destination.with_name(
                destination.name + ".tmp-" + secrets.token_hex(8))
            try:
                with temporary.open("xb") as stream:
                    stream.write(dest_bytes)
                os.replace(temporary, destination)
            finally:
                temporary.unlink(missing_ok=True)
        else:
            destination.parent.mkdir(parents=True, exist_ok=True)
            with destination.open("xb") as stream:
                stream.write(dest_bytes)
        session["labels"] = saved
        return len(saved)


def public_state(session, index):
    pairs = session["blind"]["pairs"]
    index = max(0, min(index, len(pairs)-1))
    pair_id = pairs[index]["pairId"]
    known = session["labels"].get(pair_id)
    next_unrated = next(
        ((index + offset) % len(pairs)
         for offset in range(1, len(pairs) + 1)
         if pairs[(index + offset) % len(pairs)]["pairId"]
         not in session["labels"]), None)
    return {
        "index": index, "total": len(pairs),
        "reviewed": len(session["labels"]),
        "remaining": len(pairs) - len(session["labels"]),
        "nextUnreviewedIndex": next_unrated,
        "pairId": pair_id,
        "label": known["label"] if known else None,
        "notes": known["notes"] if known else "",
    }


REVIEWER_HTML = """<!doctype html>
<html lang="en"><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Photo Identity – __STAGE__</title>
<style>
:root{font-family:system-ui;color-scheme:dark}body{margin:0;background:#111;color:#eee}
main{max-width:1450px;margin:auto;padding:12px}
header,.bar{display:flex;align-items:center;justify-content:space-between;gap:12px;flex-wrap:wrap}
.small{font-size:13px;color:#bbb}#stage{display:grid;grid-template-columns:1fr 1fr;gap:10px;margin:12px 0}
.image{background:#050505;min-height:55vh;display:flex;justify-content:center;align-items:center;border-radius:10px;overflow:hidden}
.image img{width:100%;max-height:68vh;object-fit:contain}
.buttons{display:grid;grid-template-columns:repeat(4,minmax(0,1fr));gap:8px}
button{padding:12px;border:1px solid #555;border-radius:8px;background:#292929;color:#eee;cursor:pointer;font:inherit}
button.selected{outline:3px solid #ccc}textarea{box-sizing:border-box;width:100%;padding:8px;margin:10px 0;background:#181818;border:1px solid #555;color:#eee;border-radius:6px}
@media(max-width:700px){#stage{grid-template-columns:1fr}.image{min-height:30vh}.image img{max-height:40vh}.buttons{grid-template-columns:1fr 1fr}}
</style></head><body><main>
<header><div><strong>Blinded visual redundancy — __STAGE__</strong>
<div class="small">__SCOPE__. No similarity score, model, bucket, partition, or original path is shown.</div></div>
<div id="status">Loading…</div></header>
<div id="stage"><div class="image"><img id="left" alt="Left review proxy"></div>
<div class="image"><img id="right" alt="Right review proxy"></div></div>
<div class="buttons">
<button data-label="redundant">1 · Redundant</button>
<button data-label="similar-keep-both">2 · Similar, keep both</button>
<button data-label="not-similar">3 · Not similar</button>
<button data-label="unsure">4 · Unsure</button></div>
<textarea id="notes" rows="2" maxlength="1000" placeholder="Optional reason; saved with your next label"></textarea>
<div class="bar"><button id="prev">← Previous</button><button id="next">Next →</button>
<button id="unrated">Next unreviewed</button><button id="save">Save current label/notes</button></div>
<p class="small">Redundant = you would normally select only one of these in the same generated collection.
Similar, keep both = images look alike but both deserve to remain eligible.
Not similar = no meaningful visual redundancy. Unsure = needs another look.
Keys 1–4 label and advance; left/right arrows navigate. Reviews save privately on this machine.
Close the terminal with Ctrl+C when done. This does not freeze any held-out policy.</p>
</main><script>
const token="__TOKEN__";
const headers={"X-PhotoIdentity-Pilot":token,"Content-Type":"application/json"};
let state=null;let busy=false;
const status=document.getElementById("status"),notes=document.getElementById("notes");
async function load(index){
 const r=await fetch("/api/state?index="+index,{headers});
 if(!r.ok)throw new Error("Unable to load pilot pair");state=await r.json();
 document.getElementById("left").src="/api/image/"+state.pairId+"/left?token="+token;
 document.getElementById("right").src="/api/image/"+state.pairId+"/right?token="+token;
 notes.value=state.notes||"";
 document.querySelectorAll("[data-label]").forEach(b=>b.classList.toggle("selected",b.dataset.label===state.label));
 status.textContent=(state.index+1)+"/"+state.total+" · reviewed "+state.reviewed+" · remaining "+state.remaining;
}
async function save(label,advance=true){
 if(busy||!state||!label)return;busy=true;
 try{
  const r=await fetch("/api/label/"+state.pairId,{method:"POST",headers,
     body:JSON.stringify({label,notes:notes.value})});
  if(!r.ok)throw new Error("Unable to save label");
  const next=advance?((await r.json()).nextUnreviewedIndex??Math.min(state.index+1,state.total-1)):state.index;
  await load(next);
 }catch(e){status.textContent=e.message;}finally{busy=false;}
}
document.querySelectorAll("[data-label]").forEach(b=>b.onclick=()=>save(b.dataset.label));
document.getElementById("prev").onclick=()=>load(Math.max(0,state.index-1));
document.getElementById("next").onclick=()=>load(Math.min(state.total-1,state.index+1));
document.getElementById("unrated").onclick=()=>load(state.nextUnreviewedIndex??state.index);
document.getElementById("save").onclick=()=>save(state.label,false);
document.addEventListener("keydown",e=>{
 if(e.target===notes)return;
 if(["1","2","3","4"].includes(e.key)){
  e.preventDefault();save(["redundant","similar-keep-both","not-similar","unsure"][+e.key-1]);
 }else if(e.key==="ArrowLeft"){e.preventDefault();load(Math.max(0,state.index-1));}
 else if(e.key==="ArrowRight"){e.preventDefault();load(Math.min(state.total-1,state.index+1));}
});
load(0).catch(e=>status.textContent=e.message);
</script></body></html>"""


def make_handler(session, token):
    class Handler(BaseHTTPRequestHandler):
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

        def guarded(self, parsed, allow_query=False):
            supplied = self.headers.get("X-PhotoIdentity-Pilot")
            if allow_query and not supplied:
                supplied = parse_qs(parsed.query).get("token", [None])[0]
            return secrets.compare_digest(str(supplied or ""), token)

        def do_GET(self):
            parsed = urlsplit(self.path)
            if parsed.path == "/":
                return self.respond(
                    REVIEWER_HTML.replace("__TOKEN__", token).replace(
                        "__STAGE__", "Independent held-out validation"
                        if session["heldout"] else "Calibration pilot").replace(
                        "__SCOPE__", "Frozen held-out validation pairs"
                        if session["heldout"] else "Only calibration pairs").encode(),
                    "text/html; charset=utf-8")
            if not self.guarded(parsed, allow_query=parsed.path.startswith("/api/image/")):
                return self.respond(b"Forbidden", "text/plain", 403)
            try:
                if parsed.path == "/api/state":
                    raw = parse_qs(parsed.query).get("index", ["0"])[0]
                    index = int(raw)
                    result = public_state(session, index)
                    return self.respond(json.dumps(result).encode(), "application/json")
                if parsed.path.startswith("/api/image/"):
                    parts = parsed.path.split("/")
                    if len(parts) != 5:
                        raise ValueError("Unknown image route")
                    content = read_verified_proxy(session, parts[3], parts[4])
                    return self.respond(content, "image/jpeg")
            except (ValueError, OSError, KeyError, TypeError):
                return self.respond(b"Invalid request", "text/plain", 400)
            return self.respond(b"Not found", "text/plain", 404)

        def do_POST(self):
            parsed = urlsplit(self.path)
            if not self.guarded(parsed):
                return self.respond(b"Forbidden", "text/plain", 403)
            if not parsed.path.startswith("/api/label/"):
                return self.respond(b"Not found", "text/plain", 404)
            try:
                size = int(self.headers.get("Content-Length", "-1"))
                if size < 2 or size > 4096:
                    raise ValueError("Review body exceeded bounds")
                body = json.loads(self.rfile.read(size))
                if not isinstance(body, dict) or set(body) != {"label", "notes"}:
                    raise ValueError("Invalid review body")
                pair_id = parsed.path.split("/")[-1]
                store_review(session, pair_id, body["label"], body["notes"])
                index = session["pair_ids"].index(pair_id)
                state = public_state(session, index)
                return self.respond(json.dumps(state).encode(), "application/json")
            except (ValueError, OSError, KeyError, TypeError):
                return self.respond(b"Invalid review", "text/plain", 400)
    return Handler


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--blind", required=True)
    parser.add_argument("--proxies", required=True)
    parser.add_argument("--labels", required=True)
    parser.add_argument("--port", type=int, default=5184)
    parser.add_argument("--no-open", action="store_true")
    parser.add_argument("--heldout-validation", action="store_true",
                        help="Explicit authorization for frozen held-out review only")
    args = parser.parse_args()
    if not 1024 <= args.port <= 65535:
        parser.error("Port must be between 1024 and 65535")
    try:
        blind = private_file(args.blind, must_exist=True)
        proxies = private_file(args.proxies, must_exist=True)
        labels = private_file(args.labels, must_exist=False)
        if len({blind, proxies, labels}) != 3:
            raise ValueError("All pilot paths must be distinct")
        session = load_session(blind, proxies, labels)
        if session["heldout"] != args.heldout_validation:
            raise ValueError("Review stage does not match explicit authorization flag")
        token = secrets.token_urlsafe(32)
        with ThreadingHTTPServer(
            ("127.0.0.1", args.port), make_handler(session, token)) as server:
            url = "http://127.0.0.1:" + str(args.port)
            print(("Local HELD-OUT validation" if session["heldout"]
                   else "Local CALIBRATION-only blinded review") + ": " + url)
            print("Private progress resumes on next launch. Ctrl+C to stop.")
            if not args.no_open:
                webbrowser.open(url)
            try:
                server.serve_forever()
            except KeyboardInterrupt:
                pass
    except (OSError, ValueError, TypeError, KeyError) as error:
        raise SystemExit("WI-0194 local reviewer failed (" +
                         type(error).__name__ + ")") from None


if __name__ == "__main__":
    main()
