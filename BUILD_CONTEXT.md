# Build context

This file is intentionally a short handoff for the next development or verification session. It should describe only the current focus, the next concrete step and the small set of documents needed to continue.

Formal work-item lifecycle status and evidence are resolved by `PhotoIdentity.Docs` from the current registry plus archived terminal history.

## Current focus

**WI-0070 — Streamline pull-request validation and stabilize integration tests** is the active M00 implementation item.

Slice 1 merged through PR #176 and validated in workflow #1093. The 294-test integration assembly took about 3m37s wall-clock and recorded 216.9s of test duration. Two classes accounted for about 78.7s of that recorded time, confirming that timing-balanced sharding is the next high-value optimization.

Slice 2 is implemented on `agent/WI-0070-integration-shards`. The first experiment ran three concurrent integration processes on one Windows runner; workflow #1095 showed that runner contention made this slower than the successful sequential reference, so that design was superseded before merge. The current design uses two separate Windows integration jobs, each sequential internally and assigned about half the #1093 measured workload.

The Slice 2 fast-test filter also excludes both integration namespaces after #1093 showed 16 integration tests using `PhotoIdentity.Integration.Tests` were accidentally duplicated in the fast phase.

WI-0066 implementation is complete through merged PR #173 and remains in review pending its focused maintainer browser verification. WI-0065 remains in review pending maintainer verification of unattended GeoNames pickup and restart/resume behavior.

## Next concrete step

1. Validate the revised WI-0070 Slice 2 in GitHub Actions: both isolated shard jobs pass, each executes exactly its assigned tests once, and the full timing-balanced plan accounts for the current discovered suite.
2. Compare overall PR wall-clock and aggregate Windows runner minutes with successful Slice 1 workflow #1093. Keep the smallest isolated shard count that meets the feedback-time goal reliably.
3. If isolated runner sharding is stable, record the evidence and proceed to Slice 3: right-size published review smoke and launcher/package checks while preserving a comprehensive `main` gate.
4. If transient HTTP 500 failures recur, use the shared-host response diagnostics and shard identity to narrow the affected host/test class before considering any quarantine.
5. Refresh `.github/test-timing-baseline.json` only when later measured class timings become materially imbalanced; it is scheduling input, not a duration assertion.

## Relevant files

- `docs/delivery/status/work-items.yaml`
- `docs/delivery/work-items/WI-0070-pr-validation-streamlining.md`
- `docs/operations/testing-and-ci-strategy.md`
- `AGENTS.md`
- `.github/workflows/build.yml`
- `.github/scripts/run-integration-shards.ps1`
- `.github/scripts/summarize-test-timings.ps1`
- `.github/test-timing-baseline.json`
- `tests/PhotoIdentity.Integration.Tests/PhotoIdentityApiTestFactory.cs`
- `tests/PhotoIdentity.Integration.Tests/TestAssembly.cs`

## Repository validation

```powershell
./build.ps1
./test.ps1
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
./verify-review.ps1 -Mode Smoke -Configuration Release
```
