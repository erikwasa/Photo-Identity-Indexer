# Build context

This file is intentionally a short handoff for the next development or verification session. It should describe only the current focus, the next concrete step and the small set of documents needed to continue.

Formal work-item lifecycle status and evidence are resolved by `PhotoIdentity.Docs` from the current registry plus archived terminal history.

## Current focus

**WI-0070 — Streamline pull-request validation and stabilize integration tests** has completed its gate-right-sizing implementation slices. PR #180 supplied a 3m37s representative fast-path run and PR #182 supplied a 4m15s run, both below the six-minute target. PR #184 workflow #1143 was functionally green but took about 10m23s because integration shard 2 had an extreme test-duration outlier. Do not rebalance from that single run; continue collecting normal PR samples and only change shard weights if the variance recurs across representative runs.

**WI-0071 — Stabilize quarantined API integration tests** is the active implementation focus. Four transient-500 endpoint tests remain in `.github/flaky-integration-tests.txt`; they execute once per workflow in the visible non-blocking diagnostic lane with no automatic retries.

PR #182 merged the shared-host migration for `ReviewProgressFilterApplicationTests`; its clean post-change count is **2/3** after PR #184 workflow #1143. PR #184 merged the shared-host migration for `PersonSmartCollectionVisibilityApplicationTests` as `2bc2c926bb73df896f595da3b42940b1b8223205`; its clean post-change count is **1/3**.

PR #184 final-head workflow #1145 initially produced three additional transient HTTP 500s in required-shard classes that still use ad-hoc API factories. A single manual failed-job diagnostic rerun passed both shards with no code or quarantine change. Treat those classes as additional migration evidence, not as justification to widen quarantine.

The current slice migrates `CollectionQueryApplicationTests` onto `PhotoIdentityApiTestFactory` through a thin local wrapper, preserving all existing collection behavior assertions and leaving its quarantined case in place while its three-run evidence window starts.

M19 feature work remains separately in review/in progress and must be preserved when branches are synchronized with `main`.

## Next concrete step

1. Validate the collection-query shared-host migration in GitHub Actions without adding retries or changing quarantine membership.
2. A successful first diagnostic lane should advance review-progress to **3/3**, person-visibility to **2/3**, and collection-query to **1/3**.
3. If review-progress reaches 3/3, restore only that case to the required shard in a subsequent change and prove exact once-only required coverage; do not combine restoration with this host migration.
4. Migrate `ReviewSuggestionGalleryApplicationTests` next so all four original quarantine classes have a shared-host stabilization change.
5. Then prioritize the additional ad-hoc factories observed failing in #1145 (`DetectorEvaluationComparisonApplicationTests`, `ReviewSuggestionApplicationTests`, and `ReviewQueueNavigationApplicationTests`) without automatically quarantining them.
6. Continue collecting natural WI-0070 timing samples; manual reruns are diagnostic only and do not count as independent timing or quarantine evidence.

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
- `tests/PhotoIdentity.Integration.Tests/ReviewProgressFilterApplicationTests.cs`
- `tests/PhotoIdentity.Integration.Tests/PersonSmartCollectionVisibilityApplicationTests.cs`
- `tests/PhotoIdentity.Integration.Tests/CollectionQueryApplicationTests.cs`
- `tests/PhotoIdentity.Integration.Tests/ReviewSuggestionGalleryApplicationTests.cs`
- `tests/PhotoIdentity.Integration.Tests/DetectorEvaluationComparisonApplicationTests.Helpers.cs`
- `tests/PhotoIdentity.Integration.Tests/ReviewSuggestionApplicationTests.cs`
- `tests/PhotoIdentity.Integration.Tests/ReviewQueueNavigationApplicationTests.cs`

## Repository validation

```powershell
./build.ps1
./test.ps1
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
./verify-review.ps1 -Mode Smoke -Configuration Release
```
