# Build context

This file is intentionally a short handoff for the next development or verification session. It should describe only the current focus, the next concrete step and the small set of documents needed to continue.

Formal work-item lifecycle status and evidence are resolved by `PhotoIdentity.Docs` from the current registry plus archived terminal history.

## Current focus

**WI-0070 — Streamline pull-request validation and stabilize integration tests** has three independent successful sub-six-minute PR samples after Slice 3: PR #180 at about 3m37s, PR #182 at about 4m15s, and PR #186 workflow #1157 at about 3m04s overall. PR #184/#186 also demonstrated substantial hosted-runner timing variance, so timing baselines should not be rebalanced from a single pathological run. The remaining stability work is owned by WI-0071.

**WI-0071 — Stabilize quarantined API integration tests** is the active implementation focus. PR #187 removes only `ReviewProgressFilterApplicationTests.Model_filter_requires_both_model_id_and_exact_hash` from `.github/flaky-integration-tests.txt` after its shared-host migration and three consecutive representative clean diagnostic runs.

Workflow #1161 (`32274508367`) proves the restoration behavior on the substantive one-line change: the diagnostic lane ran exactly **3** remaining quarantined tests once with no retries; integration shard 1 passed **156/156** required tests and shard 2 passed **143/143**, with planned=results=unique and zero quarantined results in both shards. The total required count is therefore **299**, exactly one higher than the pre-restoration 298-test gate, proving the restored review-progress case returned to blocking coverage exactly once.

The same representative #1161 diagnostic run advances the still-quarantined post-change counters to: person-visibility **3/3**, collection-query **2/3**, and suggestion-gallery **not started** because its shared-host stabilization migration has not yet occurred.

M19 feature work remains separately in review/in progress and must be preserved when branches are synchronized with `main`.

## Next concrete step

1. Merge PR #187 after its final-head documentation/handoff validation remains green.
2. Migrate `ReviewSuggestionGalleryApplicationTests` to `PhotoIdentityApiTestFactory` next so the last original quarantine case begins its own post-change evidence window.
3. Keep person-visibility quarantined for one more PR despite already reaching 3/3; after the suggestion-gallery migration, restore person-visibility in a separate PR so that restoration run also advances suggestion-gallery naturally.
4. Collection-query is at **2/3** and should reach **3/3** on the suggestion-gallery migration PR if that diagnostic lane is clean; restore it only in a later separate PR.
5. Continue one-entry-at-a-time quarantine removal with exact required-shard coverage and no automatic retries.
6. Reconcile the stale formal WI-0070/WI-0071 registry lifecycle when the stabilization work is being closed; do not mix registry-only churn into an attributable quarantine-restoration code change.

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
