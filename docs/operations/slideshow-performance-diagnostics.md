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

The default probe:

1. confirms `/health` is healthy;
2. resets process-local throughput diagnostics;
3. measures saved slideshow-library loading;
4. measures snapshot creation for the selected collection;
5. downloads the first viewer-preview three times and captures repeated hash-read evidence;
6. writes a path-free JSON report under the current user's temporary `PhotoIdentity\slideshow-performance` directory.

Use `-RepeatCount` between 1 and 10 when more repeated viewer requests are useful.

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

Prepared mode starts a temporary preparation session, waits for a terminal/ready state within the configured timeout, repeatedly downloads the first prepared original when ready, records aggregate stage/hash evidence, then ends the session.

## Interpreting the first baseline

Compare caller-observed milliseconds with the corresponding aggregate stages:

- A slow library request with a small `slideshow-library-load` stage points outside saved-definition repository work. A large stage points at definition loading/deserialization.
- A large `slideshow-snapshot-creation` stage points at saved-filter/current-state evaluation and snapshot ordering.
- Repeated `original-open` hash reads for the same repeated request sequence demonstrate that unchanged viewer/prepared-original serving is rereading the complete original for immutable verification.
- `original-status` hash reads during preparation identify verification work performed before playback serving begins.
- If prepared-original repeat requests stay expensive after preparation is ready, compare `slideshow-prepared-original-open`, `original-verification-hash`, and caller-observed request time before changing cache/receipt semantics.

Use the evidence to choose the next focused WI-0108 slice. Do not add PostgreSQL indexes or weaken immutable verification based only on code inspection.
