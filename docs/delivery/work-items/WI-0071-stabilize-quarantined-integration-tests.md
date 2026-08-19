---
id: WI-0071
title: Stabilize quarantined API integration tests
milestone: M00
status_source: ../status/work-items.yaml
depends_on: [WI-0070]
affected_modules: [tests/PhotoIdentity.Integration.Tests, .github/flaky-integration-tests.txt, .github/workflows/build.yml, docs/operations/testing-and-ci-strategy.md]
---

# WI-0071: Stabilize quarantined API integration tests

## Objective

Eliminate the remaining transient HTTP 500 failures in generic API integration tests so every temporarily quarantined test can return to the required pull-request shards without retries or failure masking.

## Context

WI-0070 established a shared generic API test host that removes unrelated production background workers. The first migrated hosted-client tests stopped reproducing their previous transient 500 failures, but several older endpoint tests still used ad-hoc `WebApplicationFactory` implementations and independently returned HTTP 500 or disposed-`TestServer` failures in otherwise unrelated CI runs.

PR #188 extends the shared-host rule to legacy factories at the integration-test namespace boundary: unqualified `WebApplicationFactory<TEntryPoint>` usages now inherit the same background-worker isolation by default even when a class has not yet been rewritten around `PhotoIdentityApiTestFactory`. Worker-specific coverage must explicitly opt back in.

The temporary quarantine is recorded in `.github/flaky-integration-tests.txt`. Quarantined tests still execute exactly once in a visible diagnostic lane on every PR/main workflow. Their failures are temporarily non-blocking only so unrelated development is not repeatedly blocked while the host instability is diagnosed. There are no automatic retries.

## Initial quarantined cases

- `CollectionQueryApplicationTests.Confirmed_collection_queries_support_explicit_any_and_all_semantics_without_paths`
- `ReviewProgressFilterApplicationTests.Model_filter_requires_both_model_id_and_exact_hash`
- `PersonSmartCollectionVisibilityApplicationTests.Merge_preserves_the_surviving_person_visibility_and_discards_the_retired_source_preference`
- `ReviewSuggestionGalleryApplicationTests.Gallery_requires_exact_model_revision_and_rejects_unknown_sort_or_confidence_group`

The list in `.github/flaky-integration-tests.txt` is canonical for current quarantine membership; this work-item list is the initial evidence set. The review-progress case has since completed its exit criterion and returned to required blocking coverage.

## Approach

1. Migrate generic endpoint tests from ad-hoc application factories to `PhotoIdentityApiTestFactory` where practical, and ensure legacy generic factories inherit the same worker-disabled host behavior by default.
2. Preserve or improve bounded server-response diagnostics so a remaining 500 exposes useful failure context.
3. Check for shared/static filesystem, SQLite, environment-variable, hosted-service, configuration or application-lifetime state that can leak across sequential factories.
4. Keep worker-specific coverage explicit rather than accidentally exercising background loops in every endpoint test.
5. Do not add unconditional retries.
6. Remove one quarantine entry at a time only after its stabilization criterion is met.

## Quarantine exit criterion

A quarantined test may return to the required integration shards when:

- its identified host/root-cause fix or shared-host migration is merged;
- it passes **three consecutive representative CI diagnostic runs** without a transient HTTP 500;
- no replacement retry/suppression mechanism is hiding failures; and
- removing the quarantine entry makes the test execute exactly once in the required shard coverage check.

If a test passes three times without any stabilization change but its original root cause remains unexplained, keep it quarantined until the evidence is sufficient to justify restoring blocking status.

## Acceptance criteria

- [x] Every current quarantine entry has a documented stabilization change or root cause.
- [x] Generic endpoint tests in scope use the shared background-worker-disabled host unless they explicitly require production hosted workers.
- [ ] Each quarantine entry has three consecutive clean diagnostic CI runs after its stabilization change.
- [ ] `.github/flaky-integration-tests.txt` is empty or removed because all tracked tests are back in the required shards.
- [x] No unconditional retry mechanism is introduced.
- [ ] Required shard coverage proves each restored test executes exactly once.
- [x] `PhotoIdentity.Docs validate` and `generate --check` pass.

## Implementation notes

### Slice 1 — migrate the review-progress quarantine case

Repository inspection confirmed that all four initially quarantined classes still used bare per-class `WebApplicationFactory` implementations that only set `PhotoIdentity:DatabasePath`; none of those local factories disabled the production place-enrichment, archive-advancement or identity-regeneration hosted workers.

The first stabilization slice intentionally changes only `ReviewProgressFilterApplicationTests` so the outcome remains attributable:

- replace its local `ReviewApiFactory` with `PhotoIdentityApiTestFactory`, which keeps detailed errors enabled and removes unrelated production hosted workers;
- use the shared bounded-body HTTP diagnostic helpers for successful string requests;
- add a reusable expected-status diagnostic helper so the quarantined negative-validation test will retain the response body if an expected HTTP 400 is replaced by another HTTP 500;
- keep `ReviewProgressFilterApplicationTests.Model_filter_requires_both_model_id_and_exact_hash` in `.github/flaky-integration-tests.txt` after this change; one green PR is not enough to restore blocking status;
- count only representative CI diagnostic runs after this stabilization change toward the three-run exit criterion.

PR #182 merged this slice as `05ecbf396b463c20732f1d18e02ec6336c81bccb`. Workflow #1139 (`32192474363`) was green: all four quarantined diagnostics executed exactly once and passed with no retries, so the review-progress case had clean post-change sample **1/3**. Both required integration shards also passed exact coverage, and the test/docs-only PR retained the WI-0070 fast path with launcher/package skipped.

### Slice 2 — migrate the person-visibility quarantine case

The second stabilization slice moves only `PersonSmartCollectionVisibilityApplicationTests` to the shared host. This class is deliberately different from Slice 1 because it exercises state-changing visibility and merge endpoints rather than only read/validation paths.

- replace the local `ReviewApiFactory` with `PhotoIdentityApiTestFactory` for all three tests in the class;
- route successful visibility updates and person merges through the bounded-body success diagnostic helper;
- route the unknown-person 404 assertion through the expected-status diagnostic helper so an unexpected 500 preserves response context;
- keep `PersonSmartCollectionVisibilityApplicationTests.Merge_preserves_the_surviving_person_visibility_and_discards_the_retired_source_preference` quarantined while its own three-run evidence window begins;
- retain the review-progress quarantine entry until it reaches its own three consecutive post-change samples.

Workflow #1143 (`32193192370`) validated this slice on PR #184: both required integration shards passed exact once-only coverage, all four quarantined diagnostics ran exactly once and passed with no retry, living/generated documentation passed, the PR `PublishedMinimum` smoke and mixed-media checks passed, and launcher/package verification was correctly skipped. This advanced the review-progress case to **2/3** clean post-change samples and started the person-visibility case at **1/3**.

The same workflow exposed a separate CI timing outlier that does not implicate this host migration. Shard 2 passed all 143 assigned tests but took 7m19s of test execution and recorded 439.4s aggregate test duration, compared with 64.5s for the same 143-test shard in workflow #1139. The unchanged `IdentityAutoAssignmentManualSupersessionTests.Manual_reassignment_supersedes_automatic_assignment_for_later_matching` case alone moved from 0.75s in #1139 to 124.39s in #1143. Multiple other unchanged classes were also materially slower. Do not rebalance the timing baseline from this single outlier; WI-0070 should retain measured follow-up for runner/test-duration variance and use additional natural runs or a robust multi-run baseline before changing shard weights.

PR #184 merged Slice 2 as `2bc2c926bb73df896f595da3b42940b1b8223205`. Its final-head workflow #1145 (`32194317428`) initially produced three unrelated required-shard HTTP 500 failures in `DetectorEvaluationComparisonApplicationTests`, `ReviewSuggestionApplicationTests`, and `ReviewQueueNavigationApplicationTests`. One manual failed-job diagnostic rerun passed both shards with no assertion, quarantine, or workflow-retry change. Before #184 merged, those three validation-exposed generic API hosts were also moved to `PhotoIdentityApiTestFactory` (preserving the detector-evaluation root callback where required). They therefore entered `main` as shared-host users rather than remaining known bare factories.

### Slice 3 — migrate collection-query and validation-exposed generic API hosts

The third stabilization slice started by moving `CollectionQueryApplicationTests` onto the shared background-worker-disabled host while keeping its behavior assertions and quarantine membership unchanged. Required-shard validation then repeatedly surfaced different generic endpoint classes that still used ad-hoc API factories. Rather than retrying until a favorable host lifecycle occurred or widening quarantine, the slice migrated only the classes actually exposed by those runs and preserved each class's existing custom settings/test doubles through the shared factory callback.

The planned quarantine migration:

- `CollectionQueryApplicationTests` now uses a thin `CollectionApiFactory : PhotoIdentityApiTestFactory`;
- every existing collection query, review-state, suggestion-scope, privacy, and validation assertion is unchanged;
- `CollectionQueryApplicationTests.Confirmed_collection_queries_support_explicit_any_and_all_semantics_without_paths` remains quarantined while its own post-change evidence window starts.

Validation-exposed shared-host migrations in the same PR:

- workflow #1149 (`32202975825`) first exposed `SmartCollectionPlaceLocationTests`; one **job-level manual diagnostic rerun** was used only to determine whether that failure was a one-off, and the rerun instead exposed `PersonFeaturedFaceApplicationTests` with a disposed `TestServer` plus `BulkSuggestionReviewApplicationTests` with another HTTP 500;
- no second rerun was attempted. `SmartCollectionPlaceLocationTests`, `PersonFeaturedFaceApplicationTests`, and `BulkSuggestionReviewApplicationTests` were moved to the shared host without assertion changes;
- workflow #1153 (`32203904268`) then passed shard 2 but exposed `ReviewApplicationTests` and `PhotoPlaceEnrichmentEndpointTests` on shard 1; both were still custom generic hosts, so they were migrated while preserving review-proxy and GeoNames settings via the configuration callback;
- workflow #1155 (`32204269605`) then passed shard 2 and exposed only `CollectionOriginalAccessApplicationTests` on shard 1. Its Files-on-Demand platform, hydration policy, and storage-probe test doubles were preserved through the shared callback;
- workflow #1156 (`32204649495`) passed shard 2 and exposed only `PhotoDetailsApplicationTests`, another database-path-only generic host, which was migrated without assertion changes.

Workflow #1157 (`32204959714`) is the first fully green substantive Slice 3 head after that evidence-driven sweep. Build/fast tests, all four once-only quarantined diagnostics, living/generated documentation, PR `PublishedMinimum` smoke, mixed-media verification, and both required exact-coverage integration shards passed; launcher/package verification remained correctly skipped for this test/docs-only PR. No automatic retry, new quarantine entry, or assertion weakening was introduced.

For quarantine accounting, PR #186 counts as **one** representative clean post-change sample regardless of its earlier diagnostic/validation heads. After #1157:

- review-progress was **3/3** (`#1139`, `#1143`, `#1157`) and eligible for a separate quarantine-restoration change;
- person-visibility was **2/3** (`#1143`, `#1157`);
- collection-query was **1/3** (`#1157`);
- suggestion-gallery had not yet received its own stabilization migration.

The successful #1157 workflow also supplies WI-0070's third independent sub-six-minute PR sample: it ran from 01:26:58 to 01:30:02 UTC, about **3m04s** overall. Together with PR #180 (~3m37s) and PR #182 (~4m15s), the required PR gate has three independent successful samples below six minutes. Earlier long/red PR #186 heads remain useful variance and host-instability evidence but are not substituted for successful timing samples.

### Slice 4 — restore review-progress and make generic host isolation the default

PR #187 removed only `ReviewProgressFilterApplicationTests.Model_filter_requires_both_model_id_and_exact_hash` from `.github/flaky-integration-tests.txt` after its three clean post-change samples. Workflow #1161 (`32274508367`) proved the restoration contract:

- the diagnostic lane ran exactly the remaining **3** quarantined tests once and passed with no retries;
- shard 1 passed **156/156** required tests with planned=results=unique=156 and `quarantined-results=0`;
- shard 2 passed **143/143** required tests with planned=results=unique=143 and `quarantined-results=0`;
- the required total increased from 298 to **299**, proving the restored review-progress test returned to blocking execution exactly once.

PR #187 merged as `8708508565a6b7633a96912de546727fcd2b8c5a`. The same representative run advanced person-visibility to **3/3** and collection-query to **2/3**.

PR #188 then migrated `ReviewSuggestionGalleryApplicationTests`, the final original quarantine class without its own stabilization change, to `PhotoIdentityApiTestFactory` while keeping the quarantine entry and all behavior assertions unchanged. Validation continued to expose the same standalone-host failure class in unrelated required tests:

- #1164 (`32277873839`) failed `CollectionManifestApplicationTests.Manifest_pages_internally_and_returns_complete_path_free_consumer_document` with HTTP 500 on shard 2; the class was migrated to the shared host with bounded diagnostics;
- #1165 (`32278292398`) passed the manifest path but failed `DetectorEvaluationApplicationTests.Detector_evaluation_sessions_persist_resume_and_export_private_ground_truth` with HTTP 500 on shard 1; its session-root configuration was preserved through the shared host callback;
- #1166 (`32279373428`) was the first fully green substantive PR #188 head and is the **single representative clean diagnostic sample** for this PR;
- #1167 (`32280183430`) later failed `SmartCollectionLocationUpdateCompatibilityTests.Update_without_place_preserves_existing_named_place_and_empty_place_clears_it` with a disposed `TestServer`; that database-path-only class was migrated to the shared host and #1168 (`32280599319`) was fully green;
- #1169 (`32281348228`) then failed `PersonAuditApplicationTests.Audit_rejects_invalid_scope_and_missing_people` with an unexpected HTTP 500 even though the code difference from the prior green head was documentation-only. Exact shard coverage still passed, confirming another generic host-lifecycle failure rather than a sharding defect.

After #1169, the remaining direct-factory surface was inventoried rather than continuing one class at a time. The integration-test namespace now defines a compatibility `WebApplicationFactory<TEntryPoint>` in `PhotoIdentityApiTestFactory.cs`. It derives from the ASP.NET Core testing factory and removes `PhotoPlaceEnrichmentHostedService`, `ArchiveAdvancementHostedService`, and `IdentityMatchRegenerationHostedService` during `CreateHost`. Because this happens after legacy subclasses register their custom host settings/test doubles, existing unqualified generic factories inherit the safe worker-disabled default without repetitive rewrites.

`PhotoIdentityApiTestFactory` now derives from that same compatibility foundation. `IdentityMatchRegenerationApplicationTests` is the intentional worker-specific exception and explicitly overrides `DisableBackgroundWorkers => false`, making its production background-worker dependency visible rather than accidental.

Workflow #1171 (`32282505397`) is fully green on that structural default: build/fast tests, all three quarantined diagnostics, living/generated documentation, PR `PublishedMinimum` smoke, mixed-media verification, and both exact-coverage integration shards passed; launcher/package remained skipped. #1171 validates the broader host architecture but does not count as a second independent PR #188 quarantine sample.

After the representative #1166 sample:

- person-visibility remains **3/3** and is restoration-eligible;
- collection-query is **3/3** (`#1157`, `#1161`, `#1166`) and is restoration-eligible;
- suggestion-gallery is **1/3** (`#1166`).

Next, restore person-visibility and collection-query in separate PRs so each removal proves exact once-only required coverage. Those two natural representative runs should also advance suggestion-gallery to **2/3** and then **3/3**. Suggestion-gallery can then be restored in its own final quarantine-removal change.

## Non-goals

- Do not weaken endpoint behavior assertions merely to make the tests pass.
- Do not delete regression coverage.
- Do not re-enable broad xUnit in-process parallelism.
- Do not treat quarantine as permanent CI architecture.
