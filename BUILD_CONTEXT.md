# Build context

This file is intentionally a short handoff for the next development or verification session. It should describe only the current focus, the next concrete step and the small set of documents needed to continue.

Formal work-item lifecycle status and evidence are resolved by `PhotoIdentity.Docs` from the current registry plus archived terminal history.

## Current focus

**WI-0070 — Streamline pull-request validation and stabilize integration tests** has three independent successful sub-six-minute PR samples after Slice 3: PR #180 at about 3m37s, PR #182 at about 4m15s, and PR #186 workflow #1157 at about 3m04s overall. Later WI-0071 runs continue to show material runner/setup/test-duration variance, so timing baselines must not be rebalanced from a single slow run. The remaining stability work is owned by WI-0071.

**WI-0071 — Stabilize quarantined API integration tests** is the active implementation focus. PR #187 merged as `8708508565a6b7633a96912de546727fcd2b8c5a` and restored `ReviewProgressFilterApplicationTests.Model_filter_requires_both_model_id_and_exact_hash` to required blocking coverage. Workflow #1161 proved exactly three tests remained in diagnostics and the required shards covered 299 tests exactly once (156 + 143), one more than before restoration.

PR #188 migrates `ReviewSuggestionGalleryApplicationTests`, the last original quarantine class without its own shared-host stabilization change, to `PhotoIdentityApiTestFactory`. Required-shard validation exposed additional standalone generic API hosts: #1164 failed `CollectionManifestApplicationTests` with HTTP 500, #1165 failed `DetectorEvaluationApplicationTests` with HTTP 500 after the manifest fix, #1167 failed `SmartCollectionLocationUpdateCompatibilityTests` with a disposed `TestServer`, and #1169 failed `PersonAuditApplicationTests.Audit_rejects_invalid_scope_and_missing_people` with an unexpected HTTP 500. None of those failed heads was rerun unchanged or counted as a representative quarantine sample.

`CollectionManifestApplicationTests`, `DetectorEvaluationApplicationTests`, and `SmartCollectionLocationUpdateCompatibilityTests` were migrated directly to `PhotoIdentityApiTestFactory`, preserving their custom settings through the shared callback. After #1169 showed another untouched generic factory failing, the remaining direct-factory surface was inventoried instead of continuing one class at a time. `PhotoIdentityApiTestFactory.cs` now provides a namespace-level compatibility `WebApplicationFactory<TEntryPoint>` that disables the three unrelated production workers at `CreateHost`, after legacy factories have registered their custom settings/test doubles. Existing unqualified legacy factories therefore inherit worker isolation automatically. `IdentityMatchRegenerationApplicationTests` is the intentional exception and explicitly overrides `DisableBackgroundWorkers => false` because it verifies production background regeneration behavior.

Workflow #1166 (`32279373428`) remains the single representative clean diagnostic sample for PR #188 quarantine accounting. Workflow #1171 (`32282505397`) is fully green on the structural host-default head: build/fast tests, all three quarantined diagnostics, living/generated documentation, PR `PublishedMinimum` smoke, mixed-media verification, and both exact-coverage integration shards passed; launcher/package were skipped. #1171 validates the broader host-default fix but does not count as another independent PR sample.

Collection-query has reached **3/3** (`#1157`, `#1161`, `#1166`) and is restoration-eligible. Person-visibility remains restoration-eligible after reaching **3/3** on #1161. Suggestion-gallery has post-change sample **1/3** from #1166.

M19 feature work remains separately in review/in progress and must be preserved when branches are synchronized with `main`.

## Next concrete step

1. Merge PR #188 after final-head documentation/handoff validation remains green.
2. Restore `PersonSmartCollectionVisibilityApplicationTests.Merge_preserves_the_surviving_person_visibility_and_discards_the_retired_source_preference` in its own PR and prove exact once-only required-shard execution; a clean representative run will also advance suggestion-gallery to **2/3**.
3. Restore `CollectionQueryApplicationTests.Confirmed_collection_queries_support_explicit_any_and_all_semantics_without_paths` in a separate subsequent PR; a clean run should advance suggestion-gallery to **3/3**.
4. Once suggestion-gallery has three clean post-change representative samples, restore it in its own final quarantine-removal PR and prove exact required coverage.
5. Keep quarantine removal one entry at a time, with no automatic retries or assertion weakening.
6. Reconcile the stale formal WI-0070/WI-0071 registry lifecycle when WI-0071 closes; do not mix registry-only churn into an attributable restoration change.

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
- `tests/PhotoIdentity.Integration.Tests/IdentityMatchRegenerationApplicationTests.cs`
- `tests/PhotoIdentity.Integration.Tests/ReviewProgressFilterApplicationTests.cs`
- `tests/PhotoIdentity.Integration.Tests/PersonSmartCollectionVisibilityApplicationTests.cs`
- `tests/PhotoIdentity.Integration.Tests/CollectionQueryApplicationTests.cs`
- `tests/PhotoIdentity.Integration.Tests/ReviewSuggestionGalleryApplicationTests.cs`
- `tests/PhotoIdentity.Integration.Tests/CollectionManifestApplicationTests.cs`
- `tests/PhotoIdentity.Integration.Tests/DetectorEvaluationApplicationTests.cs`
- `tests/PhotoIdentity.Integration.Tests/SmartCollectionLocationUpdateCompatibilityTests.cs`

## Repository validation

```powershell
./build.ps1
./test.ps1
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
./verify-review.ps1 -Mode Smoke -Configuration Release
```
