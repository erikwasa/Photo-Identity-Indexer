---
id: WI-0079
title: Investigate included-folder synchronization timeout and scaling
milestone: M21
status_source: ../status/work-items.yaml
depends_on: [WI-0041]
related_adrs: [ADR-0007]
affected_modules: [PhotoIdentity.Api, PhotoIdentity.Web, PhotoIdentity.Persistence.Sqlite, PhotoIdentity.Source.Local]
---

# WI-0079: Investigate included-folder synchronization timeout and scaling

## Priority

**Critical.** This is the first issue to investigate after the 2026-08-26 consolidated maintainer verification.

## Problem statement

The maintainer reports that **Sync included folders** is taking increasingly long to complete as the real catalogue/archive grows. Recent attempts have failed from the browser with:

```text
net_http_request_timedout, 100
```

The observed trend is important: this is not only a single transient timeout. Repeated synchronization attempts have become progressively slower, and the current operator workflow can now exceed the browser/request lifetime.

The synchronization operation is expected to be safe to repeat, discover new files in previously included folders, avoid reprocessing unchanged immutable revisions unnecessarily, and remain usable as archive coverage grows. The current behavior threatens that operating model.

## Investigation objective

Establish where synchronization time is being spent, whether work grows linearly or worse with catalogue size, and whether a long-running catalogue synchronization is incorrectly coupled to one browser HTTP request.

Do **not** choose or implement a fix until the investigation has produced evidence for the dominant cost and the maintainer has reviewed the solution options.

## Investigation questions

- What exact endpoint/application path is invoked by **Sync included folders**, and which work is performed synchronously before the request returns?
- How does runtime change with number of included roots, folders, files, catalogue revisions and previously analyzed files?
- Is every synchronization rescanning all included folders and/or revalidating unchanged files?
- Are filesystem enumeration, SHA-256 verification, SQLite queries/writes, OneDrive state checks, metadata inspection, archive advancement or UI result construction responsible for material portions of the delay?
- Are there repeated per-file database queries or other N+1 behavior whose cost grows with catalogue size?
- Does the browser timeout merely hide a server operation that continues safely, or does request cancellation abort/partially cancel synchronization?
- Is durable/background orchestration more appropriate for this operation, similar to the browser-lifetime lesson learned for GeoNames, or can the synchronous path remain bounded after eliminating avoidable work?
- What progress/status information is needed so the operator can distinguish scanning, reconciliation, useful changes and completion?

## Evidence to capture

Use the maintainer catalogue where safe and add instrumentation before optimization if existing diagnostics are insufficient. Record at least:

- included-root count;
- directories/files enumerated;
- candidate/new/changed/unchanged revision counts;
- elapsed time per major synchronization phase;
- database query/write counts or representative timings where practical;
- hashing/verification counts and bytes read where practical;
- OneDrive/local-state checks;
- request duration and whether server work survives browser cancellation;
- repeat-run timings with no filesystem changes;
- timing after adding a small new folder to an otherwise unchanged included hierarchy.

## Safety constraints

- Preserve immutable revision/hash guarantees.
- Do not mark an unchanged file as analyzed merely to make synchronization faster.
- Do not weaken OneDrive/locality checks or cause unbounded hydration.
- Repeated synchronization must remain idempotent and must not create duplicate catalogue revisions/jobs.
- Do not hide a long operation solely by increasing the browser timeout without understanding the scaling problem.

## Investigation log — 2026-08-26 static trace

The first code trace identified the synchronous request path and two potentially dominant scaling costs.

### Request path and cancellation

- The Archive page calls `POST api/archive/sync` and remains busy until the request completes.
- The WebAssembly `HttpClient` is registered with only a base address; Photo Identity does not configure a longer timeout for archive synchronization.
- `ArchiveEndpoints.SyncAsync` awaits `LocalArchiveSyncCoordinator.SyncAsync` and passes the HTTP request cancellation token through the coordinator, source enumeration, SHA-256 reads and SQLite writes before building the returned archive status.
- The synchronization therefore has no durable/background boundary today. If request cancellation reaches ASP.NET, the propagated token can cancel the scan/hash/persistence work. The diagnostic slice emits `cancelled=true` when that path is observed.

### Scaling candidates found before measurement

1. **Every local supported image is content-hashed on every sync.** `SqliteArchiveSourceCatalogueScanner` opens each local file and computes SHA-256 before recording its observation, even when size/last-write metadata and the previously verified revision are unchanged. A no-change run therefore remains proportional to total locally available archive bytes, not only to changed files.
2. **Observation persistence is per-file and transaction-heavy.** `RecordScanObservationAsync` ensures schema, opens a SQLite connection/transaction, upserts source/asset/availability, reads existing observation/latest revision, upserts the revision when local, updates the observation and commits for each enumerated file. This creates repeated per-file database work that can become material as coverage grows.
3. **The OneDrive-aware source performs a complete recursive filesystem/attribute scan before yielding assets.** Supported and unsupported paths are collected and sorted first. That phase includes directory/file enumeration, OneDrive file-attribute checks and `FileInfo` metadata reads.
4. **Missing-item reconciliation runs after each normalized included root.** The current assets index is keyed by source/deletion/source-key, but the scoped update uses a `substr(source_key, ...)` prefix expression; measurement determines whether this matters materially compared with hashing and per-file writes.
5. **Status construction is still inside the same request.** After synchronization, the API rebuilds total and per-included-folder status before returning.

### Diagnostic slice

PR #208 initially added measurement only. Each normalized included folder and the aggregate run emit a launcher-log line prefixed with:

```text
[WI-0079 sync diagnostics]
```

The diagnostics record total folder-sync time, source scan time, directories/files enumerated, OneDrive/local-state checks, hashed file count/bytes/time, observation write count/persistence time, missing-item reconciliation time and request cancellation.

Packaged launcher stdout/stderr logs are under:

```text
%LOCALAPPDATA%\PhotoIdentity\launcher-logs
```

## Real-catalogue evidence — 2026-08-26

The maintainer ran the PR #208 diagnostic package against the real catalogue. The previously observed browser timeout did **not** reproduce during these measurement runs; both completed normally with `cancelled=false`. The operation nevertheless remained long enough to characterize the scaling problem.

### No-change repeat

The second synchronization was run without filesystem changes:

```text
Sync complete: 0 new revision(s), 584 unchanged local, 1602 online-only, 1 downloading.
[WI-0079 sync diagnostics] cancelled=false included_folders=12 total_ms=37831.2
  directories=61 files=2337 status_checks=2771
  hashed_files=584 hashed_bytes=2053956759
  source_scan_ms=390.8 hash_ms=6988.1
  observation_writes=2187 persistence_ms=30380.0
  missing_reconcile_ms=20.8
```

Approximate contribution to the 37.8-second request:

- observation persistence: **30.38 s / 80.3%**;
- SHA-256 hashing: **6.99 s / 18.5%**;
- source enumeration/OneDrive inspection: **0.39 s / 1.0%**;
- missing reconciliation: negligible.

The no-change run therefore re-read and SHA-256 hashed approximately **2.05 GB** across all 584 local images even though no immutable revision changed.

### Small-change run

After adding a small intended set of images, synchronization completed with:

```text
[WI-0079 sync diagnostics] cancelled=false included_folders=12 total_ms=44327.0
  directories=61 files=2384 status_checks=2843
  hashed_files=620 hashed_bytes=2128435446
  source_scan_ms=440.3 hash_ms=7920.9
  observation_writes=2223 persistence_ms=35894.2
  missing_reconcile_ms=22.6
```

The cost shape remained essentially unchanged: per-file persistence dominated, repeated local hashing was secondary, and filesystem/OneDrive scanning was small. This distinguishes the issue from WI-0076 model-session throughput and does not support OneDrive enumeration as the dominant cause.

## Correction strategies considered

### Strategy 1 — batched persistence, retain unconditional local hashing

Reuse one SQLite connection/transaction per included folder and remove repeated per-file schema/connection/transaction overhead, but continue SHA-256 hashing every local file on every sync.

- **Safety:** strongest continuation of current verification semantics.
- **Expected benefit:** removes the measured ~80% dominant persistence cost.
- **Remaining scaling cost:** no-change runs still read/hash every local byte; measured baseline was ~7 seconds for ~2.05 GB and grows with locally available archive bytes.

### Strategy 2 — incremental verified-baseline reuse + batched persistence

Batch observation persistence and skip SHA-256 only for a continuously present local asset whose prior state is `verified` and whose current **size, last-write timestamp and media type** exactly match the retained verified baseline. New, changed, reappearing, unverified, `needs-source-verification`, and legacy-without-baseline local files still require SHA-256 before establishing/reselecting an immutable revision.

- **Safety:** preserves SHA-256 as the authority whenever a change is detected or trust is incomplete; ordinary sync treats an exact metadata match as evidence that the already-verified immutable revision can be reused.
- **Tradeoff:** a deliberately modified file that preserves all three metadata values could escape an ordinary incremental sync. A future explicit full-verification/audit operation can provide re-hash-on-demand semantics without charging every routine sync for all local bytes.
- **Expected benefit:** removes both measured dominant repeat costs while preserving full coverage/OneDrive state scanning.

### Strategy 3 — durable/background sync orchestration

Move synchronization behind a durable server-side run state and expose progress/polling rather than holding one browser request open.

- **Benefit:** eliminates browser lifetime as a completion boundary.
- **Limitation:** by itself it does not reduce the measured 38–44 second workload. It remains a possible follow-up if optimized synchronous runs can still approach client lifetime at future catalogue scale.

## Maintainer decision — Strategy 2

On **2026-08-26**, the maintainer selected **Strategy 2 — incremental verified-baseline reuse + batched persistence**.

Implementation contract:

1. Preserve recursive scanning of every included folder and current OneDrive availability checks; sync must still discover new, missing and availability-changing items.
2. Preload retained observation/revision baselines for the included scope before deciding whether a local original needs content verification.
3. Reuse a verified immutable revision without reopening the original only when the asset was continuously present, prior verification state is `verified`, and size + last-write timestamp + media type exactly match the retained verified baseline.
4. Hash new, changed, reappearing, unverified, `needs-source-verification`, and legacy-without-baseline local files with SHA-256 before changing/confirming immutable revision identity.
5. Never hydrate online-only files during synchronization. Existing online-only metadata-divergence/source-verification semantics remain authoritative.
6. Persist one included-folder scan as a batched SQLite transaction rather than one connection/transaction per file.
7. Preserve missing-item reconciliation, idempotency, duplicate prevention and downstream archive-analysis state.
8. Retain diagnostics and add counters for metadata-based revision reuse and baseline-read time.

## Implementation verification plan

Automated regression coverage must prove at least:

- a first local scan hashes content and creates the immutable revision;
- a metadata-stable repeat scan does not reopen/hash the local original, reports it unchanged and reuses the verified revision;
- a metadata-changing local file is hashed again and can create/select a new immutable revision;
- a previously missing asset that reappears online-only is not silently trusted from its old baseline and remains source-verification work;
- parent-folder expansion still reuses existing assets, discovers new children and preserves normalized coverage behavior;
- the diagnostic counters distinguish `metadata_reused` files from files actually hashed.

After CI, run the new packaged build on the same real catalogue and capture a no-change repeat plus a small-change run. Compare directly with the diagnostic baseline above. The expected result is that a no-change run hashes **zero** metadata-stable verified local files and that persistence time drops materially from the ~30-second per-file-transaction baseline. Do not select a fixed production time threshold until the optimized real-catalogue measurement is available.

## Investigation acceptance criteria

- [x] The timeout is reproduced or otherwise characterized against a realistically sized included-folder set.
- [x] End-to-end and per-phase timing evidence identifies the dominant cost(s).
- [x] Repeat-run behavior with no changes is measured separately from a run with a small number of new files.
- [x] The effect of browser/request cancellation is known: synchronization is not durable and propagates request cancellation; the diagnostic measurement runs completed without cancellation.
- [x] Catalogue-size scaling is characterized well enough to distinguish expected linear scanning from avoidable repeated work.
- [x] At least two viable correction strategies are compared with safety, complexity and operator-experience tradeoffs.
- [x] The maintainer selects the implementation direction before product-code changes begin.
- [x] The eventual implementation plan includes regression/performance evidence that prevents synchronization time from silently degrading again.

## Source finding

During the final consolidated M19/M20 maintainer verification on 2026-08-26, all planned acceptance checks passed, but the maintainer separately reported increasingly slow **Sync included folders** requests and recent failures with `net_http_request_timedout, 100`. This issue is deliberately separated from already-passed M19/M20 acceptance and from WI-0076 archive analysis model-session throughput.
