# Slideshow performance diagnostics

WI-0108 uses the existing process-local throughput diagnostics to attribute slideshow latency before changing PostgreSQL query shape or immutable-original verification semantics.

The diagnostics are aggregate and path-free. They do not expose collection names, revision identifiers, filenames, source paths, credentials, image content or embeddings.

## Stage metrics

The slideshow measurement slice adds these stages to `GET /api/archive/diagnostics/throughput`:

- `slideshow-library-load` — repository time used by `/api/slideshows/collections`;
- `slideshow-snapshot-creation` — saved Smart Collection snapshot creation;
- `slideshow-preparation-start` — synchronous preparation-session start work;
- `slideshow-preparation-status` — preparation status reads;
- `slideshow-prepared-original-open` — verified prepared-original resolution/open;
- `collection-viewer-preview-open` — normal viewer-preview resolution/open.

Existing `original-verification-hash` stage timing plus `original-status` and `original-open` hash-read aggregates show when full immutable SHA-256 reads occur.

The normal API-family request timings continue to include HTTP response/streaming time. The repository-level slideshow stages separate server resolution/query/hash work from the caller-observed request duration.

## Bounded real-catalogue probe

Start the normal PostgreSQL-authoritative application, identify a saved Smart Collection to measure, then run from the repository root:

```powershell
.\measure-slideshow-performance.ps1 `
    -CollectionId <smart-collection-guid>
```

The probe defaults to `http://127.0.0.1:5080`, matching the launcher's explicit IPv4 loopback style. Do not replace that with `localhost` for maintainer baselines unless hostname-resolution behavior itself is what you are testing: the 2026-09-11 Windows baseline showed an artificial roughly two-second caller delay per request through `localhost` while the server stages stayed in the tens of milliseconds.

The default probe:

1. confirms `/health` is healthy;
2. resets process-local throughput diagnostics;
3. measures saved slideshow-library loading;
4. measures snapshot creation for the selected collection;
5. downloads up to the first ten distinct viewer previews in snapshot order and records only one-based position plus elapsed milliseconds;
6. downloads the first viewer-preview three additional times and captures repeated hash-read/cache evidence;
7. writes a path-free JSON report under the current user's temporary `PhotoIdentity\slideshow-performance` directory.

Use `-SequenceItemCount` between 1 and 50 to change the bounded ordered sample and `-RepeatCount` between 1 and 10 when more repeated first-viewer requests are useful. The ordered sample is intended to test whether direct-server image latency grows with slideshow position; it does not claim to reproduce browser decode/render or prefetch scheduling.

For a representative collection with at least ten photos:

```powershell
.\measure-slideshow-performance.ps1 `
    -CollectionId <smart-collection-guid> `
    -SequenceItemCount 10
```

The report's `sequenceViewerPreviews` contains entries such as `position` and `milliseconds` only. `sequenceViewerPreviewStages` and `sequenceViewerPreviewHashReads` contain the corresponding aggregate server evidence without revision identity.

### Prepared-original measurement

Prepared-original probing is opt-in because preparation can request OneDrive hydration. Prefer an already-local, deliberately small Smart Collection—ideally the one-photo case that reproduced the long immediate-reopen delay.

For a one-photo collection:

```powershell
.\measure-slideshow-performance.ps1 `
    -CollectionId <smart-collection-guid> `
    -IncludePreparedOriginals `
    -MaximumPreparationItems 1
```

The script refuses to prepare a collection larger than five items by default. Raising `-MaximumPreparationItems` is an explicit operator decision; do not use a large archive-wide collection merely to gather timing evidence.

Prepared mode starts a temporary preparation session, waits for a terminal/ready state within the configured timeout, repeatedly downloads the first prepared original when ready, records aggregate stage/hash evidence, then ends the session. The status loop polls every 500 ms, so `preparationToTerminalMilliseconds` includes up to that polling granularity and must be interpreted together with `slideshow-preparation-start` and `slideshow-preparation-status`.

## Interpreting the baseline

Compare caller-observed milliseconds with the corresponding aggregate stages:

- A slow library request with a small `slideshow-library-load` stage points outside saved-definition repository work. A large stage points at definition loading/deserialization.
- A large `slideshow-snapshot-creation` stage points at saved-filter/current-state evaluation and snapshot ordering.
- An ordered `sequenceViewerPreviews` series that stays bounded rules out direct viewer serving as the cause of latency growth with slideshow position; browser/network/decode/prefetch timing should then be measured on the playback surface.
- Repeated `original-open` hash reads for the same repeated request sequence demonstrate that unchanged viewer/prepared-original serving is rereading the complete original for immutable verification, but the hash-stage milliseconds must be material before verification reuse is treated as the primary performance correction.
- `original-status` hash reads during preparation identify verification work performed before playback serving begins.
- If prepared-original repeat requests stay expensive after preparation is ready, compare `slideshow-prepared-original-open`, `original-verification-hash`, and caller-observed request time before changing cache/receipt semantics.

## 2026-09-11 real-catalogue evidence

With the PostgreSQL-authoritative catalogue at schema version 23 and the explicit IPv4 loopback origin:

- a one-photo collection measured library loading at about 9–12 ms, snapshot creation at about 23–27 ms, viewer-preview requests at about 38–41 ms, and prepared-original opens at about 38–50 ms;
- immutable hash reads in that one-photo sample were about 2.4–2.6 ms each;
- an 11-photo representative collection measured library loading at about 9 ms, snapshot creation at about 34 ms, the first viewer preview at about 51 ms, and repeated first-preview requests at about 34 ms;
- the 11-photo viewer sequence showed no original-open hash reads in the repeated-first-preview phase.

These results do not justify a PostgreSQL query/index rewrite or immutable-verification cache as the first correction. The next evidence target is ordered distinct-image latency, followed by browser-visible request/render/prefetch timing if the direct sequence remains bounded.
