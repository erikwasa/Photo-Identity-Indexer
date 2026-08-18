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

- [ ] CI exposes enough timing data to identify slow test assemblies and the dominant slow integration classes/tests without reconstructing timestamps manually.
- [ ] Generic API integration tests use a shared host setup that disables irrelevant production hosted services by default; worker-specific tests explicitly opt in or exercise worker cycles directly.
- [ ] The remaining transient HTTP 500 failure class has improved diagnostics and a documented root cause or narrowly tracked stabilization follow-up.
- [ ] Integration coverage is partitioned into isolated sequential shards/processes so the required PR critical path no longer waits for the entire host-heavy assembly serially in one process.
- [ ] In-process xUnit parallelism remains disabled for host-heavy integration tests unless later evidence demonstrates a safe replacement architecture.
- [ ] Any temporarily quarantined flaky tests are visible in CI, tracked, non-silently retried, and have a documented condition for returning to the required gate.
- [ ] Published review smoke on PRs is reduced to behavior that adds unique signal beyond integration tests, while comprehensive published-app coverage remains on `main` or another explicit full gate.
- [ ] Launcher/package checks no longer run on unrelated PR changes unless evidence shows keeping them unconditional is cheaper/safer than path-aware gating.
- [ ] A comprehensive `main` gate retains the meaningful integration, published application, launcher and package coverage moved off the fast PR path.
- [ ] At least three representative successful PR runs show the required validation critical path at or below 6 minutes, or the work item records measured evidence for the remaining blocker and a follow-up needed to reach that target.
- [ ] Runner-minute impact is recorded as well as wall-clock impact so speed is not achieved by an unreasonable multiplication of expensive Windows jobs.
- [ ] A durable testing/CI strategy document is added and linked from the repository documentation index where appropriate.
- [ ] `AGENTS.md` contains concise rules for test-layer choice, host-heavy integration tests, flaky-test handling, and PR descriptions/CI-impact reporting.
- [ ] PR guidance makes test-layer additions and material CI-cost changes explicit instead of allowing them to accumulate silently.
- [ ] `PhotoIdentity.Docs validate` and `generate --check` pass after the work-item and documentation changes.

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

### Slice 1 in progress — 2026-08-18

- CI now separates the fast/non-integration pass from the sequential integration assembly. The integration command writes TRX to `.artifacts/test-results` rather than being buried inside one solution-level test command.
- `.github/scripts/summarize-test-timings.ps1` converts the integration TRX into JSON plus a GitHub Step Summary showing the slowest classes and individual tests. The timing artifact is retained even when the test run fails.
- `PhotoIdentityApiTestFactory` is the shared generic API host foundation. It keeps detailed errors enabled and removes `PhotoPlaceEnrichmentHostedService`, `ArchiveAdvancementHostedService` and `IdentityMatchRegenerationHostedService` from generic test hosts by default.
- The two hosted-style tests that failed in PR #173 are the first migrations to the shared factory and are tagged `Category=FlakyDiagnostic` for visibility only. They remain required; there is no retry or quarantine behavior in Slice 1.
- The shared HTTP helper reads a bounded response body before throwing on non-success, so a future 500 can retain server-side diagnostic content instead of only the status exception.
- `docs/operations/testing-and-ci-strategy.md` and concise `AGENTS.md` rules are added early in the work item so future PR/test work follows the intended layer and cost discipline while later slices refine the gate.
