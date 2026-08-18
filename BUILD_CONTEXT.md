# Build context

This file is intentionally a short handoff for the next development or verification session. It should describe only the current focus, the next concrete step and the small set of documents needed to continue.

Formal work-item lifecycle status and evidence are resolved by `PhotoIdentity.Docs` from the current registry plus archived terminal history.

## Current focus

**WI-0069 — Reduce GitHub Actions feedback time** is the active tooling item.

Slice 1 is implemented on `agent/WI-0069-ci-runtime-optimization`:

- `verify-local.ps1` keeps its full build/test/documentation checkpoint by default but now has explicit `-SkipBuild`, `-SkipTests` and `-SkipDocumentation` switches for callers that already validated the same checkout;
- skipped media verification requires the previously built `PhotoIdentity.Cli` assembly, so CI cannot silently run against missing build output;
- the mixed-media GitHub Actions step reuses the earlier successful restore/build/test/documentation work instead of rerunning the repository checkpoint;
- workflow-level concurrency groups pull-request runs by PR number and cancels superseded PR runs, while `main` push runs use a unique run ID and are not canceled by this policy.

The existing decoder fixture/report assertions, launcher verification and package verification remain unchanged.

## Next concrete step

1. Run the draft pull-request GitHub Actions workflow and confirm build, tests, documentation validation, review smoke, launcher verification and package verification all pass.
2. Inspect the mixed-media log and confirm it reports repository validation as skipped and proceeds directly to the decoder fixtures without a second integration-test run.
3. Compare `build-and-test` wall-clock duration with the pre-change run #1060 baseline and record timing evidence in WI-0069.
4. Mark WI-0069 completed only after the CI behavior and timing evidence are verified.
5. Treat package/publish reuse, SDK/package caching and isolated integration-test sharding as later follow-up decisions rather than silently extending Slice 1.

## Relevant files

- `docs/delivery/status/work-items.yaml`
- `docs/delivery/work-items/WI-0069-ci-runtime-optimization.md`
- `.github/workflows/build.yml`
- `verify-local.ps1`

## Repository validation

```powershell
./build.ps1
./test.ps1
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
./verify-review.ps1 -Mode Smoke -Configuration Release
```
