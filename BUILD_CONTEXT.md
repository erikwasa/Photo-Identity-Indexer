# Build context

This file is intentionally a short handoff for the next development or verification session. It should describe only the current focus, the next concrete step and the small set of documents needed to continue.

Formal work-item lifecycle status and evidence are resolved by `PhotoIdentity.Docs` from the current registry plus archived terminal history.

## Current focus

**M20 — Operator polish and archive throughput** is the active delivery focus.

**WI-0073 — Polish cards, menus and archive navigation** is implemented and merged through PR #196. **WI-0074 — Filter Face Review by suggested person** is implemented and merged through PR #197. The maintainer has explicitly deferred browser verification until multiple M20 items are ready for one consolidated pass.

**WI-0072 — Integrate archive photo metadata** was implemented and merged through PR #191, and **WI-0078 — Reprocess stale photo metadata after extraction-contract changes** was implemented and merged through PR #195. Their formal lifecycle state remains constrained by deferred real-media/provider verification rather than missing implementation. Do not mark them completed without maintainer evidence.

**WI-0075 — Make GeoNames background timing configurable from launcher settings** is implemented and merged through PR #198 (`1d00a7c533299bd5139c3a02ce3157dd52fec4c1`), with exact-head workflow #1221 (`32314889279`) green.

**WI-0077 — Simplify Photo Viewer metadata and location editing** is implemented and merged through PR #199 (`10816b7e0affe13625637606ed7e74d5e58c9a89`), with exact-head workflow #1223 (`32317946797`) green. Browser verification remains deferred to the consolidated pass.

**WI-0076 — Improve archive processing throughput** is the current implementation focus on `agent/WI-0076-archive-session-reuse`. The first optimization slice preserves one governed analysis job per archive advancement but reuses the expensive `LocalInspectionJobHandler` detector/embedder session across coordinator invocations for the same catalogue, exact profile hash and full batch configuration. The shared session is serialized and exposes lightweight initialization/attempt diagnostics. This removes repeated per-image ONNX setup without changing metadata-before-analysis, SHA-256 verification, durable checkpoints, retry semantics, hydration ownership or analysis concurrency.

WI-0076 remains formally constrained by the deferred WI-0072 maintainer acceptance. Treat this as implementation against already-merged prerequisite code; do not manufacture lifecycle completion solely to unblock engineering work.

## Next concrete step

1. Validate the WI-0076 session-reuse slice in exact-head CI, including full build/integration/package lanes.
2. If green, record the PR/workflow evidence and merge only after maintainer direction.
3. During the later consolidated archive benchmark, compare the same representative >=100-image set against the observed ~100 images/hour baseline and record model-session initialization count/duration plus total throughput.
4. Use that benchmark to choose the next WI-0076 slice: redundant full-file hash reads first if local I/O dominates; bounded hydration prefetch if OneDrive wait dominates; only then consider multi-job analysis batches or loop-delay tuning.

## Relevant files

- `docs/delivery/status/work-items.yaml`
- `docs/delivery/work-items/WI-0076-archive-throughput.md`
- `src/PhotoIdentity.Worker/ArchiveAnalysisProcessing.cs`
- `src/PhotoIdentity.Worker/LocalInspectionJobHandler.cs`
- `src/PhotoIdentity.Api/ArchiveBoundedAnalysisService.cs`
- `src/PhotoIdentity.Api/ArchiveAdvancementHostedService.cs`
- `AGENTS.md`

## Repository validation

```powershell
./build.ps1
./test.ps1
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
./verify-review.ps1 -Mode Smoke -Configuration Release
```
