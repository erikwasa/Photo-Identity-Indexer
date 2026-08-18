# Build context

This file is intentionally a short handoff for the next development or verification session. It should describe only the current focus, the next concrete step and the small set of documents needed to continue.

Formal work-item lifecycle status and evidence are resolved by `PhotoIdentity.Docs` from the current registry plus archived terminal history.

## Current focus

**WI-0070 — Streamline pull-request validation and stabilize integration tests** has completed its gate-right-sizing implementation slices. PR #180 supplied a 3m37s representative fast-path run and PR #182 supplied a 4m15s run, both below the six-minute target. PR #184 workflow #1143 was functionally green but took about 10m23s because integration shard 2 had an extreme test-duration outlier: the same 143-test shard recorded 439.4s aggregate test duration versus 64.5s in #1139, and an unchanged auto-assignment test moved from 0.75s to 124.39s. Do not rebalance from this single outlier; WI-0070 retains measured follow-up for runner/test-duration variance and also remains open while WI-0071 completes the remaining shared generic API-host migrations.

**WI-0071 — Stabilize quarantined API integration tests** is the active implementation focus. Four transient-500 endpoint tests remain in `.github/flaky-integration-tests.txt`; they execute once per workflow in the visible non-blocking diagnostic lane with no retries.

PR #182 merged the first host migration for `ReviewProgressFilterApplicationTests`. Workflow #1139 provided clean post-change diagnostic sample **1/3**. PR #184 migrates `PersonSmartCollectionVisibilityApplicationTests` to the same shared worker-disabled host. Workflow #1143 passed all four diagnostics once with no retry, advancing review-progress to **2/3** and person-visibility to **1/3**.

M19 feature work remains separately in review/in progress and must be preserved when branches are synchronized with `main`.

## Next concrete step

1. Merge PR #184 after its final-head validation remains green; do not count documentation-only reruns of the same PR as additional representative quarantine samples.
2. Migrate one of the two remaining ad-hoc quarantined hosts (`CollectionQueryApplicationTests` or `ReviewSuggestionGalleryApplicationTests`) on the next normal PR.
3. A successful next diagnostic lane should advance review-progress to **3/3**, person-visibility to **2/3**, and the newly migrated case to **1/3**.
4. After review-progress has three clean post-change samples, remove only that quarantine entry in a subsequent change and prove it executes exactly once in required shard coverage.
5. Continue collecting natural WI-0070 timing samples. Treat #1143 as measured blocker evidence rather than a new timing baseline; investigate or adjust shard timing only if the large variance recurs across representative runs.

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

## Repository validation

```powershell
./build.ps1
./test.ps1
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
./verify-review.ps1 -Mode Smoke -Configuration Release
```
