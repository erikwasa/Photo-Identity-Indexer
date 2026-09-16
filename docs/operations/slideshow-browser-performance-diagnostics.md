# Slideshow browser performance diagnostics

WI-0108 introduced this browser-side diagnostic after direct-server slideshow serving had been shown to remain bounded across a representative ordered sequence. WI-0129 extends the meaning of the presentation timing so the dual-layer renderer is measured through decode readiness and the point at which the destination layer is actually visible.

The browser diagnostic is deliberately identity-free. It records at most the first 50 displayed slideshow images in one browser slideshow session and submits only:

- a one-based sample sequence number;
- elapsed milliseconds from insertion of the staged slideshow image until that image becomes the visible presentation layer after load/decode and any crossfade;
- same-origin Resource Timing duration when the browser exposes it; and
- whether an application-owned prefetch for that same resource had already completed before the staged slideshow image was inserted.

It does **not** submit or persist collection names, collection identifiers, revision identifiers, filenames, image URLs, source paths, image content, credentials or embeddings.

## What the metrics mean

The existing throughput endpoint exposes the browser samples as:

- `slideshow-browser-image-presentation` — aggregate staged-DOM-to-visible-layer time. Under WI-0129 this includes browser load/decode readiness plus any non-reduced-motion crossfade, and therefore represents user-visible presentation latency rather than merely the image `load` event;
- `slideshow-browser-image-resource` — aggregate browser Resource Timing duration when available;
- `slideshow-browser-image-presentation-position-NN` — the bounded per-sequence visible-presentation timing used to detect growth as playback advances;
- `slideshow-browser-prefetch-hits` — the application-owned prefetch had completed before the staged image was inserted;
- `slideshow-browser-prefetch-misses` — no completed application-owned prefetch was visible at staging time.

The browser presentation helper also keeps identity-free per-element readiness timestamps in memory so tests and browser debugging can distinguish `decode()` readiness from final visibility. Those timestamps are not sent to the server and disappear with the page.

The browser instrumentation is best-effort. Failure to post a timing sample never blocks image display, navigation, autoplay, fullscreen or protected-mode behavior.

The prefetch-hit classification uses the slideshow's explicit in-memory prefetch state captured when the staged `<img>` is inserted. It does not infer a hit from only the latest Resource Timing entry, because a subsequent displayed-image request can otherwise hide evidence of an earlier completed prefetch.

## Real-phone verification workflow

Start the current PostgreSQL-authoritative application normally and verify `/health` first. On the Windows host, reset process-local diagnostics:

```powershell
.\measure-slideshow-browser-performance.ps1 -Reset
```

Then, on the supported phone/browser:

1. open the read-only slideshow library;
2. start the representative saved Smart Collection used for slideshow acceptance;
3. advance through at least 10 distinct photos, preferably the 11-photo representative collection already used by the direct-server probe;
4. reproduce the navigation style being verified: autoplay, tap or swipe as applicable;
5. include both portrait and landscape photos and at least one loop boundary when verifying WI-0129;
6. exit or leave the slideshow after enough samples have been collected.

Back on Windows, capture the report:

```powershell
.\measure-slideshow-browser-performance.ps1
```

The generated JSON is written under the current user's temporary `PhotoIdentity\slideshow-performance` directory unless `-OutputPath` is supplied.

The report contains the browser timing/prefetch evidence plus the relevant server-side `collection-viewer-preview-open`, `original-verification-hash`, `api-collection-request`, `api-slideshow-request` stages and aggregate original hash-read statistics from the same diagnostics generation. No second manual diagnostics query is required for the normal comparison.

## 2026-09-11 phone baseline before prefetch deduplication

The representative 11-photo slideshow was subjectively responsive on the maintainer's phone; the corrective work below is therefore preventive rather than evidence of a current unacceptable UX.

The browser report recorded:

- 11 displayed-image samples;
- presentation time averaging about 168 ms and peaking at 389 ms under the pre-WI-0129 load-event definition;
- Resource Timing averaging about 156 ms and peaking at about 367 ms;
- no systematic growth with slideshow position;
- 11/11 samples classified as prefetch misses by the original diagnostic.

The same diagnostics generation showed substantially more server work than the 11 displayed images required:

- 39 `collection-viewer-preview-open` operations, averaging about 40 ms;
- 50 `api-collection-request` operations, averaging about 124 ms;
- 22 `original-verification-hash` operations, averaging about 9.7 ms;
- 22 `original-open` hash reads across only five revision subjects, with one subject read up to seven times.

Code inspection matched that amplification: every prefetch refresh cleared and recreated the complete browser `Image` set, and navigation could clear the prefetched `Image` for the revision that had just become current before the displayed image finished loading.

The preventive correction keeps unchanged prefetch `Image` objects alive, treats an unchanged desired URL set as a no-op, retains the immediately previous desired generation once so a newly-current prefetched resource can be reused/coalesced, and removes only entries that remain outside the bounded desired set. The configured prefetch window itself is unchanged.

After the correction, repeat the same 11-photo phone workflow. Success does not require an exact request count, because the existing bounded previous/next prefetch semantics remain intact. Look for a material reduction from the 39 preview opens / 22 hash reads baseline, credible prefetch hits, no systematic latency growth and no regression in the already-responsive perceived experience.

Because WI-0129 changes `slideshow-browser-image-presentation` from load completion to final visible-layer completion, its values are not directly comparable to the 2026-09-11 presentation numbers. Compare Resource Timing and server stages directly; use the new presentation values to detect stalls or growth within a WI-0129 run rather than treating the added fixed crossfade duration as a regression.

## Interpretation

Compare `browserPresentationSequence` with `browserStages`:

- If visible-presentation time grows through the sequence while `slideshow-browser-image-resource` remains low, the delay is in browser decode/transition/Blazor presentation lifecycle rather than server/network transfer.
- If visible-presentation time is high and Resource Timing is similarly high, investigate phone-network/content-serving behavior and compare the API-side `collection-viewer-preview-open` and `original-verification-hash` stages from the same report.
- If a slow presentation is recorded as a prefetch hit, the resource was already available before staging; investigate decode, transition or playback state rather than server retrieval.
- If slow transitions are mostly prefetch misses, inspect whether the bounded prefetch is being retained and scheduled early enough before considering a larger prefetch window.
- If browser presentation remains bounded and practical across the representative run, the renderer is not accumulating per-photo presentation cost.

Do not change PostgreSQL query shape, immutable verification semantics, prefetch window size or image quality solely because one stage exists. Choose a correction only when the real-phone browser evidence makes that stage material.
