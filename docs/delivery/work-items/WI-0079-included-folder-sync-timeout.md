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

The first code trace identified the synchronous request path and two potentially dominant scaling costs. These are hypotheses until measured against the maintainer catalogue.

### Request path and cancellation

- The Archive page calls `POST api/archive/sync` and remains busy until the request completes.
- The WebAssembly `HttpClient` is registered with only a base address; Photo Identity does not configure a longer timeout for archive synchronization.
- `ArchiveEndpoints.SyncAsync` awaits `LocalArchiveSyncCoordinator.SyncAsync` and passes the HTTP request cancellation token through the coordinator, source enumeration, SHA-256 reads and SQLite writes before building the returned archive status.
- The synchronization therefore has no durable/background boundary today. A browser/client cancellation can cancel the server operation; the diagnostic slice records an explicit `cancelled=true` marker when that cancellation reaches the coordinator.

### Scaling candidates found before measurement

1. **Every local supported image is content-hashed on every sync.** `SqliteArchiveSourceCatalogueScanner` opens each local file and computes SHA-256 before recording its observation, even when size/last-write metadata and the previously verified revision are unchanged. A no-change run therefore remains proportional to total locally available archive bytes, not only to changed files.
2. **Observation persistence is per-file and transaction-heavy.** `RecordScanObservationAsync` ensures schema, opens a SQLite connection/transaction, upserts source/asset/availability, reads existing observation/latest revision, upserts the revision when local, updates the observation and commits for each enumerated file. This creates repeated per-file database work that can become material as coverage grows.
3. **The OneDrive-aware source performs a complete recursive filesystem/attribute scan before yielding assets.** Supported and unsupported paths are collected and sorted first. That phase includes directory/file enumeration, OneDrive file-attribute checks and `FileInfo` metadata reads.
4. **Missing-item reconciliation runs after each normalized included root.** The current assets index is keyed by source/deletion/source-key, but the scoped update uses a `substr(source_key, ...)` prefix expression; measurement will determine whether this matters materially compared with hashing and per-file writes.
5. **Status construction is still inside the same request.** After synchronization, the API rebuilds total and per-included-folder status before returning. This is expected to be smaller than full content hashing but remains part of end-to-end request time and should be separated if measurements show otherwise.

### Diagnostic slice

Branch `agent/WI-0079-sync-diagnostics` adds measurement only; it deliberately does not skip hashes, batch SQLite writes, change request lifetime or alter archive state semantics.

Each normalized included folder emits a launcher-log line prefixed with:

```text
[WI-0079 sync diagnostics]
```

The line records:

- total folder-sync milliseconds;
- source scan milliseconds;
- directories/files enumerated;
- OneDrive/local-state status checks;
- hashed file count and bytes;
- SHA-256 elapsed milliseconds;
- observation write count and elapsed persistence milliseconds; and
- missing-item reconciliation milliseconds.

A completed synchronization also emits aggregate totals. If the request cancellation token is observed, the coordinator emits `cancelled=true`, completed-folder count and elapsed time before rethrowing cancellation.

Packaged launcher stdout/stderr logs are under:

```text
%LOCALAPPDATA%\PhotoIdentity\launcher-logs
```

A convenient PowerShell extraction after a test run is:

```powershell
Get-ChildItem "$env:LOCALAPPDATA\PhotoIdentity\launcher-logs" -File |
  Sort-Object LastWriteTime -Descending |
  Select-Object -First 4 |
  ForEach-Object { Select-String -Path $_.FullName -Pattern "WI-0079 sync diagnostics" }
```

For the evidence pass, capture at least two runs on the same build:

1. **No-change repeat:** run **Sync included folders** twice without changing the archive between runs and retain the second run's diagnostic lines.
2. **Small-change run:** add a small new folder/file set inside already intended archive coverage, synchronize again, and retain the diagnostic lines.

If the browser times out, retain the matching `cancelled=true` line if present. If no cancellation line is emitted and server work later logs a normal completion, that would instead show that browser timeout and server lifetime are decoupled in the deployed hosting path.

Do not commit the launcher log itself because it is a private operational artefact. Copy only aggregate counters/timings needed for the WI-0079 evidence record.

## Investigation acceptance criteria

- [ ] The timeout is reproduced or otherwise characterized against a realistically sized included-folder set.
- [ ] End-to-end and per-phase timing evidence identifies the dominant cost(s).
- [ ] Repeat-run behavior with no changes is measured separately from a run with a small number of new files.
- [ ] The effect of browser/request cancellation is known: server work either continues durably or cancellation semantics are explicitly documented.
- [ ] Catalogue-size scaling is characterized well enough to distinguish expected linear scanning from avoidable super-linear/repeated work.
- [ ] At least two viable correction strategies are compared with safety, complexity and operator-experience tradeoffs.
- [ ] The maintainer selects the implementation direction before product-code changes begin.
- [ ] The eventual implementation plan includes regression/performance evidence that prevents synchronization time from silently degrading again.

## Source finding

During the final consolidated M19/M20 maintainer verification on 2026-08-26, all planned acceptance checks passed, but the maintainer separately reported increasingly slow **Sync included folders** requests and recent failures with `net_http_request_timedout, 100`. This issue is deliberately separated from already-passed M19/M20 acceptance and from WI-0076 archive analysis model-session throughput.
