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

WI-0070 established a shared generic API test host that removes unrelated production background workers. The first migrated hosted-client tests stopped reproducing their previous transient 500 failures, but several older endpoint tests still use ad-hoc `WebApplicationFactory` implementations and have independently returned HTTP 500 in otherwise unrelated CI runs.

The temporary quarantine is recorded in `.github/flaky-integration-tests.txt`. These tests still execute exactly once in a visible diagnostic lane on every PR/main workflow. Their failures are temporarily non-blocking only so unrelated development is not repeatedly blocked while the host instability is diagnosed. There are no automatic retries.

## Initial quarantined cases

- `CollectionQueryApplicationTests.Confirmed_collection_queries_support_explicit_any_and_all_semantics_without_paths`
- `ReviewProgressFilterApplicationTests.Model_filter_requires_both_model_id_and_exact_hash`
- `PersonSmartCollectionVisibilityApplicationTests.Merge_preserves_the_surviving_person_visibility_and_discards_the_retired_source_preference`
- `ReviewSuggestionGalleryApplicationTests.Gallery_requires_exact_model_revision_and_rejects_unknown_sort_or_confidence_group`

The list in `.github/flaky-integration-tests.txt` is canonical for current quarantine membership; this work-item list is the initial evidence set.

## Approach

1. Migrate generic endpoint tests from ad-hoc application factories to `PhotoIdentityApiTestFactory` where they do not intentionally test production hosted workers.
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

- [ ] Every current quarantine entry has a documented stabilization change or root cause.
- [ ] Generic endpoint tests in scope use the shared background-worker-disabled host unless they explicitly require production hosted workers.
- [ ] Each quarantine entry has three consecutive clean diagnostic CI runs after its stabilization change.
- [ ] `.github/flaky-integration-tests.txt` is empty or removed because all tracked tests are back in the required shards.
- [ ] No unconditional retry mechanism is introduced.
- [ ] Required shard coverage proves each restored test executes exactly once.
- [ ] `PhotoIdentity.Docs validate` and `generate --check` pass.

## Implementation notes

### Slice 1 — migrate the review-progress quarantine case

Repository inspection confirmed that all four initially quarantined classes still use bare per-class `WebApplicationFactory` implementations that only set `PhotoIdentity:DatabasePath`; none of those local factories disables the production place-enrichment, archive-advancement or identity-regeneration hosted workers.

The first stabilization slice intentionally changes only `ReviewProgressFilterApplicationTests` so the outcome remains attributable:

- replace its local `ReviewApiFactory` with `PhotoIdentityApiTestFactory`, which keeps detailed errors enabled and removes unrelated production hosted workers;
- use the shared bounded-body HTTP diagnostic helpers for successful string requests;
- add a reusable expected-status diagnostic helper so the quarantined negative-validation test will retain the response body if an expected HTTP 400 is replaced by another HTTP 500;
- keep `ReviewProgressFilterApplicationTests.Model_filter_requires_both_model_id_and_exact_hash` in `.github/flaky-integration-tests.txt` after this change; one green PR is not enough to restore blocking status;
- count only representative CI diagnostic runs after this stabilization change toward the three-run exit criterion.

PR #182 merged this slice as `05ecbf396b463c20732f1d18e02ec6336c81bccb`. Workflow #1139 (`32192474363`) was green: all four quarantined diagnostics executed exactly once and passed with no retries, so the review-progress case has clean post-change sample **1/3**. Both required integration shards also passed exact coverage, and the test/docs-only PR retained the WI-0070 fast path with launcher/package skipped.

### Slice 2 — migrate the person-visibility quarantine case

The second stabilization slice moves only `PersonSmartCollectionVisibilityApplicationTests` to the shared host. This class is deliberately different from Slice 1 because it exercises state-changing visibility and merge endpoints rather than only read/validation paths.

- replace the local `ReviewApiFactory` with `PhotoIdentityApiTestFactory` for all three tests in the class;
- route successful visibility updates and person merges through the bounded-body success diagnostic helper;
- route the unknown-person 404 assertion through the expected-status diagnostic helper so an unexpected 500 preserves response context;
- keep `PersonSmartCollectionVisibilityApplicationTests.Merge_preserves_the_surviving_person_visibility_and_discards_the_retired_source_preference` quarantined while its own three-run evidence window begins;
- retain the review-progress quarantine entry until it reaches its own three consecutive post-change samples.

Workflow #1143 (`32193192370`) validated this slice on PR #184: both required integration shards passed exact once-only coverage, all four quarantined diagnostics ran exactly once and passed with no retry, living/generated documentation passed, the PR `PublishedMinimum` smoke and mixed-media checks passed, and launcher/package verification was correctly skipped. This advances the review-progress case to **2/3** clean post-change samples and starts the person-visibility case at **1/3**.

The same workflow exposed a separate CI timing outlier that does not implicate this host migration. Shard 2 passed all 143 assigned tests but took 7m19s of test execution and recorded 439.4s aggregate test duration, compared with 64.5s for the same 143-test shard in workflow #1139. The unchanged `IdentityAutoAssignmentManualSupersessionTests.Manual_reassignment_supersedes_automatic_assignment_for_later_matching` case alone moved from 0.75s in #1139 to 124.39s in #1143. Multiple other unchanged classes were also materially slower. Do not rebalance the timing baseline from this single outlier; WI-0070 should retain measured follow-up for runner/test-duration variance and use additional natural runs or a robust multi-run baseline before changing shard weights.

The remaining collection-query and suggestion-gallery quarantined classes stay unchanged in this slice. No retry or quarantine removal is introduced.

## Non-goals

- Do not weaken endpoint behavior assertions merely to make the tests pass.
- Do not delete regression coverage.
- Do not re-enable broad xUnit in-process parallelism.
- Do not treat quarantine as permanent CI architecture.
