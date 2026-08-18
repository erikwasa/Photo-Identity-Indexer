---
id: WI-0070
title: Streamline pull-request validation and stabilize integration tests
milestone: M00
status_source: ../status/work-items.yaml
depends_on: [WI-0069]
affected_modules: [.github/workflows/build.yml, tests/PhotoIdentity.Integration.Tests, verify-review.ps1, verify-launcher.ps1, verify-package.ps1, AGENTS.md, docs/operations]
---

# WI-0070: Streamline pull-request validation and stabilize integration tests

## Objective

Reduce required pull-request feedback time and failure noise while preserving meaningful regression coverage. Make the validation architecture explicit enough that future changes do not gradually recreate a slow, host-heavy PR gate.

## Why this is needed

WI-0069 removed the duplicate full build/test/documentation pass from mixed-media verification, but the critical path has moved into the integration suite.

A successful PR #172 workflow run (#1080 / `32172257390`) showed:

- `build-and-test` took about 10m13s end to end;
- the `dotnet test PhotoIdentity.slnx` step ran for about 7m12s, with the 293-test `PhotoIdentity.Integration.Tests` assembly accounting for roughly 7m07s;
- restore and build were each under one minute;
- living/generated documentation checks took only a few seconds;
- review smoke added about 1m06s, dominated by another published Blazor WebAssembly/Emscripten build;
- launcher and package verification completed in parallel in roughly 3m42s and 4m04s respectively, so they consume runner time but do not currently determine PR wall-clock time.

The integration assembly is deliberately sequential after PR #153 because concurrent `WebApplicationFactory` / `TestServer` hosts caused cross-test HTTP 500 failures. Recent runs have nevertheless produced different transient HTTP 500 failures even with assembly-level parallelism disabled. The current state therefore combines long sequential execution with unreliable signal.

PR #173 provided another concrete example on 2026-08-18: build, launcher and package verification passed, while two unrelated hosted-client/style tests (`DetectorComparisonWorkspaceApplicationTests` and `HostedStylesApplicationTests`) returned HTTP 500 after several minutes of otherwise sequential integration execution. The failing calls used `GetStringAsync`, so the retained test diagnostic contained only the status exception and not the response body.

## Guiding principles

- Preserve valuable behavior coverage; do not remove tests solely to make CI green or fast.
- Put a rule at the lowest practical test layer. Use full HTTP-host integration tests primarily for cross-layer wiring and contract behavior that cannot be established more cheaply.
- Keep isolated integration shards sequential internally rather than re-enabling unsafe in-process host parallelism.
- Treat retries as diagnostics, not as a permanent way to mask nondeterminism.
- A quarantined flaky test must remain visible, have a tracked stabilization path, and have an explicit condition for returning to the required gate.
- Expensive published-runtime/package checks should run where they add signal: on relevant changes, on `main`, or in a slower comprehensive gate rather than indiscriminately on every PR.
- Measure before/after timing so pipeline changes are evidence-driven.

## Scope

### Slice 1 — timing, taxonomy and host isolation

- Emit machine-readable and human-readable test timing evidence from CI so slow assemblies/classes/tests can be identified without manually reading raw logs.
- Introduce a small test taxonomy/trait convention for at least fast, integration, published-runtime and temporarily flaky/diagnostic coverage where useful.
- Replace ad-hoc per-class application factories with a shared integration-test host foundation where practical.
- Disable production hosted/background workers by default in generic API integration hosts unless the test explicitly verifies that worker.
- Preserve dedicated worker tests that exercise worker cycles directly or opt in to the relevant hosted service.
- Investigate the remaining transient HTTP 500 class with better host/server exception diagnostics rather than hiding it with unconditional retries.

### Slice 2 — shorten the required PR critical path

- Split host-heavy integration coverage into multiple isolated process/job shards with each shard still sequential internally.
- Balance shards using measured timings, not test count alone.
- Keep deterministic regression coverage required on PRs unless there is a documented reason to move a class to a slower gate.
- Separate currently known flaky tests into an explicit diagnostic/quarantine lane only while their root cause is being fixed.
- Preserve a comprehensive `main` validation path so moving checks off the required PR critical path does not silently remove coverage.

### Slice 3 — right-size published application and package checks

- Reduce PR review smoke to the minimum published-app behavior that is uniquely valuable after integration tests, such as application startup, hosted client/static assets and representative API behavior.
- Keep the broader end-to-end review smoke on `main` or another comprehensive gate unless measurements show it is cheap enough to remain required.
- Make launcher/package verification path-aware or move it to the comprehensive gate when a change cannot affect those surfaces.
- Reuse publish/package outputs where that avoids repeated native WASM/Emscripten compilation without weakening verification.
- Add dependency/package caching where measurement shows worthwhile savings.

### Slice 4 — durable agent and contributor guidance

Create durable documentation for how tests and PR validation are expected to evolve, then make the concise rules discoverable by future agents.

At minimum:

- add a repository testing/CI strategy document (for example under `docs/operations/`) describing the test layers, PR gate, comprehensive `main` gate, flaky-test policy, timing expectations and when published/package verification belongs;
- update `AGENTS.md` so agents are instructed to choose the lowest practical test layer, justify new host-heavy integration coverage, reuse the shared test host, and avoid adding expensive required PR checks without measured value;
- instruct agents creating PRs to state what test layer was added/changed, whether the PR changes the required CI gate, and any material timing/coverage tradeoff;
- document that retries must not be used to normalize flaky behavior and that temporary quarantine requires a tracked work item or explicit follow-up with an exit condition;
- consider a PR template/checklist if it is the clearest way to make these expectations consistent for both agents and maintainers.

The detailed strategy document should carry rationale and examples. `AGENTS.md` should remain concise and action-oriented rather than duplicating the whole strategy.

## Acceptance criteria

- [x] CI exposes enough timing data to identify slow test assemblies and the dominant slow integration classes/tests without reconstructing timestamps manually.
- [ ] Generic API integration tests use a shared host setup that disables irrelevant production hosted services by default; worker-specific tests explicitly opt in or exercise worker cycles directly.
- [x] The remaining transient HTTP 500 failure class has improved diagnostics and a documented root cause or narrowly tracked stabilization follow-up. WI-0071 owns the remaining ad-hoc API-host cases and their quarantine exit evidence.
- [x] Integration coverage is partitioned into isolated sequential shards/processes so the required PR critical path no longer waits for the entire host-heavy assembly serially in one process.
- [x] In-process xUnit parallelism remains disabled for host-heavy integration tests unless later evidence demonstrates a safe replacement architecture.
- [x] Any temporarily quarantined flaky tests are visible in CI, tracked, non-silently retried, and have a documented condition for returning to the required gate. `.github/flaky-integration-tests.txt` is the canonical temporary list; WI-0071 requires a stabilization change plus three consecutive clean diagnostic runs before restoration.
- [ ] Published review smoke on PRs is reduced to behavior that adds unique signal beyond integration tests, while comprehensive published-app coverage remains on `main` or another explicit full gate.
- [ ] Launcher/package checks no longer run on unrelated PR changes unless evidence shows keeping them unconditional is cheaper/safer than path-aware gating.
- [ ] A comprehensive `main` gate retains the meaningful integration, published application, launcher and package coverage moved off the fast PR path.
- [ ] At least three representative successful PR runs show the required validation critical path at or below 6 minutes, or the work item records measured evidence for the remaining blocker and a follow-up needed to reach that target.
- [x] Runner-minute impact is recorded as well as wall-clock impact so speed is not achieved by an unreasonable multiplication of expensive Windows jobs.
- [x] A durable testing/CI strategy document is added and linked from the repository documentation index where appropriate.
- [x] `AGENTS.md` contains concise rules for test-layer choice, host-heavy integration tests, flaky-test handling, and PR descriptions/CI-impact reporting.
- [x] PR guidance makes test-layer additions and material CI-cost changes explicit instead of allowing them to accumulate silently.
- [x] `PhotoIdentity.Docs validate` and `generate --check` pass after the Slice 1 work-item and documentation changes.

## Non-goals

- Do not delete regression coverage merely because it is slow.
- Do not re-enable broad in-process integration-test parallelism as the first optimization.
- Do not make flaky tests appear stable through unconditional automatic retries.
- Do not weaken package/release verification without retaining an explicit comprehensive gate.
- Do not turn `AGENTS.md` into a long CI design document; detailed rationale belongs in dedicated documentation.

## Initial implementation order

1. Add timing output and shared test-host isolation, then measure the suite again.
2. Classify and diagnose the known flaky HTTP 500 tests.
3. Split deterministic integration coverage into timing-balanced isolated shards.
4. Move/reduce expensive published/package checks using measured path/gate value.
5. Encode the resulting rules in durable CI/testing documentation and `AGENTS.md` before completing the item.

## Implementation notes

### Slice 1 validated — 2026-08-18

- PR #176 merged as `ca17c5fb01981480d9c7d79b53ef75383415fe04` after successful workflow #1093 (`32178207171`).
- CI separates the fast/non-integration pass from the sequential integration assembly and publishes TRX plus JSON/Markdown timing evidence.
- Workflow #1093 completed `build-and-test` in about 6m58s. The full 294-test integration command took about 3m37s wall-clock and recorded 216.9s of aggregate test duration.
- Two classes dominate the measured integration cost: `ResumableBatchProcessorTests` recorded about 40.2s and `DetectorEvaluationComparisonApplicationTests` about 38.5s. The next class was about 6.3s, confirming that test-count-only partitioning would be badly imbalanced.
- The first shared-host migrations passed in #1093 without reproducing their prior HTTP 500 failures. The bounded non-success response diagnostic remains in place if the failure returns.
- The #1093 fast-filter command exposed a second namespace spelling used by 16 integration tests: `PhotoIdentity.Integration.Tests`. Those tests ran for about eight seconds of recorded test time during the fast phase and were then run again in the full integration phase. Slice 2 excludes both integration namespaces from the fast pass.
- `docs/operations/testing-and-ci-strategy.md` and concise `AGENTS.md` rules are merged, so future test/PR work now carries the intended layer and cost discipline.

### Slice 2 validated — 2026-08-18

- Runtime discovery groups every current integration test by class, and the scheduler greedily balances by measured class duration. New classes are automatically assigned with a conservative default weight until the baseline is refreshed.
- The first Slice 2 attempt in workflow #1095 ran three concurrent `dotnet test` child processes on the same Windows runner. It did not fail immediately, but after the overall run exceeded eight minutes it was still inside the shard step; successful #1093 had already completed `build-and-test` in about 6m58s. Same-runner three-process concurrency was therefore rejected as a performance regression rather than being allowed to become the final design.
- The revised Slice 2 design uses two separate Windows integration jobs, each internally sequential and independently restored/built. This intentionally spends additional runner setup/build minutes in exchange for real CPU/testhost isolation and a lower wall-clock critical path.
- Workflow #1104 proved the revised scheduler and coverage accounting: shard 2 passed all 159 assigned tests and shard 1 executed all 135 assigned tests exactly once. Shard 1 was blocked only by `ReviewSuggestionGalleryApplicationTests.Gallery_requires_exact_model_revision_and_rejects_unknown_sort_or_confidence_group`, which expected HTTP 400 but intermittently received HTTP 500 from another ad-hoc API factory. This matches the existing cross-class host-flake pattern rather than a sharding coverage defect.
- Four independently observed transient-500 tests are recorded in `.github/flaky-integration-tests.txt`. Required shards exclude those exact tests while retaining all other coverage. The same workflow executes the four tests exactly once in a visible `continue-on-error` diagnostic step, records TRX/JSON evidence, and never retries them.
- WI-0071 is the stabilization follow-up. A quarantine entry returns to required blocking coverage only after a root-cause/shared-host stabilization change and three consecutive representative diagnostic CI passes without a transient 500.
- Workflow #1112 (`32183423755`) was fully green on current `main`: both required shards passed exact coverage, all four quarantined diagnostics passed once, and build/docs/review/mixed-media/launcher/package checks passed. It also showed that the #1093 timing weights were no longer representative on the isolated runners: shard 1 recorded 486.8s of test duration and took 8m06 to execute, while shard 2 recorded 151.2s and took 2m31. Overall workflow wall-clock was therefore about 10m37, which is evidence against accepting that balance as the final optimization.
- The largest outlier in #1112 was `ResumableBatchProcessorTests.Five_hundred_job_sample_produces_complete_status_summary` at 157.8s. The test asserted durable completion/accounting/idempotency semantics rather than a 500-item performance threshold, and no repository contract referenced the number 500. The required PR fixture is now 50 jobs with the same assertions and a sample-sized attempt bound; this retains non-trivial batch coverage without using the PR gate as a scale test.
- `.github/test-timing-baseline.json` was rebalanced from #1112's actual required-shard TRX data. After the 50-job change, `ResumableBatchProcessorTests` carries a conservative 20s scheduling estimate until the next measured run.
- Workflow #1116 (`32184890234`) demonstrated that the fixture reduction plus new balance materially shortened shard 1: all 152 assigned required tests executed in 1m54s. That run was red only because `CollectionViewerPreviewApplicationTests.Local_verified_original_without_proxy_is_served_directly_without_hydration` returned another transient HTTP 500 from an ad-hoc API host. The class was migrated to `PhotoIdentityApiTestFactory` instead of widening quarantine, preserving its custom Files-on-Demand/storage test doubles while disabling unrelated production background workers. A bounded-response-body success helper was added for non-string endpoints.
- Workflow #1118 (`32185693518`) validated the final Slice 2 shape end to end. Both required shards passed exact coverage; shard 1 ran 139 required tests in 1m36s and shard 2 ran 153 required tests in 1m15s. The migrated viewer-preview tests passed without quarantine. All four tracked flaky diagnostics ran once and passed with no retries. Fast tests, living/generated documentation, review smoke, mixed-media verification, launcher verification and package verification all passed.
- #1118 overall workflow wall-clock was about 6m17s (21:03:30–21:09:47 UTC). Integration was no longer the bottleneck: `build-and-test` completed in roughly 4m04s and package verification was the last job to finish. The remaining ~17 seconds above the <=6m target, plus the duplicated integration-runner setup cost, therefore belong to Slice 3 rather than further integration sharding.
- Slice 2 uses five Windows jobs on full PR validation instead of Slice 1's three. Based on observed job spans, #1118 consumed roughly 19–20 Windows runner-minutes versus roughly 14–15 in the three-job Slice 1 shape. The wall-clock improvement is useful but the runner-cost increase is material; Slice 3 should reclaim some of that by making launcher/package/published-runtime work conditional or reusable rather than adding more integration runners.
- The main `build-and-test` job no longer runs deterministic integration coverage serially. It retains true fast tests, the small flaky diagnostic lane, documentation validation, published review smoke and mixed-media verification.
- The timing summarizer reads TRX `TestMethod.className` definitions instead of inferring class names from theory display text, avoiding incorrect grouping for parameterized cases.
