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

## Additional photo-worker queue and derivative isolation — checkpoint 14

The post-PR #552 audit found that the **photo-only background selectors** still lacked an explicit persisted media-kind discriminator. The general photo gallery/search selectors were isolated, but archived photo-analysis scheduling and review-proxy scheduling could admit a video asset from the SAME source when its revision lacked a photo analysis/proxy row.

This checkpoint adds `asset.media_kind = 'photo'` and `revision.media_kind = 'photo'` at all current-revision selection boundaries and filters the latest candidate subqueries in:

- Photo archive-analysis pending/current completed revision counts.
- Photo post-analysis review-proxy generation.
- Photo review-proxy generation pending queue.
- Evaluation-only photo-quality sample of current review proxies.

Photo-analysis completion and review-proxy recording also now **reject video revision IDs**, even with a valid photo processing run or proxy profile. Photo-analysis completion lookups and photo-proxy single/bulk readers suppress legacy video companion rows. No changes are made to the video-specific metadata, poster storage, explicit opt-in ingest or separate video library.

The disposable PostgreSQL regression adds analyzed and pending videos in the SAME source as ordinary photos. It checks that photo processing still schedules the expected photos, that a video cannot be recorded as completed photo analysis/proxy, that legacy video analysis/proxy rows cannot affect photo completed counts or reveal video proxy data, and that a photo-quality sample remains photo-only. The analyzed video is deliberately lexicographically earlier than the photo, so a missing-proxy selector without media guards would choose the video.

The test does not prove every historical SQL path is video-free. Maintained end-to-end photo/video counts, private exclusion and original read-only verification remain necessary in the final one-time operator review.


## Photo annotation and metadata isolation — checkpoint 15

Further audit after PR #555 identified a separate write-side boundary: photo manual person, tag, place and capture-date adapters checked only that a revision ID existed, so explicitly registered video revisions could be annotated through the photo-specific repositories. The local photo metadata backfill enumerated all source revisions, including videos, and the photo capture/extended metadata and inspection adapters accepted arbitrary revision IDs.

Checkpoint 15 requires **both** asset and revision `media_kind = 'photo'` before any manual photo annotation read/write. The photo capture-date editor retains its revision-level PostgreSQL lock (`FOR UPDATE OF revision`) while also checking both media-kind values. Photo metadata backfill now admits photo revisions only, including force mode, so it cannot probe a locally available video via the photo metadata worker. Photo capture/extended metadata and extraction-inspection saves use one SQL `INSERT … SELECT … WHERE EXISTS` gate and reject missing/video IDs without mutating photo tables; corresponding metadata reads hide any legacy video companion rows.

The disposable PostgreSQL photo-metadata backfill fixture now creates a photo and a video in the **same local source**. It verifies the photo remains eligible and persists metadata normally, both ordinary and forced photo backfill omit video, and video revision IDs fail manual-person, tag, place, capture-date and photo-metadata operations. There is no new scanner, schema migration, automatic video processing, cloud hydration, photo-to-video conversion or change to the original media.

The video catalogue has its own capture/GPS/camera contracts. Do not re-enable these photo APIs for videos as a shortcut; WI-0153 will define video-aware manual metadata separately. A broader audit and real-file operator acceptance are still required before WI-0151 is completed.
