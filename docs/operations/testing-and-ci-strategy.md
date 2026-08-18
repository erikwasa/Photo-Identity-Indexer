# Testing and CI strategy

This document defines how Photo Identity should balance regression confidence, pull-request feedback time and test reliability. It is the detailed companion to the concise rules in `AGENTS.md`.

## Goals

- Keep the required pull-request gate fast enough to support short development iterations.
- Preserve meaningful regression coverage rather than deleting tests to improve timing.
- Keep failures attributable: a red required check should usually indicate a product or contract regression, not an unrelated host-lifetime race.
- Keep a comprehensive validation path on `main` for expensive published-runtime, launcher and package behavior that does not need to block every unrelated pull request.
- Measure wall-clock time and runner-minute cost before and after material pipeline changes.

WI-0070 starts from a measured baseline where `build-and-test` exceeded ten minutes and the sequential integration assembly dominated the critical path.

## Test layers

### Fast tests

Use unit, persistence, source, recognition, documentation and other non-host-heavy tests for rules that can be proven without starting the complete ASP.NET application. Prefer this layer for parsing, validation, repository semantics, pure transformations and combinatorial business rules.

A rule should not become a full-host integration test merely because an HTTP endpoint eventually calls it. Keep one representative contract/wiring test where useful and test the behavior matrix below the host boundary.

The solution-level fast pass must exclude both integration namespaces currently present in the repository: `PhotoIdentity_Integration_Tests` and `PhotoIdentity.Integration.Tests`. A namespace filter that excludes only one spelling can silently duplicate integration coverage in the fast phase.

### API integration tests

Use `PhotoIdentity.Integration.Tests` for cross-layer application contracts that genuinely require the ASP.NET host, SQLite wiring, HTTP serialization, static-web-host wiring or interactions among multiple application modules.

Generic API tests should use `PhotoIdentityApiTestFactory`. The shared factory disables unrelated production background workers by default so a request test is not competing with archive advancement, identity-regeneration or place-enrichment loops. Tests that specifically verify a hosted worker should exercise its cycle directly where possible or explicitly opt into the production worker.

The integration assembly has xUnit in-process parallelization disabled because concurrent `WebApplicationFactory` / `TestServer` lifetimes produced unrelated HTTP 500 failures on Windows. Do not simply turn broad in-process parallelism back on.

WI-0070 instead shards by **separate `dotnet test` processes**. Each process still runs its assigned xUnit tests sequentially, which preserves isolation of application/testhost static state. The current implementation runs multiple shard processes concurrently inside the same already-built Windows job rather than creating multiple runner jobs; this avoids multiplying SDK setup, restore/build and Windows runner minutes.

Shard assignment is timing-based, not count-based. `.github/test-timing-baseline.json` contains measured class weights from a known successful workflow. `.github/scripts/run-integration-shards.ps1` discovers the current tests at runtime, assigns each class to exactly one shard, and gives new classes a default weight until the baseline is refreshed. The runner fails if the number of TRX results or unique test IDs differs from the discovery count, preventing an optimization from silently dropping or duplicating tests.

The timing baseline is scheduling input, not a performance assertion. Refresh it after material suite changes when the measured shard distribution becomes meaningfully imbalanced.

### Published-runtime smoke

Published-runtime smoke proves behavior that a TestServer integration test cannot: the published application starts, hosted client/static assets are present, and representative production packaging/hosting paths work.

Do not duplicate the whole API integration suite in published smoke. Keep the PR smoke to uniquely valuable published-app checks; retain the broader smoke in the comprehensive `main` gate when it is useful for regression confidence.

### Launcher and package verification

Launcher/package verification protects Windows deployment behavior, durable-data upgrade behavior, self-contained publish output and the actual operator package. These checks are valuable but expensive. They should run for changes that can affect those surfaces and in the comprehensive `main` gate; unrelated documentation or narrow application-rule changes should not pay the full package cost once path-aware gating is implemented.

## Flaky-test policy

A flaky failure is a defect in the validation system until proven otherwise. Do not make an unreliable test look stable through unconditional automatic retries.

When a test is suspected flaky:

1. preserve the failure evidence and identify the exact test/run;
2. improve diagnostics before suppressing the signal where possible;
3. track the stabilization work explicitly;
4. if temporary quarantine is needed, keep the test running in a visible diagnostic lane rather than deleting or silently skipping it;
5. document the condition for restoring it to the required gate.

`Category=FlakyDiagnostic` is the initial trait used to identify tests with observed intermittent infrastructure/host failures. The trait alone does not make a test non-blocking; gate behavior must be explicit in the workflow.

## Timing evidence

The PR workflow records integration results as TRX and publishes both JSON and Markdown timing summaries. The summary should make shard duration, the slowest classes and individual tests visible without reconstructing timestamps from raw logs.

Use measured durations to balance integration shards. Do not balance shards only by test count: workflow #1093 showed two classes at roughly 40 seconds each while most classes were only a few seconds or less.

For material CI changes, record both:

- required PR wall-clock critical path; and
- approximate Windows runner minutes consumed across parallel jobs.

Prefer concurrency inside already-required jobs when it gives safe isolation and materially reduces wall-clock time without multiplying expensive runner setup. Add separate jobs only when process/resource isolation or a different gate dependency actually requires them.

The WI-0070 target is a required PR critical path at or below six minutes across representative successful runs without unreasonable runner multiplication.

## Pull-request expectations

When a change adds or materially changes tests, the PR description should identify the layer: fast, API integration, published-runtime, launcher/package or diagnostic/flaky.

When a change modifies the required workflow, the PR description should state:

- what moved into or out of the required PR gate;
- why the signal justifies the cost;
- measured timing when available;
- what comprehensive coverage remains on `main` if a check no longer blocks every PR.

Agents should not add a new host-heavy integration test when the same behavior can be proved at a faster layer without losing the contract that matters.

## Transition under WI-0070

WI-0070 changes the pipeline in measured slices rather than all at once:

1. separate fast and integration test commands, add timing evidence and establish shared test-host isolation;
2. diagnose known transient HTTP 500 tests and classify any temporary diagnostic lane explicitly;
3. partition deterministic integration tests into isolated sequential processes using measured timing while keeping exact discovery/result coverage checks;
4. reduce duplicate published-runtime coverage and make launcher/package checks appropriately path-aware while retaining comprehensive `main` validation;
5. keep this document and `AGENTS.md` aligned with the resulting steady-state gate.

Until the later gate-right-sizing slice lands, launcher and package verification remain unconditional and the broad published review smoke remains part of the current workflow.
