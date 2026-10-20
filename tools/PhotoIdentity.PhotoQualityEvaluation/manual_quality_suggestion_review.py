#!/usr/bin/env python3
"""WI-0629 local human-controlled review of automatic candidate suggestions.

This server cannot exclude/purge a photo. "Consider exclusion" is a private
review annotation; actual exclusion only happens in the native Photo Identity
Photo page under its separately confirmed existing action.
"""
from __future__ import annotations

import argparse
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
from pathlib import Path
import secrets
import threading
from urllib.parse import urlsplit, quote, unquote, parse_qs
import webbrowser

from manual_quality_suggestions import (
    ACTIONS, REASONS, SCOPES, aggregate, atomic_state, blank_state,
    private_path, read_json, read_proxy, validate_manifest, validate_state,
)

HTML = """<!doctype html><html lang="en"><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<meta name="referrer" content="no-referrer">
<title>Photo Identity · Low-quality photo suggestions</title>
<style>
:root{font-family:system-ui,sans-serif;color-scheme:dark}
body{background:#121212;color:#f3f3f3;margin:0}
main{max-width:1150px;margin:auto;padding:18px}
header,.row{display:flex;justify-content:space-between;gap:12px;flex-wrap:wrap;align-items:center}
small,.small{color:#b8b8b8;font-size:14px}
section{border:1px solid #454545;border-radius:10px;padding:14px;margin:14px 0}
button,select,a.action{padding:12px 16px;border:1px solid #777;background:#272727;color:#fff;border-radius:8px;font:inherit;cursor:pointer}
button:focus-visible,a:focus-visible{outline:3px solid #fcbd63}
button.sel{border:2px solid #eee;background:#465064}
img{max-width:100%;max-height:66vh;object-fit:contain}
#image{min-height:280px;background:#050505;display:flex;align-items:center;justify-content:center;border-radius:8px}
.badge{display:inline-block;border:1px solid #777;border-radius:8px;padding:6px;margin:3px}
#choices{display:flex;gap:10px;flex-wrap:wrap}
label{display:inline-flex;gap:8px;align-items:center;margin:8px}
#message{min-height:24px}
a.action{text-decoration:none;display:inline-block}
.warn{border-color:#cc9952;background:#322718}
</style></head><body><main>
<header><div><h1>Possible low-quality photos</h1>
<p>Automatic suggestions. Every decision is yours. No photo is removed without a separate explicit confirmation in Photo Identity.</p>
</div><div id="status">Loading…</div></header>
<section>
<strong>Where would suggestions help you?</strong>
<p class="small">These switches only control this private review queue. They do not alter Archive, collections or slideshows.</p>
<div class="row">
<label><input type="checkbox" data-scope="archive-cleanup"> Archive cleanup</label>
<label><input type="checkbox" data-scope="collection-curation"> Collection review</label>
<label><input type="checkbox" data-scope="slideshow-curation"> Slideshow review</label>
</div>
<div class="row"><label>Review context
<select id="scope"><option value="archive-cleanup">Archive cleanup</option>
<option value="collection-curation">Collection review</option>
<option value="slideshow-curation">Slideshow review</option></select></label>
<label>Reason filter <select id="reason"><option value="all">All candidate reasons</option>
<option value="candidate-low-sharpness">Possible blur/focus</option>
<option value="candidate-severe-underexposure">Underexposure</option>
<option value="candidate-severe-overexposure">Overexposure</option>
<option value="candidate-low-contrast">Low contrast</option>
<option value="candidate-shadow-clipping">Shadow clipping</option>
<option value="candidate-highlight-clipping">Highlight clipping</option>
<option value="candidate-low-effective-proxy-resolution">Low resolution</option>
</select></label></div></section>
<section><div class="row"><h2>Review suggestion</h2><span id="progress"></span></div>
<div id="image"><p>Loading private review proxy…</p></div>
<p id="reasons"></p>
<p class="small">A sharpness or exposure flag is not proof of a bad photo.
Personal meaning, intentional style and subject focus are for you to judge.</p>
<div id="choices">
<button data-decision="keep">1 · Keep</button>
<button data-decision="dismiss">2 · Dismiss suggestion</button>
<button data-decision="consider-exclusion">3 · Consider exclusion</button>
</div>
<div class="row" style="margin-top:16px">
<button id="previous">← Previous</button><button id="next">Next →</button>
<button id="unreviewed">Next unreviewed</button>
<a id="native" class="action warn" target="_blank" rel="noopener noreferrer">Open in Photo Identity</a>
</div>
<p class="small"><strong>Consider exclusion</strong> only marks this suggestion in a private file.
To actually exclude a source copy, open the photo in Photo Identity and use its
separately confirmed <strong>Exclude from Photo Identity</strong> command.
That action purges Photo Identity’s retained data but does not delete the OneDrive original.
A per-collection or slideshow hide is not implemented here.</p>
</section>
<section class="row"><div><button id="finish">Finish & Report</button></div>
<div id="message" role="status"></div></section>
<p class="small">Review state resumes on the next launch; keys 1/2/3 save the selected decision.
Originals are never opened by this reviewer. Contexts and notes stay in private local files.</p>
</main><script>
const token="__TOKEN__", headers={"X-PhotoIdentity-Suggestions":token,"Content-Type":"application/json"};
let state=null, busy=false, index=0;
const status=document.getElementById("status"), progress=document.getElementById("progress");
const scope=document.getElementById("scope"),reason=document.getElementById("reason");
async function api(url,body){
 const res=await fetch(url,{method:body?"POST":"GET",headers,body:body?JSON.stringify(body):undefined});
 if(!res.ok)throw new Error(await res.text());
 return res.json();
}
function show(){
 if(!state)return;
 for(const key of ["archive-cleanup","collection-curation","slideshow-curation"]){
  const input=document.querySelector('[data-scope="'+key+'"]');
  input.checked=state.enabledScopes[key];
 }
 let total=state.candidates.length;
 status.textContent=state.scope+" · "+state.reviewed+" reviewed · "+state.remaining+" remaining";
 const p=state.current;
 progress.textContent=(total?(state.index+1):0)+" / "+total;
 document.getElementById("choices").style.display=p?"flex":"none";
 document.getElementById("native").style.display=p?"inline-block":"none";
 if(!p){document.getElementById("image").replaceChildren(document.createTextNode(
  state.scopeEnabled?"No suggestions in this filter.":"This context is off. Enable it above to review suggestions."));
  document.getElementById("reasons").textContent="";return;}
 const image=new Image();image.alt="Potentially low-quality photo review proxy";
 image.src="/api/image/"+encodeURIComponent(p.revisionId)+"?token="+encodeURIComponent(token);
 document.getElementById("image").replaceChildren(image);
 document.getElementById("reasons").textContent=p.reasons.join(" · ");
 document.getElementById("native").href=state.photoUrl;
 document.querySelectorAll("[data-decision]").forEach(b=>b.classList.toggle("sel",b.dataset.decision===p.decision));
}
async function load(requested){
 try{state=await api("/api/state?scope="+encodeURIComponent(scope.value)+"&reason="+encodeURIComponent(reason.value)+"&index="+requested);
  index=state.index;show();document.getElementById("message").textContent="";
 }catch(e){document.getElementById("message").textContent=e.message;}
}
async function act(decision){
 if(!state||!state.current||busy)return;busy=true;
 try{await api("/api/decide",{scope:scope.value,revisionId:state.current.revisionId,decision});
  await load(state.nextUnreviewedIndex===null?index:state.nextUnreviewedIndex);
 }catch(e){document.getElementById("message").textContent=e.message;}finally{busy=false;}
}
document.querySelectorAll("[data-decision]").forEach(b=>b.onclick=()=>act(b.dataset.decision));
document.getElementById("previous").onclick=()=>load(Math.max(0,index-1));
document.getElementById("next").onclick=()=>load(index+1);
document.getElementById("unreviewed").onclick=()=>load(state.nextUnreviewedIndex??index);
scope.onchange=()=>load(0);reason.onchange=()=>load(0);
document.querySelectorAll("[data-scope]").forEach(x=>x.onchange=async()=>{
 try{await api("/api/settings",{scope:x.dataset.scope,enabled:x.checked});await load(0);}
 catch(e){document.getElementById("message").textContent=e.message;}
});
document.getElementById("finish").onclick=async()=>{
 try{const x=await api("/api/finish",{});document.getElementById("message").textContent=
  "Review saved. Aggregate report in PowerShell; the browser can now be closed.";}
 catch(e){document.getElementById("message").textContent=e.message;}
};
document.addEventListener("keydown",e=>{
 if(e.target.tagName==="INPUT"||e.target.tagName==="SELECT")return;
 if(["1","2","3"].includes(e.key)){e.preventDefault();act(["keep","dismiss","consider-exclusion"][Number(e.key)-1]);}
});
load(0);
</script></body></html>"""


def session_load(manifest_path, state_path, proxy_root, app_base):
    manifest = read_json(manifest_path)
    validate_manifest(manifest)
    state = (validate_state(read_json(state_path), manifest)
             if state_path.exists() else blank_state(manifest))
    root = Path(proxy_root).resolve(strict=True)
    if not root.is_dir():
        raise ValueError("Configured proxy root is not a directory")
    return {"manifest": manifest, "state": state, "state_path": state_path,
            "root": root, "app_base": app_base, "lock": threading.Lock(),
            "by_id": {row["revisionId"]: row for row in manifest["candidates"]}}


def save(session):
    atomic_state(session["state_path"], session["state"], session["manifest"])


def view(session, scope, reason, index):
    if scope not in SCOPES or reason != "all" and reason not in REASONS:
        raise ValueError("Invalid context or reason")
    enabled = session["state"]["enabledScopes"][scope]
    candidates = [row for row in session["manifest"]["candidates"]
                  if enabled and (reason == "all" or reason in row["reasons"])]
    index = max(0, min(index, len(candidates)-1)) if candidates else 0
    decisions = session["state"]["decisions"][scope]
    current = candidates[index] if candidates else None
    next_unreviewed = next(
        (i for i, row in enumerate(candidates)
         if row["revisionId"] not in decisions and i != index), None)
    return {
        "scope": scope, "scopeEnabled": enabled,
        "enabledScopes": dict(session["state"]["enabledScopes"]),
        "index": index, "reviewed": sum(row["revisionId"] in decisions
                    for row in candidates),
        "remaining": sum(row["revisionId"] not in decisions for row in candidates),
        "candidates": [row["revisionId"] for row in candidates],
        "nextUnreviewedIndex": next_unreviewed,
        "current": ({"revisionId": current["revisionId"],
                      "reasons": [REASONS[c][0] for c in current["reasons"]],
                      "decision": decisions.get(current["revisionId"])}
                    if current else None),
        "photoUrl": (session["app_base"] + "/photo/" + current["revisionId"]
                     if current else None),
    }


def set_decision(session, scope, revision, action):
    if scope not in SCOPES or revision not in session["by_id"] or action not in ACTIONS:
        raise ValueError("Invalid decision")
    if not session["state"]["enabledScopes"][scope]:
        raise ValueError("Review context is not enabled")
    with session["lock"]:
        session["state"]["decisions"][scope][revision] = action
        save(session)


def set_scope(session, scope, enabled):
    if scope not in SCOPES or type(enabled) is not bool:
        raise ValueError("Invalid context setting")
    with session["lock"]:
        session["state"]["enabledScopes"][scope] = enabled
        save(session)


def request_handler(session, token):
    class Handler(BaseHTTPRequestHandler):
        server_version = "PhotoIdentitySuggestionReview/1"
        def log_message(self, *_):
            pass
        def respond(self, data, mime="application/json", status=200):
            self.send_response(status)
            for k, v in (("Content-Type", mime), ("Cache-Control", "no-store"),
                         ("Referrer-Policy", "no-referrer"),
                         ("X-Frame-Options", "DENY"),
                         ("X-Content-Type-Options", "nosniff"),
                         ("Content-Length", str(len(data)))):
                self.send_header(k,v)
            self.end_headers()
            self.wfile.write(data)
        def allowed(self, query, image=False):
            supplied = self.headers.get("X-PhotoIdentity-Suggestions")
            if image and not supplied:
                supplied = parse_qs(query).get("token", [None])[0]
            return secrets.compare_digest(str(supplied or ""), token)
        def do_GET(self):
            parsed=urlsplit(self.path)
            if parsed.path=="/":
                return self.respond(HTML.replace("__TOKEN__",token).encode(),
                                    "text/html; charset=utf-8")
            if not self.allowed(parsed.query,image=parsed.path.startswith("/api/image/")):
                return self.respond(b"Unauthorized","text/plain",403)
            try:
                if parsed.path=="/api/state":
                    q=parse_qs(parsed.query)
                    with session["lock"]:
                        data=view(session,q.get("scope",["archive-cleanup"])[0],
                                  q.get("reason",["all"])[0],
                                  int(q.get("index",["0"])[0]))
                    return self.respond(json.dumps(data).encode())
                if parsed.path.startswith("/api/image/"):
                    revision=unquote(parsed.path[len("/api/image/"):])
                    if revision not in session["by_id"]:
                        return self.respond(b"Not found","text/plain",404)
                    data=read_proxy(session["root"],session["by_id"][revision])
                    return self.respond(data,"image/jpeg")
            except (ValueError,OSError,KeyError):
                return self.respond(b"Invalid request","text/plain",400)
            return self.respond(b"Not found","text/plain",404)
        def do_POST(self):
            parsed=urlsplit(self.path)
            if not self.allowed(parsed.query):
                return self.respond(b"Unauthorized","text/plain",403)
            try:
                length=int(self.headers.get("Content-Length","-1"))
                if length<2 or length>2048:raise ValueError("Body too large")
                payload=json.loads(self.rfile.read(length))
                if not isinstance(payload,dict):raise ValueError("Invalid body")
                if parsed.path=="/api/decide" and set(payload)=={
                        "scope","revisionId","decision"}:
                    set_decision(session,payload["scope"],
                                 payload["revisionId"],payload["decision"])
                elif parsed.path=="/api/settings" and set(payload)=={
                        "scope","enabled"}:
                    set_scope(session,payload["scope"],payload["enabled"])
                elif parsed.path=="/api/finish" and not payload:
                    with session["lock"]:
                        save(session)
                    self.respond(b"{}")
                    threading.Thread(target=self.server.shutdown,daemon=True).start()
                    return
                else:raise ValueError("Invalid operation")
                return self.respond(b"{}")
            except (ValueError,OSError,TypeError,KeyError):
                return self.respond(b"Invalid review","text/plain",400)
    return Handler


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--manifest",required=True)
    parser.add_argument("--state",required=True)
    parser.add_argument("--proxy-root",required=True)
    parser.add_argument("--app-base",default="http://127.0.0.1:5080")
    parser.add_argument("--port",type=int,default=5188)
    parser.add_argument("--no-open",action="store_true")
    args=parser.parse_args()
    if (not 1024<=args.port<=65535 or
            args.app_base not in ("http://127.0.0.1:5080",
                                  "http://localhost:5080")):
        parser.error("Only known local app-base URLs and loopback ports are supported")
    try:
        a=private_path(args.manifest,must_exist=True)
        b=private_path(args.state,must_exist=False)
        if a==b:raise ValueError("Source manifest and state must be separate")
        session=session_load(a,b,args.proxy_root,args.app_base)
        token=secrets.token_urlsafe(24)
        with ThreadingHTTPServer(("127.0.0.1",args.port),
                                 request_handler(session,token)) as server:
            address="http://127.0.0.1:"+str(args.port)
            print("Local manual-quality suggestion reviewer: "+address,flush=True)
            print("Keep/Dismiss/Consider; Finish & Report ends this session.",flush=True)
            if not args.no_open:webbrowser.open(address)
            try:server.serve_forever()
            except KeyboardInterrupt:pass
        with session["lock"]:
            save(session)
            print(json.dumps(aggregate(session["manifest"],session["state"]),
                             indent=2,allow_nan=False),flush=True)
    except (ValueError,OSError,TypeError,KeyError) as ex:
        raise SystemExit("Suggestion reviewer failed ("+type(ex).__name__+")") from None


if __name__=="__main__":
    main()
