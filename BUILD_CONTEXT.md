# Build context

This file is intentionally a short handoff for the next development or verification session. It should describe only the current focus, the next concrete step and the small set of documents needed to continue.

Formal work-item lifecycle status and evidence are resolved by `PhotoIdentity.Docs` from the current registry plus archived terminal history.

## Current focus

**WI-0070 — Streamline pull-request validation and stabilize integration tests** has three independent successful sub-six-minute PR samples after Slice 3: PR #180 at about 3m37s, PR #182 at about 4m15s, and PR #186 workflow #1157 at about 3m04s overall. Later WI-0071 runs continue to show material hosted-runner/setup/test-duration variance, so timing baselines must not be rebalanced from a single slow run. The remaining integration-stability work is owned by WI-0071.

**WI-0071 — Stabilize quarantined API integration tests** is the active implementation focus. PR #188 merged the structural generic-host isolation change. PR #190 then restored `PersonSmartCollectionVisibilityApplicationTests.Merge_preserves_the_surviving_person_visibility_and_discards_the_retired_source_preference` to blocking required coverage and merged as `aa9b63300b7e11d5dfdffa18e305d4fed07a4944`.

Workflow #1177 (`32290736530`) is the representative PR #190 restoration run: the diagnostic lane ran exactly the two remaining quarantined tests once with no retries; shard 1 passed **156/156** and shard 2 passed **144/144**, with planned=results=unique and `quarantined-results=0` in both shards. Required coverage therefore increased from 299 to **300**, proving the restored person-visibility case returned exactly once. Build/fast tests, living/generated documentation, PR `PublishedMinimum` smoke, and mixed-media verification also passed; launcher/package verification were correctly skipped.

The same representative #1177 diagnostic run advances suggestion-gallery to **2/3** post-change samples (`#1166`, `#1177`). Collection-query had already reached **3/3** (`#1157`, `#1161`, `#1166`) and is restoration-eligible.

The current branch removes only `CollectionQueryApplicationTests.Confirmed_collection_queries_support_explicit_any_and_all_semantics_without_paths` from quarantine. Suggestion-gallery remains quarantined. A clean representative run should leave exactly one diagnostic test, increase required coverage from 300 to **301**, prove collection-query executes exactly once in the blocking shards, and advance suggestion-gallery to **3/3**.

M19 feature work remains separate and must be preserved when branches are synchronized with `main`.

## Next concrete step

1. Validate the collection-query restoration branch with exact required-shard coverage and one remaining once-only diagnostic test.
2. If clean, merge the collection-query restoration and record its representative workflow evidence without adding retries, assertion weakening, or quarantine widening.
3. With suggestion-gallery at **3/3**, restore `ReviewSuggestionGalleryApplicationTests.Gallery_requires_exact_model_revision_and_rejects_unknown_sort_or_confidence_group` in its own final quarantine-removal PR and prove exact required coverage.
4. After the quarantine is empty, reconcile the formal WI-0070/WI-0071 registry lifecycle and final documentation as a closeout change.
5. Do not rebalance `.github/test-timing-baseline.json` from a single hosted-runner outlier.

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
