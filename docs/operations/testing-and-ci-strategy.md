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

WI-0070 instead shards by **separate `dotnet test` processes on isolated Windows runners**. Each shard remains sequential internally, preserving xUnit/TestServer host-lifetime isolation. A three-process experiment inside one runner was rejected after its first PR run remained in the integration step after the whole successful sequential reference job had already completed; sharing one runner did not provide enough CPU/I/O isolation.

The current design therefore uses two isolated integration jobs. This duplicates .NET setup, restore and integration-project build, so it is not free in runner minutes. That cost is intentional and must be measured against the wall-clock reduction. Later pipeline work should prefer artifact/build reuse if it reduces duplicated setup without coupling the testhosts back onto one constrained runner.

Shard assignment is timing-based, not count-based. `.github/test-timing-baseline.json` contains measured class weights from a known successful workflow. Each integration job discovers the entire current suite, computes the same deterministic class plan, and runs one shard. New classes receive a conservative default weight until the baseline is refreshed.

Each selected shard must produce exactly the number of unique TRX test IDs assigned by the plan. The full plan must also account for every discovered required class and test before execution. Tests named in `.github/flaky-integration-tests.txt` are deliberately excluded from blocking shards only while their stabilization work is open; they remain part of runtime discovery and are executed separately as diagnostics.

The timing baseline is scheduling input, not a performance assertion. Refresh it after material suite changes when the measured shard distribution becomes meaningfully imbalanced.

### Published-runtime smoke

Published-runtime smoke proves behavior that a TestServer integration test cannot: the published application starts, hosted client/static assets are present, and representative production packaging/hosting paths work.

Do not duplicate the whole API integration suite in published smoke. Pull requests use `verify-review.ps1 -SmokeProfile PublishedMinimum`: the published host must become healthy, the hosted Blazor client must be served, and a representative review-gallery request must return the synthetic fixture without leaking private paths and with the expected no-store cache policy. The broader mutation-heavy published smoke remains `Comprehensive` and runs on `main`.

### Launcher and package verification

Launcher/package verification protects Windows deployment behavior, durable-data upgrade behavior, self-contained publish output and the actual operator package. These checks are valuable but expensive.

The pull-request workflow classifies changed paths before allocating those Windows jobs. Changes under `src/` and shared build metadata are conservatively treated as publish-surface changes. Launcher-specific scripts/configuration trigger launcher verification; packaging/model inputs trigger package verification. Documentation-only, test-only and other unrelated pull requests can therefore skip those expensive deployment jobs. A push to `main` always enables both jobs, preserving the comprehensive deployment gate even when the merged PR did not need them on its fast path.

When adding a new deployment input, update the classifier in `.github/workflows/build.yml` in the same change so path-aware gating does not silently miss it.

## Flaky-test policy

A flaky failure is a defect in the validation system until proven otherwise. Do not make an unreliable test look stable through unconditional automatic retries.

When a test is suspected flaky:

1. preserve the failure evidence and identify the exact test/run;
2. improve diagnostics before suppressing the signal where possible;
3. track the stabilization work explicitly;
4. if temporary quarantine is needed, keep the test running in a visible diagnostic lane rather than deleting or silently skipping it;
5. document the condition for restoring it to the required gate.

`Category=FlakyDiagnostic` may annotate tests with observed intermittent infrastructure/host failures, but the canonical temporary quarantine is `.github/flaky-integration-tests.txt`. Quarantine membership must be exact and reviewable; stale or duplicate entries are treated as configuration errors rather than silently ignored.

The workflow runs every quarantined test exactly once in `Run quarantined integration diagnostics` after the solution is already built. That step uses `continue-on-error` so a known intermittent host failure does not block unrelated PRs, but it still emits a failed step, TRX, a JSON summary and an uploaded artifact. **No retry is performed.**

WI-0071 owns stabilization of the current quarantine. A test returns to required shard coverage only when its root-cause/shared-host stabilization change is in place and it has passed **three consecutive representative diagnostic CI runs** without the transient HTTP 500. Removing the quarantine entry must then cause the required shard coverage check to execute it exactly once. Quarantine is temporary architecture, not a permanent low-confidence test tier.

## Timing evidence

The PR workflow records integration results as TRX and publishes JSON and Markdown timing summaries for each shard. The summary should make shard duration, the slowest classes and individual tests visible without reconstructing timestamps from raw logs.

Use measured durations to balance integration shards. Do not balance shards only by test count: workflow #1093 showed two classes at roughly 40 seconds each while most classes were only a few seconds or less.

For material CI changes, record both:

- required PR wall-clock critical path; and
- approximate Windows runner minutes consumed across parallel jobs.

Do not assume more concurrency is faster. Measure contention on the actual hosted runner. If concurrency requires separate runners, record the duplicate setup/build cost and keep the smallest shard count that reaches the feedback-time goal reliably.

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
2. diagnose known transient HTTP 500 tests and isolate the explicitly tracked cases in a visible, non-retried diagnostic lane while WI-0071 stabilizes them;
3. partition deterministic integration tests into timing-balanced isolated runner jobs while keeping exact per-shard coverage checks;
4. reduce duplicate published-runtime coverage and make launcher/package checks appropriately path-aware while retaining comprehensive `main` validation;
5. keep this document and `AGENTS.md` aligned with the resulting steady-state gate.

Slice 3 implements the gate split described above: pull requests retain the minimum uniquely valuable published-app smoke, launcher/package jobs are path-aware, and `main` remains comprehensive.

### Slice 3 validation status — 2026-08-18

PR #179 merged Slice 3 as `e50e26c0cfeca4a1cc1a2aea53ef7d41c2a57bdc`. Its workflow #1130 (`32189443581`) proved the affected-path branch of the classifier: changing the CI workflow enabled both launcher and package verification, the new `PublishedMinimum` review smoke passed, launcher/package verification passed, and the only initial red signal was an unrelated transient API-host HTTP 500 that passed a single manual diagnostic rerun with no code or quarantine change.

PR #180 workflow #1134 (`32190714702`) provides the complementary unrelated-path evidence. With only this documentation file changed, the classifier reported `launcher=false; package=false`; both deployment jobs were skipped while `build-and-test`, both exact-coverage integration shards, published minimum smoke, documentation checks, mixed-media verification and all four once-only quarantined diagnostics passed. The required Windows critical path was about **3m37s**, with about **9.9 Windows runner-minutes** across the three required Windows jobs. Compared with Slice 2 workflow #1118 at about 6m17s and roughly 19–20 Windows runner-minutes, this representative fast-path run reduced wall-clock by about 43% and Windows runner consumption by roughly half.

This is the first clean measured unrelated-path run after Slice 3. WI-0070's three-representative-run acceptance sample should accumulate from normal subsequent PRs rather than artificial reruns. The transient host failures remain owned by WI-0071 and are not normalized with retries.
