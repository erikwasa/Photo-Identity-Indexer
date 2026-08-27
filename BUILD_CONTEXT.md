# Build context

This file is intentionally a short handoff for the next development or verification session. It should describe only the current focus, the next concrete step and the small set of documents needed to continue.

Formal work-item lifecycle status and evidence are resolved by `PhotoIdentity.Docs` from the current registry plus archived terminal history.

## Current focus

**WI-0076 — Improve archive processing throughput** is the active engineering item.

The maintainer chose a measurement-first baseline from current `main`. The previous session-reuse PR #200 remains unmerged and untouched as a later optimization candidate. Do not apply session reuse, new concurrency, hydration prefetch, verification caching or loop tuning until the baseline identifies the dominant cost.

The active branch is `agent/WI-0076-throughput-metrics`. It adds process-local aggregate throughput diagnostics and a reset/snapshot HTTP contract without changing archive-processing semantics. The report measures sync, OneDrive wait, verification/hash activity, metadata, model-session setup/lifetime, decode/detect/align/embed/persistence, proxies, face-review derivatives and hydration/release requests.

PR #209 for WI-0080 merged on 2026-08-26. Maintainer visual confirmation is intentionally deferred, so WI-0080 is not yet treated as completed.

WI-0081 remains ready but is deferred while WI-0076 measurement work is active.

## Next concrete step

1. Finish the WI-0076 metrics-only PR and obtain exact-head CI.
2. Build/use the resulting Windows package with a disposable benchmark catalogue.
3. Run the same fixed 100–200 image sample twice following `docs/operations/archive-throughput-benchmark.md`: first with originals already local, then with the same originals online-only.
4. Compare wall-clock throughput, OneDrive wait share, stage timings, model-session initialization frequency and aggregate full-file hash reads.
5. Select the next WI-0076 optimization only from measured evidence; PR #200 session reuse is one candidate, not a predetermined fix.

## Relevant files

- `docs/delivery/work-items/WI-0076-archive-throughput.md`
- `docs/operations/archive-throughput-benchmark.md`
- `src/PhotoIdentity.Worker/ArchiveThroughputMetrics.cs`
- `src/PhotoIdentity.Worker/ArchiveAnalysisProcessing.cs`
- `src/PhotoIdentity.Worker/LocalInspectionJobHandler.cs`
- `src/PhotoIdentity.Worker/LocalArchiveSyncCoordinator.cs`
- `src/PhotoIdentity.Api/ArchiveBoundedAnalysisService.cs`
- `src/PhotoIdentity.Api/ArchiveAdvancementHostedService.cs`
- `src/PhotoIdentity.Api/ArchiveEndpoints.cs`
- `docs/delivery/status/work-items.yaml`
- `AGENTS.md`

## Repository validation

```powershell
./build.ps1
./test.ps1
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
./verify-review.ps1 -Mode Smoke -Configuration Release
```
