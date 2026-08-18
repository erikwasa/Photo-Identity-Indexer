# Build context

This file is intentionally a short handoff for the next development or verification session. It should describe only the current focus, the next concrete step and the small set of documents needed to continue.

Formal work-item lifecycle status and evidence are resolved by `PhotoIdentity.Docs` from the current registry plus archived terminal history.

## Current focus

**WI-0070 — Streamline pull-request validation and stabilize integration tests** is the active M00 implementation item.

Slice 1 merged through PR #176 and validated in workflow #1093. The 294-test integration assembly took about 3m37s wall-clock and recorded 216.9s of test duration. Two classes accounted for about 78.7s of that recorded time.

Slice 2 is active in PR #177. Same-runner process concurrency was rejected after workflow #1095 showed runner contention. The current design uses two isolated Windows integration jobs, each sequential internally and timing-balanced from the #1093 evidence. Workflow #1104 proved the scheduler/coverage checks: shard 2 passed 159/159 and shard 1 executed all 135 assigned tests exactly once, but one older ad-hoc API test again returned a transient HTTP 500.

Four independently observed transient-500 endpoint tests are temporarily listed in `.github/flaky-integration-tests.txt`. They are excluded from the required shards but still execute once on every workflow in a visible, non-blocking diagnostic step with TRX/JSON evidence and **no retries**. Proposed WI-0071 owns stabilization; each entry requires a host/root-cause fix plus three consecutive clean diagnostic runs before returning to required coverage.

The Slice 2 fast-test filter also excludes both integration namespaces after #1093 showed 16 integration tests using `PhotoIdentity.Integration.Tests` were accidentally duplicated in the fast phase.

WI-0066 implementation is complete through merged PR #173 and remains in review pending its focused maintainer browser verification. WI-0065 remains in review pending maintainer verification of unattended GeoNames pickup and restart/resume behavior.

## Next concrete step

1. Validate PR #177 with the temporary flaky diagnostic lane: both required isolated shards must pass exact coverage while quarantined tests execute once without blocking unrelated work.
2. Confirm documentation validation accepts registered proposed WI-0071 and its M00 membership.
3. Compare successful Slice 2 PR wall-clock and aggregate Windows runner minutes with workflow #1093; retain the smallest isolated shard count that meets the feedback-time goal reliably.
4. After Slice 2 is stable, proceed to WI-0070 Slice 3: right-size published review smoke and launcher/package checks while preserving comprehensive `main` coverage.
5. WI-0071 later migrates/fixes the quarantined API hosts and restores each test after three consecutive clean diagnostic runs.

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
- `tests/PhotoIdentity.Integration.Tests/PhotoIdentityApiTestFactory.cs`

## Repository validation

```powershell
./build.ps1
./test.ps1
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
./verify-review.ps1 -Mode Smoke -Configuration Release
```
