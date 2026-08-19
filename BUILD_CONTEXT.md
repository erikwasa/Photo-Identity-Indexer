# Build context

This file is intentionally a short handoff for the next development or verification session. It should describe only the current focus, the next concrete step and the small set of documents needed to continue.

Formal work-item lifecycle status and evidence are resolved by `PhotoIdentity.Docs` from the current registry plus archived terminal history.

## Current focus

**WI-0070 — Streamline pull-request validation and stabilize integration tests** now has three independent successful sub-six-minute PR samples after Slice 3: PR #180 at about 3m37s, PR #182 at about 4m15s, and PR #186 workflow #1157 at about 3m04s overall. PR #184/#186 also demonstrated substantial hosted-runner timing variance, so timing baselines should not be rebalanced from a single pathological run. The remaining stability work is owned by WI-0071.

**WI-0071 — Stabilize quarantined API integration tests** is the active implementation focus. Four transient-500 endpoint tests remain in `.github/flaky-integration-tests.txt`; they execute once per workflow in the visible non-blocking diagnostic lane with no automatic retries.

PR #186 Slice 3 migrated `CollectionQueryApplicationTests` plus the generic API hosts surfaced by required-shard validation (`SmartCollectionPlaceLocationTests`, `PersonFeaturedFaceApplicationTests`, `BulkSuggestionReviewApplicationTests`, `ReviewApplicationTests`, `PhotoPlaceEnrichmentEndpointTests`, `CollectionOriginalAccessApplicationTests`, and `PhotoDetailsApplicationTests`) onto `PhotoIdentityApiTestFactory`. Custom proxy, GeoNames, Files-on-Demand, hydration-policy, and storage-probe test settings were preserved through the shared configuration callback. No assertions or quarantine membership were weakened.

Workflow #1157 (`32204959714`) is the first fully green substantive head after that sweep: build/fast tests, all four diagnostics, docs, PublishedMinimum, mixed-media verification, and both exact-coverage integration shards passed; launcher/package were skipped. PR #186 counts as one representative clean diagnostic sample, advancing review-progress to **3/3**, person-visibility to **2/3**, and collection-query to **1/3**. Suggestion-gallery has not yet received its stabilization migration.

M19 feature work remains separately in review/in progress and must be preserved when branches are synchronized with `main`.

## Next concrete step

1. Merge PR #186 after its documentation-only final-head validation is green.
2. Restore only `ReviewProgressFilterApplicationTests.Model_filter_requires_both_model_id_and_exact_hash` from `.github/flaky-integration-tests.txt` in a separate PR and prove it executes exactly once in required-shard coverage.
3. Migrate `ReviewSuggestionGalleryApplicationTests` to the shared host so all four original quarantine classes have a stabilization change.
4. Continue natural diagnostic samples: person-visibility needs 1 more, collection-query needs 2 more, and suggestion-gallery will need 3 after migration.
5. Remove each quarantine entry only when its own three-run criterion is satisfied; never use automatic retries as a substitute.
6. Keep WI-0070 timing variance as measured evidence; the three successful sub-six-minute samples are now satisfied.

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
