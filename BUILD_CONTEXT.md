# Build context

This file is intentionally a short handoff for the next development or verification session. It should describe only the current focus, the next concrete step and the small set of documents needed to continue.

Formal work-item lifecycle status and evidence are resolved by `PhotoIdentity.Docs` from the current registry plus archived terminal history.

## Current focus

**WI-0070 — Streamline pull-request validation and stabilize integration tests** is the active CI optimization item.

Slice 1 merged through PR #176. Slice 2 is implemented in PR #177 and validated by fully green workflow #1118. Two isolated Windows integration jobs remain sequential internally and use timing-balanced runtime discovery plus exact TRX coverage checks. The final #1118 shard executions were 1m36 and 1m15, so integration is no longer the pull-request bottleneck.

The required PR workflow is now about 6m17 end to end. `build-and-test` finishes in roughly 4m04; package verification is the last job. Slice 3 should therefore focus on published review/package/launcher work and runner reuse/path-awareness instead of adding more integration parallelism. Slice 2 increased the full-gate Windows runner footprint to roughly 19–20 runner-minutes, so Slice 3 should also recover runner cost where possible.

Four independently observed transient-500 endpoint tests remain temporarily listed in `.github/flaky-integration-tests.txt`. They execute once on every workflow in a visible, non-blocking diagnostic step with TRX/JSON evidence and **no retries**. Proposed WI-0071 owns stabilization; each entry requires a host/root-cause fix plus three consecutive clean diagnostic runs before returning to required coverage. A newer viewer-preview transient 500 was fixed by migrating that class to the shared background-worker-disabled test host and did not require quarantine.

WI-0066 implementation is complete through merged PR #173 and remains in review pending its focused maintainer browser verification. WI-0067 is also active on the M19 feature track and must be preserved when CI branches are synchronized with `main`.

## Next concrete step

1. Merge PR #177 after the latest-head Actions run confirms the documentation-only Slice 2 evidence/handoff commits remain green.
2. Start WI-0070 Slice 3 from current `main`.
3. Make package and launcher verification conditional on relevant PR paths while retaining unconditional comprehensive validation on `main`.
4. Reduce PR published review smoke to behavior that adds unique signal beyond API integration tests; retain the broader published-app check on `main` if still valuable.
5. Investigate publish/package artifact reuse and dependency caching where measurement shows worthwhile savings, then compare both PR wall-clock and total Windows runner minutes with workflow #1118.
6. WI-0071 later migrates/fixes the quarantined API hosts and restores each test after its stabilization change plus three consecutive clean diagnostic runs.

## Relevant files

- `docs/delivery/status/work-items.yaml`
- `docs/delivery/work-items/WI-0070-pr-validation-streamlining.md`
- `docs/delivery/work-items/WI-0071-stabilize-quarantined-integration-tests.md`
- `docs/operations/testing-and-ci-strategy.md`
- `AGENTS.md`
- `.github/workflows/build.yml`
- `.github/flaky-integration-tests.txt`
- `.github/scripts/run-integration-shards.ps1`
- `.github/scripts/run-flaky-integration-diagnostics.ps1`
- `.github/scripts/summarize-test-timings.ps1`
- `.github/test-timing-baseline.json`
- `tests/PhotoIdentity.Integration.Tests/ResumableBatchProcessorTests.cs`
- `tests/PhotoIdentity.Integration.Tests/PhotoIdentityApiTestFactory.cs`
- `tests/PhotoIdentity.Integration.Tests/CollectionViewerPreviewApplicationTests.cs`

## Repository validation

```powershell
./build.ps1
./test.ps1
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
./verify-review.ps1 -Mode Smoke -Configuration Release
```
