# Active face and collection safety for retired archive aliases

This is a follow-up to the v43 mapping (#836/#839) and the internal,
unexposed single-pair retire/restore rehearsal (#838). It does **not**
enable or execute any production retirement.

## Principle

An active case-only retirement records the secondary asset in
archive_case_alias_retirements with restored_at_utc IS NULL. That
secondary asset and all of its revisions, faces, crops, suggestions and
review history **remain stored**. The business rule is to remove its
observations from *active discovery and decision* paths, not erase
history.

## Active read surfaces covered

- Face-review gallery: page, total count, navigation, detail and
  filter run/model counts (PostgresReviewQueryRepository).
- Identity-suggestion gallery: page, count and navigation, using the
  same active-face predicate (PostgresSuggestionGalleryRepository).
- Similar-face gallery base face joins (PostgresSimilarFaceRepository).
- Person collection photo results, both confirmed and pending suggestion
  branches (PostgresCollectionQueryRepository).
- Auto-identity candidate selection: High suggestions, multi-evidence
  target faces, cluster members, and confirmed reference exemplars
  (PostgresIdentityAutoAssignmentService).

The ordinary photo search and Smart Collection queries already
check assets.deleted_at_utc IS NULL; retiring a secondary asset
marks it missing. The additional explicit retirement predicate in
face workflows avoids changing other historical missing-asset
behavior. Identity-match regeneration and other derivative pipeline
admissions still need a separate audit before production rollout.

## Write-side safeguards

The canonical review-action and suggestion-acceptance repositories
acquire a row lock on the face and its parent asset and verify there
is no active retirement. Consequently, a stale browser action
cannot add an assignment to a logically retired secondary. Racing
retirement and acceptance must serialize on the asset row. The stored
review/suggestion history remains accessible in the database and is
not deleted, and restoration reopens the normal active workflows.

## Disposable verification

Use PowerShell 7.6 on Windows:

~~~powershell
.\verify-postgres.ps1 -SkipContainerStart
~~~

The existing disposable-database retirement service integration test
is extended to confirm that face-review and suggestion gallery
pages/counts become empty during retirement, direct face details
cannot be opened through the active gallery, stale direct review
writes are rejected, and the original face appears again after
restoration. The original asset/revisions and later automatic
review history are asserted separately to remain intact.

No CLI, web endpoint, approval workflow, batch apply or source-file
deletion is introduced. Even if all tests pass, production execution
remains prohibited until a restore-tested backup, live-query
admission audit, bounded approval flow and post-apply/rollback
verification are in place.
