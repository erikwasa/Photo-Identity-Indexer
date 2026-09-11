# Slideshow browser performance diagnostics

WI-0108 uses this browser-side diagnostic only after direct-server slideshow serving has been shown to remain bounded across a representative ordered sequence.

The browser diagnostic is deliberately identity-free. It records at most the first 50 displayed slideshow images in one browser slideshow session and submits only:

- a one-based sample sequence number;
- elapsed milliseconds from browser DOM presentation of the slideshow image to its `load` event;
- same-origin Resource Timing duration when the browser exposes it; and
- whether that resource had already completed before the slideshow image was presented, which is used as bounded prefetch evidence.

It does **not** submit or persist collection names, collection identifiers, revision identifiers, filenames, image URLs, source paths, image content, credentials or embeddings.

## What the metrics mean

The existing throughput endpoint exposes the browser samples as:

- `slideshow-browser-image-presentation` — aggregate DOM-presentation-to-image-load time;
- `slideshow-browser-image-resource` — aggregate browser Resource Timing duration when available;
- `slideshow-browser-image-presentation-position-NN` — the bounded per-sequence presentation timing used to detect growth as playback advances;
- `slideshow-browser-prefetch-hits` — image resources that had completed before presentation began;
- `slideshow-browser-prefetch-misses` — image resources that had not completed before presentation began, or for which no prior completed resource was visible.

The browser instrumentation is best-effort. Failure to post a timing sample never blocks image display, navigation, autoplay, fullscreen or protected-mode behavior.

## Real-phone verification workflow

Start the current PostgreSQL-authoritative application normally and verify `/health` first. On the Windows host, reset process-local diagnostics:

```powershell
.\measure-slideshow-browser-performance.ps1 -Reset
```

Then, on the supported phone/browser:

1. open the read-only slideshow library;
2. start the representative saved Smart Collection used for WI-0108 acceptance;
3. advance through at least 10 distinct photos, preferably the 11-photo representative collection already used by the direct-server probe;
4. reproduce the navigation style that previously felt slow: autoplay, tap or swipe as applicable;
5. exit or leave the slideshow after enough samples have been collected.

Back on Windows, capture the report:

```powershell
.\measure-slideshow-browser-performance.ps1
```

The generated JSON is written under the current user's temporary `PhotoIdentity\slideshow-performance` directory unless `-OutputPath` is supplied.

## Interpretation

Compare `browserPresentationSequence` with `browserStages`:

- If presentation time grows through the sequence while `slideshow-browser-image-resource` remains low, the delay is in browser/Blazor presentation lifecycle rather than server/network transfer.
- If presentation time is high and Resource Timing is similarly high, investigate phone-network/content-serving behavior and compare the API-side `collection-viewer-preview-open` and `original-verification-hash` stages from the same diagnostics generation.
- If a slow presentation is recorded as a prefetch hit, the resource was already available before the slideshow image became current; investigate rendering/playback state rather than server retrieval.
- If slow transitions are mostly prefetch misses, inspect whether the bounded prefetch window is being scheduled early enough and whether phone/network latency makes a one-image window insufficient.
- If browser presentation remains bounded and practical across the representative run, the original progressive slowdown is not reproduced and WI-0108 should move to explicit maintainer acceptance rather than speculative optimization.

Do not change PostgreSQL query shape, immutable verification semantics, prefetch window size or image quality solely because one stage exists. Choose a correction only when the real-phone browser evidence makes that stage material.
