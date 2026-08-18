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

## Non-goals

- Do not weaken endpoint behavior assertions merely to make the tests pass.
- Do not delete regression coverage.
- Do not re-enable broad xUnit in-process parallelism.
- Do not treat quarantine as permanent CI architecture.
