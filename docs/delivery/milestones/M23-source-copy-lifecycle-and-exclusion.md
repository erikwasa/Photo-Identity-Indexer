---
id: M23
title: Source-copy lifecycle and privacy exclusion
status_source: ../status/milestones.yaml
depends_on: []
---

# M23: Source-copy lifecycle and privacy exclusion

## Outcome

Photo Identity handles duplicate source copies, source deletion and included-photo moves conservatively while giving the operator a source-copy-specific privacy exclusion that permanently removes Photo Identity's retained pixels, crops, biometric/identity data and photo metadata without deleting the OneDrive original.

The behavioral contract is defined in [Source-copy lifecycle and privacy exclusion](../../product/source-copy-lifecycle.md) and the privacy decision in [ADR-0008](../../decisions/ADR-0008-source-copy-exclusion-and-purge.md).

## User-visible demonstration

A representative archive can demonstrate all of the following:

1. Two byte-identical files at different source paths appear as exact duplicates but remain independent source copies.
2. Excluding one duplicate purges only that copy; the other remains included and accessible.
3. Moving/renaming an ordinary included photo preserves its Photo Identity asset/history when the exact match is unambiguous.
4. Moving an excluded file creates a new non-excluded source copy that must be excluded again explicitly.
5. Deleting photos in OneDrive moves them into a reviewable **Removed from source** queue after reconciliation rather than immediately purging them.
6. Bulk **Exclude & purge** removes selected missing copies from Photo Identity.
7. A still-present private OneDrive photo can be manually excluded; the original remains in OneDrive while Photo Identity retains no photo-specific pixels, hashes, metadata, faces, embeddings or identity links after purge.
8. Excluded/purge-pending content cannot be opened through viewer, Smart Collection, slideshow, face-review or original/hydration paths.

## Work items

- [WI-0087](../work-items/WI-0087-exact-duplicate-inventory.md) - add authoritative exact-duplicate inventory without merging or suppressing independent source copies.
- [WI-0088](../work-items/WI-0088-source-move-reconciliation.md) - preserve included asset identity across exact, unambiguous source moves/renames while refusing ambiguous and excluded matches.
- [WI-0089](../work-items/WI-0089-source-copy-exclusion-boundary.md) - add durable source-copy exclusion state and enforce it below UI/media/processing boundaries.
- [WI-0090](../work-items/WI-0090-exclusion-purge-service.md) - implement crash-safe, resumable deletion of revision-linked catalogue state and filesystem derivatives.
- [WI-0091](../work-items/WI-0091-archive-lifecycle-review.md) - add Removed from source, Exact duplicates, Excluded and purge-status operator workflows with manual/bulk exclusion.

## Delivery sequence

1. WI-0087 establishes exact-content grouping and the ambiguity rules needed by reconciliation.
2. WI-0088 adds conservative included-photo move/rename preservation.
3. WI-0089 establishes the durable source-copy exclusion gate. It may be implemented independently of WI-0088 once WI-0087 semantics are understood.
4. WI-0090 makes exclusion a real privacy purge rather than a visibility flag.
5. WI-0091 exposes the complete lifecycle to the operator and performs end-to-end acceptance across duplicate, move, missing and privacy scenarios.

## Exit criteria

- Exact SHA-256 duplicates are discoverable without asset/history merging or automatic exclusion propagation.
- Included-photo move/rename reconciliation preserves identity only when the match is authoritative and unambiguous.
- Excluded source locators never follow move/rename reconciliation.
- Missing source copies remain reviewable and are not automatically purged.
- Manual exclusion is source-copy-specific, becomes durable before cleanup, and immediately denies all Photo Identity media/processing access.
- Purge removes all known local pixel derivatives and revision-linked photo/face/identity/metadata state while leaving the source original untouched.
- Purge is idempotent, crash-safe and retryable, with pending/failed state visible to the operator.
- A purged exclusion retains only the minimal source-locator tombstone required to keep that locator excluded.
- Re-inclusion starts from source content again rather than restoring purged identifications/history.
- Real-catalogue maintainer verification passes the duplicate, included-move, excluded-move, source-deletion, bulk-purge and private-still-present scenarios.

## Risks

- Partial/scoped scans can create false move candidates unless reconciliation is scope-aware.
- Multiple byte-identical copies can make move inference ambiguous; the implementation must prefer no reconciliation over a wrong one.
- Filesystem derivative deletion and catalogue cleanup can diverge on crash unless purge state is durable and resumable.
- A missed access path could leak excluded media unless exclusion is enforced below the UI/query layer.
- Purging canonical review links is intentionally irreversible and must remain clearly separated from ordinary missing-source handling.

## Evidence

M23 completed maintainer acceptance on 2026-09-26 with privacy-safe evidence only:

- Exact duplicate A/B: A was excluded and fully purged while B remained independently included and accessible.
- Included rename: accepted WI-0088 evidence preserved the same AssetId/history, reconciled one authoritative move and was idempotent on the following synchronization.
- Excluded rename: the old locator remained excluded while the moved source appeared as a new included catalogue copy with fresh processing.
- Source deletion: a real source deletion entered **Removed from source** after synchronization while retaining the verified revision for review.
- Bulk removed-source purge: two Removed entries were selected together, left the Removed queue immediately, completed purge, and exposed no thumbnail/original-view action afterward.
- Still-present privacy exclusion: the source original was left untouched, normal viewer access was blocked immediately, purge completed, and re-inclusion produced fresh catalogue processing rather than restoring the purged revision.
- Real-catalogue PostgreSQL residue check for the purged revision returned zero revision rows, zero direct revision-reference rows and zero tables with residue.
- The live PostgreSQL acceptance gate passed 53 persistence tests and 9 runtime/composition tests; automated WI-0090 coverage verifies derivative-file cleanup and dependent identity/review graph deletion.

Repository evidence intentionally contains no personal image content, crops, embeddings, private paths, source filenames, photo identifiers or hashes.
