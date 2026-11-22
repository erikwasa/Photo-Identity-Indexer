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

Eliminate the transient HTTP 500 / disposed-`TestServer` failures that forced generic API integration tests into temporary quarantine, then restore every quarantined test to required pull-request coverage without retries or assertion weakening.

## Contract

Generic endpoint integration tests should run through the shared worker-disabled host foundation so unrelated production background workers do not interfere with endpoint tests. Worker-specific coverage must opt back into the relevant hosted workers explicitly.

A quarantined test may return to required coverage only after:

- its host/root-cause stabilization change is merged;
- it passes three representative post-change diagnostic CI runs;
- no retry or suppression mechanism hides failures; and
- required-shard accounting proves the restored test executes exactly once.

The quarantine list is `.github/flaky-integration-tests.txt`. The file may remain comment-only because the diagnostic script reads it unconditionally, but no active quarantine entries may remain at completion.

## Initial quarantined cases

- `CollectionQueryApplicationTests.Confirmed_collection_queries_support_explicit_any_and_all_semantics_without_paths`
- `ReviewProgressFilterApplicationTests.Model_filter_requires_both_model_id_and_exact_hash`
- `PersonSmartCollectionVisibilityApplicationTests.Merge_preserves_the_surviving_person_visibility_and_discards_the_retired_source_preference`
- `ReviewSuggestionGalleryApplicationTests.Gallery_requires_exact_model_revision_and_rejects_unknown_sort_or_confidence_group`

## Implementation summary

WI-0071 was delivered incrementally so restoration evidence remained attributable rather than relying on blanket retries:

- PR #182 began the shared-host migration with the review-progress case and improved bounded HTTP failure diagnostics.
- Subsequent slices migrated the other quarantined classes plus generic endpoint hosts exposed by representative CI runs.
- PR #188 made the worker-disabled compatibility `WebApplicationFactory<TEntryPoint>` the namespace default so legacy unqualified factories inherit the same isolation even before individual cleanup. `PhotoIdentityApiTestFactory` derives from that foundation; worker-specific tests explicitly opt back in.
- PR #187 restored review-progress after its three-sample evidence window.
- PR #190 restored person-visibility.
- PR #192 restored collection-query. Workflow #1188 (`32293845086`) passed 301 required integration tests with exact once-only accounting and advanced suggestion-gallery to its third clean post-change sample.
- PR #193 restored suggestion-gallery, the final active quarantine entry.

Earlier intermediate CI failures and migration details remain available in the merged PR history; this work-item document records the final durable contract and acceptance state.

## Final verification

PR #193 / workflow #1196 (`32295213938`) is the final restoration evidence:

- the diagnostic step reported `No quarantined integration tests are configured.`;
- shard 1 passed 158/158 required tests;
- shard 2 passed 144/144 required tests;
- total required integration coverage was therefore 302 tests;
- planned/results/unique accounting matched with `quarantined-results=0`;
- no retries were added;
- build, fast tests, living/generated documentation, `PublishedMinimum` review smoke and mixed-media verification passed;
- launcher/package verification correctly remained skipped for that test/docs-only PR.

The 2026-09-13 repository audit additionally confirms `.github/flaky-integration-tests.txt` has no active entries and the only fully-qualified ASP.NET `WebApplicationFactory` reference in the integration-test tree is the shared compatibility foundation itself.

## Acceptance criteria

- [x] Every tracked quarantine entry has a documented stabilization change or root cause.
- [x] Generic endpoint tests use the shared background-worker-disabled host unless they explicitly require production hosted workers.
- [x] Each quarantine entry completed its three representative clean post-change diagnostic runs.
- [x] `.github/flaky-integration-tests.txt` contains no active quarantine entries because all tracked tests are back in required shards.
- [x] No unconditional retry mechanism was introduced.
- [x] Required shard coverage proves every restored test executes exactly once.
- [x] `PhotoIdentity.Docs validate` and `generate --check` pass on the restoration changes.

## Non-goals

- Do not weaken endpoint behavior assertions merely to make tests pass.
- Do not delete regression coverage.
- Do not re-enable broad xUnit in-process parallelism.
- Do not treat quarantine as permanent CI architecture.
