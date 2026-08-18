# Build context

This file is intentionally a short handoff for the next development or verification session. It should describe only the current focus, the next concrete step and the small set of documents needed to continue.

Formal work-item lifecycle status and evidence are resolved by `PhotoIdentity.Docs` from the current registry plus archived terminal history.

## Current focus

**WI-0070 — Streamline pull-request validation and stabilize integration tests** is the active M00 implementation item.

WI-0066 implementation is complete through merged PR #173. Its focused maintainer browser verification remains pending before completion. The final PR #173 run built successfully and passed launcher/package verification but two unrelated hosted-client/style integration tests returned transient HTTP 500 responses; that validation-system instability is now part of WI-0070 rather than further feature work in WI-0066.

WI-0070 Slice 1 is establishing timing evidence and a shared API integration-test host that disables unrelated production background workers by default. The initial migration targets the hosted-style tests that failed in PR #173 and improves 500 diagnostics by preserving a bounded response body.

WI-0065 remains in review pending maintainer verification of unattended GeoNames pickup and restart/resume behavior.

## Next concrete step

1. Validate WI-0070 Slice 1 in GitHub Actions: build, fast-test split, sequential integration run, TRX timing summary and shared-host migrations.
2. Use the timing artifact to identify the dominant integration classes and balance the first isolated process/job shards.
3. If transient HTTP 500 failures recur, use the new response diagnostics to narrow the host-lifetime/root-cause follow-up before quarantining anything.
4. Continue migrating generic API tests to `PhotoIdentityApiTestFactory`; keep worker-specific tests direct or explicitly opted in.
5. After measured host/timing evidence, implement the isolated integration shards before changing published/package gate coverage.

## Relevant files

- `docs/delivery/status/work-items.yaml`
- `docs/delivery/work-items/WI-0070-pr-validation-streamlining.md`
- `docs/operations/testing-and-ci-strategy.md`
- `AGENTS.md`
- `.github/workflows/build.yml`
- `.github/scripts/summarize-test-timings.ps1`
- `tests/PhotoIdentity.Integration.Tests/PhotoIdentityApiTestFactory.cs`
- `tests/PhotoIdentity.Integration.Tests/TestAssembly.cs`
- `tests/PhotoIdentity.Integration.Tests/HostedStylesApplicationTests.cs`
- `tests/PhotoIdentity.Integration.Tests/DetectorComparisonWorkspaceApplicationTests.cs`

## Repository validation

```powershell
./build.ps1
./test.ps1
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
./verify-review.ps1 -Mode Smoke -Configuration Release
```
