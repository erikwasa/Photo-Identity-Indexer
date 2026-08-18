---
id: WI-0069
title: Reduce GitHub Actions feedback time
milestone: M00
status_source: ../status/work-items.yaml
depends_on: [WI-0004]
affected_modules: [.github/workflows/build.yml, verify-local.ps1, delivery-status]
---

# WI-0069: Reduce GitHub Actions feedback time

## Objective

Reduce pull-request CI wall-clock time and wasted Windows runner work without weakening the repository's existing validation coverage.

## Baseline finding

The normal `build-and-test` job already restores, builds, tests and validates documentation before invoking the mixed-media checkpoint. `verify-local.ps1` currently repeats the solution build, full automated test suite and documentation validation before running the small decoder fixture checks. Recent CI also spends work on superseded pull-request commits when another commit is pushed before the previous run finishes.

## Scope

### Slice 1 — remove redundant work and superseded runs

- Keep the default local `verify-local.ps1` checkpoint behavior unchanged.
- Add explicit skip controls so CI can reuse a build/test/documentation validation that already succeeded earlier in the same job.
- Make the mixed-media CI step use those skip controls while retaining the same valid, warning, corrupt and unsupported fixture assertions.
- Add workflow-level concurrency so a newer run for the same pull request cancels the older in-progress run.
- Do not cancel or coalesce independent `main` push runs as part of this slice.

### Follow-up candidates

After Slice 1 has real run evidence, evaluate package/publish reuse, .NET/NuGet setup caching and isolated integration-test sharding. Those changes are deliberately deferred because they have larger verification or runner-cost tradeoffs.

## Acceptance criteria

- [ ] A normal invocation of `verify-local.ps1` still builds, tests and validates documentation by default.
- [ ] CI can explicitly skip build, tests and documentation validation only after those checks have already succeeded in the same job.
- [ ] The CI mixed-media verification still exercises all four fixture outcomes and validates the generated report.
- [ ] The mixed-media step no longer runs the full integration test suite a second time.
- [ ] A newer pull-request workflow run cancels an older in-progress run for the same pull request.
- [ ] Pushes to `main` are not canceled merely because a newer main push starts.
- [ ] Living/generated documentation validation still passes for the new work-item state.
- [ ] At least one pull-request CI run provides post-change timing evidence for comparison with the pre-change baseline.

## Verification

Use GitHub Actions as the Windows execution gate. Review the first successful run to confirm that the mixed-media step reaches decoder checks immediately after the lightweight script setup rather than rebuilding/retesting the repository, and record the resulting job duration in this work item before completion.
