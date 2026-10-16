# WI-0151 — photo-only query isolation before consolidated acceptance

This checkpoint makes the existing photo-specific PostgreSQL read paths explicit about **media_kind = photo**. Existing assets and revisions migrated to schema v33 already default to photo; the new constraints do not reinterpret or modify existing records. Video records remain available only through opt-in video-specific paths.

## Query surfaces hardened

- Photo Search: currently selected revision lookup, missing semantic embedding candidates, current embeddings, visual results, and caption search's current-revision CTEs now demand photo media kind at both the asset and revision selection boundary.
- Smart Collection and Creative Collection snapshot candidates: all five top-level archive revision selectors demand photo asset and revision kind; video metadata/GPS cannot be mistaken for photo metadata.
- Manual photo-list collections: creation/update reject requested video revision identifiers; slideshow materialization excludes any existing non-photo item (defense in depth).
- Collection face matches and review faces: explicitly constrain joined revision/face rows to photo media kind; existing face-only evidence can no longer make a video appear as a photo.
- Photo Details: non-photo revision ID no longer resolves as a photo details object.
- Photo Caption candidates: correlated latest revision selection now filters photo kind.
- Archive status/photo items: asset and current-revision selectors filter photo, so a video registered in the SAME source/folder does not change the existing photo archive totals, analysis states, or item pages.

## Disposable database regression evidence

Mixed-source PostgreSQL fixtures deliberately include real video asset/revision records. The archive-status photo totals and item list are unchanged when a MOV is present. The photo search test seeds an explicit video with legacy review-proxy evidence, and checks that it does not become a candidate. The manual photo-list test rejects video revision IDs during both create and update without changing existing photo snapshot order.

The CI build, persistence suite and integration shards remain the authoritative automated checks. These fixtures do **not** replace the maintainer's eventual local comparison of search/collection/slideshow output before and after enabling video support.

## Boundaries

- This checkpoint does not enable any new video processing, SQL migration, internet access, OneDrive hydration or video playback.
- Existing photo database queries outside the listed primary surfaces are not hereby certified. Additional workflow-level mixed-media checks and maintained photo regression remain part of WI-0151 acceptance.
- HLG and PQ color transforms still require a separately validated local visual sample; do **not** treat unknown or HDR transfer as verified SDR.

WI-0151 stays in progress. The maintainer requested a single consolidated real-file verification after implementation, not an intermediate manual test for this PR.
