# Build context

This file is intentionally a short handoff for the next development or verification session. It should describe only the current focus, the next concrete step and the small set of documents needed to continue.

Formal work-item lifecycle status and evidence are resolved by `PhotoIdentity.Docs` from the current registry plus archived terminal history.

## Current focus

**WI-0070 — Streamline pull-request validation and stabilize integration tests** has completed its implementation slices. PR #180 supplied the first independent post-Slice-3 timing sample at about 3m37s required Windows critical path, and PR #182 supplied the second at about 4m15s. Both stayed below the six-minute target with launcher/package skipped on unrelated changes. WI-0070 remains open only until a third normal representative PR sample is recorded.

**WI-0071 — Stabilize quarantined API integration tests** is the active implementation focus. Four transient-500 endpoint tests remain in `.github/flaky-integration-tests.txt`; they execute once per workflow in the visible non-blocking diagnostic lane with no retries.

PR #182 merged the first host migration for `ReviewProgressFilterApplicationTests`. Workflow #1139 was green and is clean post-change diagnostic sample **1/3** for `Model_filter_requires_both_model_id_and_exact_hash`.

The current slice migrates `PersonSmartCollectionVisibilityApplicationTests` from its bare per-class factory to `PhotoIdentityApiTestFactory`, adds bounded response diagnostics to its mutation and expected-404 requests, and deliberately leaves its flaky test quarantined while its own three-run evidence window begins.

M19 feature work remains separately in review/in progress and must be preserved when branches are synchronized with `main`.

## Next concrete step

1. Validate the person-visibility shared-host migration in GitHub Actions without adding retries or removing quarantine.
2. Count a successful diagnostic lane as review-progress sample **2/3** and person-visibility sample **1/3**.
3. Use that same normal PR as WI-0070's third independent representative timing sample; if it remains at or below six minutes, record the acceptance evidence and complete WI-0070.
4. Continue one quarantined host at a time so each stabilization change has attributable evidence.
5. Remove a quarantine entry only after its own stabilization change is merged and three consecutive representative diagnostic runs pass, then prove it executes exactly once in required shard coverage.

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

## Repository validation

```powershell
./build.ps1
./test.ps1
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
./verify-review.ps1 -Mode Smoke -Configuration Release
```
