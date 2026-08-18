# Build context

This file is intentionally a short handoff for the next development or verification session. It should describe only the current focus, the next concrete step and the small set of documents needed to continue.

Formal work-item lifecycle status and evidence are resolved by `PhotoIdentity.Docs` from the current registry plus archived terminal history.

## Current focus

**WI-0070 — Streamline pull-request validation and stabilize integration tests** is the active CI optimization item.

Slice 1 merged through PR #176 and validated in workflow #1093. Slice 2 is active in PR #177. The current architecture uses two isolated Windows integration jobs, each sequential internally, while the main job runs true fast tests plus documentation, published review smoke and mixed-media checks.

Workflow #1112 validated the complete sharding/quarantine mechanics on current `main`: both required shards passed exact coverage, all four quarantined diagnostics ran once and passed with no retries, and every other job was green. It also showed the current timing plan was not acceptable: overall wall-clock was about 10m37 because shard 1 took 8m06 of test execution versus shard 2 at 2m31.

The largest outlier was an arbitrary 500-job batch accounting fixture at 157.8s. It has been right-sized to 50 jobs with the same completion/accounting/idempotency assertions. The scheduling baseline is refreshed from #1112's actual TRX timings and now targets approximately 248.2s / 248.2s per shard, with a conservative post-change estimate for the resized batch class.

Four independently observed transient-500 endpoint tests remain temporarily listed in `.github/flaky-integration-tests.txt`. They are excluded only from the required shards and still execute once on every workflow in a visible, non-blocking diagnostic step with TRX/JSON evidence and **no retries**. Proposed WI-0071 owns stabilization; each entry requires a host/root-cause fix plus three consecutive clean diagnostic runs before returning to required coverage.

WI-0066 implementation is complete through merged PR #173 and remains in review pending its focused maintainer browser verification. WI-0065 remains in review pending maintainer verification of unattended GeoNames pickup and restart/resume behavior.

## Next concrete step

1. Validate PR #177 after the 50-job fixture reduction and #1112-based shard rebalancing.
2. Confirm both required shards still execute every non-quarantined integration test exactly once and compare actual shard durations.
3. Measure total PR wall-clock and aggregate Windows runner minutes against #1093 and #1112. Keep two shards only if the result materially improves feedback time without excessive runner cost.
4. After Slice 2 is stable, proceed to WI-0070 Slice 3: right-size published review smoke and launcher/package checks while preserving comprehensive `main` coverage.
5. WI-0071 later migrates/fixes the quarantined API hosts and restores each test after three consecutive clean diagnostic runs following stabilization.

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

## Repository validation

```powershell
./build.ps1
./test.ps1
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
./verify-review.ps1 -Mode Smoke -Configuration Release
```
