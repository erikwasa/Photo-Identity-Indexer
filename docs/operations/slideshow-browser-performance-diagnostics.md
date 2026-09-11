# Slideshow browser performance diagnostics

WI-0108 uses this browser-side diagnostic only after direct-server slideshow serving has been shown to remain bounded across a representative ordered sequence.

The browser diagnostic is deliberately identity-free. It records at most the first 50 displayed slideshow images in one browser slideshow session and submits only:

- a one-based sample sequence number;
- elapsed milliseconds from browser DOM presentation of the slideshow image to its `load` event;
- same-origin Resource Timing duration when the browser exposes it; and
- whether an application-owned prefetch for that same resource had already completed before the slideshow image was presented.

It does **not** submit or persist collection names, collection identifiers, revision identifiers, filenames, image URLs, source paths, image content, credentials or embeddings.

## What the metrics mean

The existing throughput endpoint exposes the browser samples as:

- `slideshow-browser-image-presentation` — aggregate DOM-presentation-to-image-load time;
- `slideshow-browser-image-resource` — aggregate browser Resource Timing duration when available;
- `slideshow-browser-image-presentation-position-NN` — the bounded per-sequence presentation timing used to detect growth as playback advances;
- `slideshow-browser-prefetch-hits` — the application-owned prefetch had completed before the displayed image became current;
- `slideshow-browser-prefetch-misses` — no completed application-owned prefetch was visible at presentation time.

The browser instrumentation is best-effort. Failure to post a timing sample never blocks image display, navigation, autoplay, fullscreen or protected-mode behavior.

The prefetch-hit classification uses the slideshow's explicit in-memory prefetch state captured when the displayed `<img>` is inserted. It does not infer a hit from only the latest Resource Timing entry, because a subsequent displayed-image request can otherwise hide evidence of an earlier completed prefetch.

## Real-phone verification workflow

Start the current PostgreSQL-authoritative application normally and verify `/health` first. On the Windows host, reset process-local diagnostics:

```powershell
.\measure-slideshow-browser-performance.ps1 -Reset
```

Then, on the supported phone/browser:

1. open the read-only slideshow library;
2. start the representative saved Smart Collection used for WI-0108 acceptance;
3. advance through at least 10 distinct photos, preferably the 11-photo representative collection already used by the direct-server probe;
4. reproduce the navigation style being verified: autoplay, tap or swipe as applicable;
5. exit or leave the slideshow after enough samples have been collected.

Back on Windows, capture the report:

```powershell
.\measure-slideshow-browser-performance.ps1
```

The generated JSON is written under the current user's temporary `PhotoIdentity\slideshow-performance` directory unless `-OutputPath` is supplied.

For request-amplification evidence from the same diagnostics generation, inspect the server aggregates before resetting again:

```powershell
$diag = Invoke-RestMethod http://127.0.0.1:5080/api/archive/diagnostics/throughput

[pscustomobject]@{
    stages = @(
        $diag.stages |
            Where-Object {
                $_.name -in @(
                    "collection-viewer-preview-open",
                    "original-verification-hash",
                    "api-collection-request",
                    "api-slideshow-request"
                )
            }
    )
    hashReads = @($diag.hashReads)
    browserCounters = @(
        $diag.counters |
            Where-Object { $_.name -like "slideshow-browser-*" }
    )
} | ConvertTo-Json -Depth 8
```

## 2026-09-11 phone baseline before prefetch deduplication

The representative 11-photo slideshow was subjectively responsive on the maintainer's phone; the corrective work below is therefore preventive rather than evidence of a current unacceptable UX.

The browser report recorded:

- 11 displayed-image samples;
- presentation time averaging about 168 ms and peaking at 389 ms;
- Resource Timing averaging about 156 ms and peaking at about 367 ms;
- no systematic growth with slideshow position;
- 11/11 samples classified as prefetch misses by the original diagnostic.

The same diagnostics generation showed substantially more server work than the 11 displayed images required:

- 39 `collection-viewer-preview-open` operations, averaging about 40 ms;
- 50 `api-collection-request` operations, averaging about 124 ms;
- 22 `original-verification-hash` operations, averaging about 9.7 ms;
- 22 `original-open` hash reads across only five revision subjects, with one subject read up to seven times.

Code inspection matched that amplification: every prefetch refresh cleared and recreated the complete browser `Image` set, and navigation could clear the prefetched `Image` for the revision that had just become current before the displayed image finished loading.

The preventive correction keeps unchanged prefetch `Image` objects alive, retains the immediately previous desired generation once so a newly-current prefetched resource can be reused/coalesced, and removes only entries that remain outside the bounded desired set. The configured prefetch window itself is unchanged.

After the correction, repeat the same 11-photo phone workflow. Success does not require an exact request count, because the existing bounded previous/next prefetch semantics remain intact. Look for a material reduction from the 39 preview opens / 22 hash reads baseline, credible prefetch hits, no systematic latency growth and no regression in the already-responsive perceived experience.

## Interpretation

Compare `browserPresentationSequence` with `browserStages`:

- If presentation time grows through the sequence while `slideshow-browser-image-resource` remains low, the delay is in browser/Blazor presentation lifecycle rather than server/network transfer.
- If presentation time is high and Resource Timing is similarly high, investigate phone-network/content-serving behavior and compare the API-side `collection-viewer-preview-open` and `original-verification-hash` stages from the same diagnostics generation.
- If a slow presentation is recorded as a prefetch hit, the resource was already available before the slideshow image became current; investigate rendering/playback state rather than server retrieval.
- If slow transitions are mostly prefetch misses, inspect whether the bounded prefetch is being retained and scheduled early enough before considering a larger prefetch window.
- If browser presentation remains bounded and practical across the representative run, the original progressive slowdown is not reproduced and WI-0108 should move to explicit maintainer acceptance rather than speculative optimization.

Do not change PostgreSQL query shape, immutable verification semantics, prefetch window size or image quality solely because one stage exists. Choose a correction only when the real-phone browser evidence makes that stage material.
