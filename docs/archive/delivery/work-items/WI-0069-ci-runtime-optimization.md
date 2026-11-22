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

The normal `build-and-test` job already restores, builds, tests and validates documentation before invoking the mixed-media checkpoint. `verify-local.ps1` previously repeated the solution build, full automated test suite and documentation validation before running the small decoder fixture checks. Recent CI also spent work on superseded pull-request commits when another commit was pushed before the previous run finished.

## Scope

### Slice 1 — remove redundant work and superseded runs

- Keep the default local `verify-local.ps1` checkpoint behavior unchanged.
- Add explicit skip controls so CI can reuse a build/test/documentation validation that already succeeded earlier in the same job.
- Make the mixed-media CI step use those skip controls while retaining the same valid, warning, corrupt and unsupported fixture assertions.
- Add workflow-level concurrency so a newer run for the same pull request cancels the older in-progress run.
- Do not cancel or coalesce independent `main` push runs as part of this slice.

### Follow-up candidates

Package/publish reuse, .NET/NuGet setup caching and isolated integration-test sharding remain separate optimization candidates because they have larger verification or runner-cost tradeoffs.

## Acceptance criteria

- [x] A normal invocation of `verify-local.ps1` still builds, tests and validates documentation by default.
- [x] CI can explicitly skip build, tests and documentation validation only after those checks have already succeeded in the same job.
- [x] The CI mixed-media verification still exercises all four fixture outcomes and validates the generated report.
- [x] The mixed-media step no longer runs the full integration test suite a second time.
- [x] A newer pull-request workflow run cancels an older in-progress run for the same pull request.
- [x] Pushes to `main` are not canceled merely because a newer main push starts.
- [x] Living/generated documentation validation still passes for the new work-item state.
- [x] At least one pull-request CI run provides post-change timing evidence for comparison with the pre-change baseline.

## Implementation

Slice 1 merged through PR #169:

- `verify-local.ps1` exposes opt-in `-SkipBuild`, `-SkipTests` and `-SkipDocumentation` switches while preserving the existing full checkpoint as the default path.
- Skipped phases are recorded as `skipped` in the verification report, and manual media checks require the existing built CLI assembly before execution.
- `.github/workflows/build.yml` uses those skip switches only after the normal restore/build/test/living-doc/generated-doc steps have already succeeded.
- Pull-request workflow concurrency is keyed by PR number and cancels superseded in-progress runs; non-PR runs fall back to unique run IDs.
- The mixed-media fixture inputs and report assertions are unchanged.

## Verification evidence

PR #169 merged as commit `b0c2f05cc4e889787d8d370cb06a6a67e2b6725c`. Its final PR run encountered one intermittent integration-test HTTP 500 before reaching the optimized media step; launcher and package verification passed.

The next pull request, PR #170, preserved the merged WI-0069 workflow changes and provided a clean end-to-end Windows run in workflow #1075 (`32163493523`):

- all three jobs passed;
- `build-and-test` ran from approximately 17:24:19 to 17:32:46 UTC, about 8m26s;
- the integration suite itself had grown to 293 tests and took 5m14s in that run;
- after build/test/docs and review smoke, mixed-media verification invoked `verify-local.ps1` with all three reuse switches;
- build, automated tests and documentation validation were explicitly reported as skipped inside that invocation;
- the four decoder/unsupported checks completed in about 1.4 seconds before the existing expected-failure assertion passed;
- no second integration-test pass occurred.

For comparison, the pre-change reference run #1060 had `build-and-test` at about 10m56s, with the old mixed-media invocation consuming roughly 4m26s largely because it rebuilt and reran the full test suite. The post-change successful job therefore finished about 2m30s faster overall despite a materially slower/larger integration-test pass, while the redundant mixed-media phase itself fell from minutes to seconds.

Concurrency behavior was also observed directly during PR #169: run #1067 was canceled after the branch advanced, leaving the newer PR attempt to proceed. Non-PR runs use `${{ github.run_id }}` in the concurrency group, so separate `main` pushes do not share a cancellation group.
