---
id: WI-0151
title: Ingest supported videos and extract metadata plus poster proxies
milestone: M30
status_source: ../status/work-items.yaml
depends_on: [WI-0150]
related_adrs: []
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Source.Local, PhotoIdentity.Source.OneDriveSync, PhotoIdentity.Api, PhotoIdentity.Persistence.Postgres, docs]
---

# WI-0151: Ingest supported videos and extract metadata plus poster proxies

## Objective

Recognize supported video files as catalogue assets and derive enough metadata/poster imagery for normal library browsing.

## Why

The first useful step is making videos visible/searchable rather than silently unsupported files.

## In scope

- Recognize the activated MP4/MOV subset.
- Persist media kind, duration, dimensions/rotation, frame rate and container/codec/stream details, and available capture time/camera/GPS using documented precedence.
- Generate bounded durable *still* posters/review images tied to source revision; playable lower-resolution video is WI-0189.
- Make initial video discovery opt-in and do not automatically enqueue all discovered videos for analysis.
- Respect OneDrive availability and hydration rules, avoid large-file hydration for cheap enumeration and classify cases requiring local video bytes.
- Report unsupported codecs/containers explicitly.

## Out of scope

- Face recognition across frames.
- Universal transcoding.
- Video job scheduling (WI-0188), smaller playable video cache (WI-0189) and captions/embeddings (WI-0190).
- Live Photo pairing.

## Acceptance criteria

- [ ] Supported videos appear as assets with duration/media kind.
- [ ] A durable poster supports browsing without replaying the original.
- [ ] Date, GPS, duration, rotation, stream metadata and unsupported/unknown fields behave predictably.
- [ ] Default-off video support does not change photo-only ingestion or query results.
- [ ] Unsupported variants remain visible in reporting.
- [ ] Original videos remain read-only.

## Verification requirements

Source, metadata, persistence and representative real-file verification.

## WI-0151 implementation checkpoint 1 — isolated pilot (2026-10-14)

- Initial PR implements separate MOV/MP4 OneDrive discovery, safe opt-in local FFprobe metadata and bounded FFmpeg still posters. It does **not** yet persist these in the PostgreSQL catalogue or make videos visible in the library.
- CLI usage, safety contract and follow-up requirements: [video metadata/poster pilot](../../operations/video-metadata-pilot.md).
- Still required before completion: persisted media-kind/revision metadata and poster lifecycle, default-off app integration, source-copy exclusions/invalidations, gallery browsing, HDR/rotation correctness and tests on real videos.
- Mark WI-0151 **in_progress**, not complete; the first PR is a foundational implementation checkpoint and does not satisfy the acceptance criteria as a whole.

## Maintainer verification and checkpoint 2 — 2026-10-15

- [Sanitized real-video pilot evidence](../../operations/video-pilot-evidence-2026-10-15.md): 25 already-local MOV/MP4 clips probed without failure; 16 H.264/HEVC Main SDR JPEG posters looked correctly oriented, un-stretched and without visible colour defects. The operator observed no unexpected OneDrive downloads. Nine HEVC Main 10 HLG videos were *correctly deferred* pending tested HDR-to-SDR mapping. Longest metadata-probed HLG video was ~152 seconds, **not** a validated long-video poster.
- Checkpoint 2 implements PostgreSQL schema version 33: media-kind discriminator defaults existing assets/revisions to photo, video revision metadata keyed by immutable revision and versioned poster rows with foreign-key cascade. The migration and contract test are isolated from the pilot and insert **no** video records in a live catalogue. Explicit opt-in video scanning/insertion, source-copy exclusions, actual metadata/poster persistence APIs, safe gallery and photo-query isolation remain to be implemented.
- This is a foundation PR and must **not** be treated as end-to-end WI-0151 acceptance. Take a PostgreSQL backup and test migration on a rehearsal database before restarting a maintained production instance.
- Next: secure revision-bound save/query and exclusion filtering, opt-in ingest, deterministic poster invalidation/cleanup and presentation; separately validate HLG colour mapping using selected local clips.

## Maintained PostgreSQL acceptance regression — 2026-10-15

- After removing Docker Desktop, the operator installed standalone Docker Compose 5.6.0, and `podman compose` again started and authenticated the unchanged PostgreSQL 18 container through Podman 5.8.5/5.8.6. The Release build completed without warnings/errors and Windows localhost protocol acceptance passed.
- The full local `verify-postgres.ps1` persistence test filter exposed a stale literal schema-version expectation (`31`) in `PostgresPhotoListCollectionRepositoryTests`. Schema v33 was **correctly** reported as 33, so this was a test assertion failure (55/56 tests passed), **not** evidence of catalogue corruption or a networking problem.
- Replaced the literal with `PostgresCatalogueDatabase.CurrentSchemaVersion`. Updated the existing Windows CI PostgreSQL package-verification gate to run the **full** `FullyQualifiedName~Postgres` persistence suite against its disposable PostgreSQL runner rather than only the new v33 schema test. This adds small runtime overhead to catch future stale expectations during PR review. The repository run remains production-catalogue safe.
- Re-run `verify-postgres.ps1` locally after the correction merges. Schema v33 migration on the maintained catalogue still needs a backup and restored-rehearsal verification; the passing test does **not** constitute that rehearsal.

## PostgreSQL runtime acceptance and next safety gate — 2026-10-15

- Maintainer re-ran verify-postgres.ps1 after PR #528: Podman 5.8.5/5.8.6, standalone Docker Compose 5.6.0, PostgreSQL authentication, Windows loopback and Release build passed (zero warnings/errors); 56 PostgreSQL persistence tests and 7 runtime/composition integration tests passed. No production reset or Docker Desktop reinstatement was needed.
- Introduced an **isolated restored-database schema rehearsal command** in [video PostgreSQL schema rehearsal](../../operations/video-postgres-schema-rehearsal.md). It binds a successful restore report to a local named photoidentity_restore_* DB, requires schema v32 and identical pre-migration counts, then initializes v33 twice and checks that photo state/counts remain intact and video tables are empty.
- This command is **not** a live catalogue migration authorization. The operator must back up the actual maintained database, complete restore verification, and run the rehearsal before updating the maintained installation. Video catalogue ingestion and HLG poster implementation remain outstanding.

## Restored-catalogue rehearsal operator blocker — 2026-10-16

- The maintainer successfully completed backup, isolated PostgreSQL restore and source/restored table/constraint verification twice, most recently for a restored schema v32 catalogue. These are restore acceptance results, **not** v33 migration acceptance.
- The first rehearsal attempt failed while setting a DbConnectionStringBuilder property from a PowerShell PSObject. Corrected PowerShell coercion, but the CLI then rejected the interpreted database name before any database connection or schema migration.
- To remove the launcher-database-alias ambiguity safely, the CLI now selects the target database **exclusively from the successful restore verification report**, enforces the exact generated photoidentity_restore_YYYYMMDDHHMMSS_xxxxxx format, rejects the source name and non-local host, and still checks real schema v32 plus all pre-migration row counts before applying migration 33. Only localhost credentials are read from the private launcher environment. The operator no longer constructs or rewrites a connection string in PowerShell.
- CI regression tests cover production-name environment variables, missing/forged reports, remote hosts, and the v32 preflight. The maintainer must still run the actual rehearsal on the retained restored catalogue; WI-0151 remains **in_progress**.

## Maintained schema v33 accepted; next adapter checkpoint — 2026-10-16

- The maintainer verified the real catalogue's v32-to-v33 migration on an isolated restored copy: 76 existing catalogue table row counts unchanged, video metadata/poster tables empty, all existing assets/revisions remained photos and a second initialization was idempotent. The maintained application then reported /health status ok, catalogueProvider postgresql, PostgreSQL ready and schemaVersion 33; existing app operations were reported normal. This closes the WI-0151 **schema/deployment gate**, not full video-ingestion acceptance.
- This PR adds **persistence-only**, revision-bound video inspection and poster save/read contracts. They require an existing immutable video revision; no new scanner, API route, worker or automatic video writes are enabled. Saves reject photo/deleted revisions and excluded source copies, and reads suppress excluded source copies. Identical retries are idempotent; conflicting tool/profile/metadata or poster bytes refuse silent replacement. Poster paths are restricted to a safe durable relative video-posters location, and unsupported inspections cannot save posters.
- PostgreSQL integration tests use a disposable database and synthetic video/photo rows to verify round trips, idempotence, source-copy exclusion, photo rejection and path validation. Next checkpoints must provide **opt-in** source/video revision insertion, explicit local availability/hash verification, deletion/exclusion-driven durable poster cleanup and server-side photo-only gating before exposing video rows to normal app queries. HDR tone mapping and gallery presentation remain open.
- The adapter does **not** imply WI-0151 completion or authorization for mass video ingestion.

## Checkpoint 4 — verified-local video asset and revision registration (2026-10-16)

- Adds a dedicated PostgreSQL video asset/revision repository, **not called by any UI/API/worker or default photo scanner**. The caller must explicitly provide a locally available MOV/MP4 observation plus independently verified SHA-256; online-only/downloading placeholders and oversized sources are rejected before SQL.
- Reuses the same stable local-folder source identity; creates or revives *video-only* assets transactionally, rejects photo-owned source-key collisions, checks durable source-copy tombstones under a source lock, and preserves immutable (asset, hash) revision IDs. A new content hash creates a new revision. Repeated/reverted content refreshes observed time so latest-content selection follows the currently verified revision; an older poster must not be selected for an edited video.
- Photo-specific revision lookup excludes video media kinds. **Critical photo-scan isolation:** existing photo-only missing reconciliation previously updated all source assets and would have marked video rows deleted. Both missing-reconciliation overloads and baseline reads now target only media_kind=photo. Photo scan batches refuse video-owned source keys before touching availability/revision state, and the upsert has a photo-kind conflict guard. Disposable PostgreSQL tests cover unchanged hash, new content, reversion, photo-only exclusion, ignored video baselines, non-deletion by either missing-reconciliation overload, failed photo scan collision, online-only refusal and source-copy tombstones.
- **No actual video files are read or ingested by this PR**, no production records inserted and no automatic video functionality enabled. This is only the revision registration seam. Upcoming checkpoints must wire opt-in hash verification, broader photo-only query protections, video metadata/poster generation, revision invalidation and safe library presentation before maintained review.
- The maintainer requested **one end-to-end review only after the full WI-0151 scope is developed**. Keep the work item in progress until then.

## Checkpoint 5 — explicit local-only MOV/MP4 ingestion CLI (2026-10-16)

- Introduces a command-line-only, opt-in video metadata ingestion path. Dry-run by default; database writes require both --enable-video and --apply, a localhost PostgreSQL connection plus an explicitly selected --database name (because the launcher string may omit Database), and an explicit archive root. No app UI, normal photo scanner, background worker or automatic scheduling invokes it.
- Reuses stable local-folder source identity and OneDrive-aware video enumeration: skip online-only/downloading placeholders, check exclusions before reads, probe bounded FFprobe technical metadata, SHA-256 stream-hash only confirmed local originals, verify observed size/write time and availability before/after reading, and then create/reuse video revisions and persist supported/unsupported video inspection evidence. Tool checksum is recorded; only aggregate diagnostics printed.
- Technical capture/GPS/camera values intentionally remain unknown: the pilot detects tag presence but has not resolved documented capture/timezone/GPS provenance. No posters or browser cards are enabled; HLG is retained as HDR technical evidence, not incorrectly tone-mapped. See [operator-only local ingestion runbook](../../operations/video-local-metadata-ingestion.md).
- The CLI command is capped at 25 selected local files (8 default), at most 1 GiB per file, with bounded FFprobe process duration and no OneDrive hydration. Source-copy and photo-only guards from earlier checkpoints remain mandatory. Added option-gating, unknown-metadata, and observed-file-stability tests.
- **Not WI-0151 completion:** still requires verified capture/GPS precedence, poster file lifecycle and deletion/exclusion cleanup, safe browsing, and full maintained real-file validation after all checkpoints are developed.

## Checkpoint 6 — embedded capture/GPS/camera precedence (2026-10-16)

- Extends the bounded FFprobe parser to resolve explicit-offset embedded QuickTime creation date (higher precedence) and generic creation_time (fallback), yielding a UTC capture timestamp paired with an exact source scope/tag. Timezone-free local clocks are left unknown; file modified time is **not** substituted.
- Reads only strict ISO-6709 signed decimal-degree coordinates from available QuickTime/generic location tags and safely bounded camera make/model strings. Invalid/missing/unsupported representations remain NULL rather than guessed. No external web lookups, OneDrive hydration or background tasks.
- Opt-in video ingest writes these values to existing PostgreSQL v33 columns using inspection profile v2. Existing v1 evidence with conflicting new fields is not silently overwritten; controlled invalidation/backfill remains a later checkpoint.
- Pilot JSON suppresses capture timestamps, GPS and camera values via explicit JsonIgnore metadata properties, preserving privacy of aggregate diagnostic exports. Synthetic parser/projection tests cover offset conversion, precedence, invalid locations, unknowns and JSON suppression.
- See [embedded metadata precedence and privacy runbook](../../operations/video-local-metadata-ingestion.md). Representative maintained MOV/MP4 file verification stays deferred until the requested final review.

## Checkpoint 7 — revision-bound SDR poster materialization (2026-10-16)

- Added explicit, dry-run-first video posters CLI, separate from the photo scanner and the previously merged local-video metadata ingestion. It requires an existing local-folder source and a currently visible immutable video revision whose stored content SHA-256 exactly matches locally hashed original bytes. No OneDrive hydration, background worker, or full-archive automatically queued processing.
- For supported SDR files only (HLG/PQ deferred), trusted external FFmpeg generates a bounded JPEG under an app-owned derivative root independent of the source. JPEG signature, dimensions, 8 MiB bound and SHA-256 are verified before recording immutable video_still_posters row with revision, profile, tool checksum and relative path. Generated file paths are deterministic by revision and tool hash; temporary files are atomically moved without overwriting existing evidence; retries check existing bytes.
- The source file is rehashed after rendering to avoid assigning a poster to mismatched original content. The current active content revision and source-copy exclusions are checked again before saving poster rows; old revisions remain hidden from active poster selection. Existing photo processing stays unchanged.
- This checkpoint intentionally does not delete orphaned, superseded or excluded poster files: **a bounded dry-run-first physical cleanup and safe-serving/read policy must ship before the UI is enabled**. JSON and console outputs reveal only aggregate counts; original media remains read-only. See [durable poster runbook](../../operations/video-durable-posters.md).
- Automated tests cover explicit opt-in, exact current revision hash, SDR/HLG/PQ gating, path and JPEG size/dimension guards, and independent derivative roots. The maintainer's hands-on validation is deferred until the complete WI-0151 workflow exists. WI-0151 remains in progress.

## Checkpoint 8 — bounded source-proven poster cleanup (2026-10-16)

- Adds a separate dry-run-first `video posters-cleanup --enable-video` CLI command with explicit existing source root, derivative root, credentials environment variable and database selection. Requires `--apply` for deletion; a 24-hour minimum age and 25-file cap prevent recent or unbounded cleanup. No background jobs are enabled.
- PostgreSQL cleanup repository inventories revision-bound posters (including excluded and deleted assets) and marks each source revision active/stale. Conditional deletion locks the stable source and rechecks deleted/excluded/superseded status, exact profile/tool/revision/path/checksum and source ownership. It never retires a currently visible poster; repeat deletes are idempotent.
- Filesystem deletion is restricted to canonical `video-posters/wi0151-sdr-still-v1/<revision-id>/<tool-sha256>.jpg` files inside an independent application-owned derivative root, rejecting symlinks, suspicious names and wrong checksums. Aged unreferenced JPEGs can only be removed when the revision is still present in the selected source's PostgreSQL catalogue and provably noncurrent; no filename-only deletion.
- Disposable PostgreSQL and command-level tests cover current-versus-excluded poster retirement, source mismatch, immutable paths, minimum age and dry-run flags.
- **Important open privacy gate:** If source-copy purge has already deleted an excluded video's revision, remaining poster files no longer have provable source ownership. This command intentionally skips them. A future checkpoint must integrate these files into ADR-0008's durable source-copy purge manifest *before* activating any poster-serving endpoint or library UI. See [poster cleanup operations](../../operations/video-poster-cleanup.md).
- WI-0151 stays in progress pending complete deletion/exclusion integration, safe video browsing, HDR color review and the maintainer's consolidated real-file acceptance.

## Checkpoint 9 — video source-copy exclusion purge manifest integration (2026-10-16)

- Extends ADR-0008's existing durable pre-deletion source-copy purge manifest to include canonical `wi0151-sdr-still-v1` video JPEGs and temporary JPEGs still attributable to the selected video revision. Database poster rows and revision-specific orphan files are inventoried *before* any cascade deletes the video catalogue ownership link.
- Adds an optional video-poster derivative root to `SourceCopyPurgeRoots`; the maintained host defaults it to `LocalApplicationData/PhotoIdentity/video-derivatives` and allows override via `PhotoIdentity:VideoPosterRoot`. Operator-run `video posters` must use the same real root. A video source with revisions but no root fails closed; unrelated photo-only exclusion remains unchanged.
- Existing durable manifests are augmented with canonical video artifacts when source identity still exists, and cleanup after a crash/restart is idempotent. The service validates exact profile/revision/tool paths and any reparse points again at deletion time; it never deletes directories or originals based on database content.
- Disposable PostgreSQL regression checks missing-root refusal, known video poster plus interrupted-render orphan capture, same-source/revision scope, persistent manifest replay, non-excluded-video survival and catalogue deletion order. No schema migration or video browser route.
- Legacy exclusions already completed before this integration may have untracked orphan files lacking source identity; this checkpoint cannot safely identify them retroactively. Video browsing and final privacy/regression acceptance remain pending.

## Checkpoint 10 — atomic video poster publication vs exclusion (2026-10-16)

- The explicit `video posters --apply` command now enters a source-locked PostgreSQL publication scope **before FFmpeg writes a temporary JPEG** and keeps that lock through JPEG promotion and immutable poster row commit. Source-copy exclusions cannot insert their tombstones (and therefore cannot prepare a manifest) between file publication and row visibility. An already-excluded source refuses rendering before FFmpeg starts.
- Binds the publication to the exact source key, current video revision and SHA-256 content hash; rejects superseded revisions, unsupported metadata and non-Rec.709 transfer for the initial SDR profile. Direct poster saves also refuse superseded revisions, and poster reads return no stale historic revision as the current poster.
- A disposable PostgreSQL concurrency regression holds a poster publication callback open, requests concurrent source-copy exclusion, checks exclusion waits until the callback/row finishes, then confirms read suppression and refusal of any post-exclusion render callback. A stalled FFmpeg run intentionally delays exclusion only up to the existing bounded operator timeout.
- The previous durable manifest and source-root safety checks remain in force; no schema migration, photo pipeline changes or automatic video work. Safe browsing, full photo-query isolation, HDR review and final real-file acceptance remain pending.

## Checkpoint 11 — read-only video library and verified SDR poster serving (2026-10-16)

- Introduces a separate `/videos` page and minimal Advanced → Archive operations entry. The API `GET /api/videos` and `GET /api/videos/{revisionId}/poster` are **disabled by default**; enable only by configuring `PhotoIdentity:VideoLibraryEnabled=true`. No photo gallery, search, collection or slideshow routes are altered.
- Server-side PostgreSQL video-only projection lists current nonexcluded, nondeleted video revisions with bounded paging and their inspection status, captured UTC date, duration, dimensions and rotation. No source paths/source IDs, GPS, camera tags or sensitive local filesystem details reach the browser. Unsupported/uninspected videos remain visible with explicit unknowns and no invented poster.
- Poster serving requires current media_kind=video revision, supported inspection, explicit bt709 color transfer, canonical revision/profile/tool path, independent configured derivative root, safe path components, bounded JPEG bytes/signature and exact SHA-256 match. The source row remains locked through authorization and file verification so a concurrent exclusion cannot commit in the middle of a read. Errors fail closed with a generic not-found result and no private paths.
- Tests on disposable PostgreSQL cover photo-video isolation, superseded/other revision exclusion, tombstones, valid poster bytes, checksum tampering and pagination. No schema migration, no background video queue, no OneDrive hydration or original video serving.
- See [operator video library runbook](../../operations/video-library-readonly.md). Remaining gates are HDR color profile/real-file verification, photo-only query audit and maintainer's consolidated acceptance. WI-0151 stays in progress.

## Checkpoint 12 — default photo query isolation audit (2026-10-16)

- Hardened direct photo-only catalogue reads with explicit photo media-kind checks on asset and current revision: Photo Search candidates/embeddings/captions, Smart and Creative Collection queries, manual photo list create/update/snapshot, face-based collections and review, photo details, photo caption candidates and photo archive status/items. Video revisions remain exclusively accessible through opt-in video-specific APIs.
- Disposable PostgreSQL fixtures exercise video in the **same archive folder** without changing photo totals, video with a deliberately misleading legacy review proxy without joining photo semantic candidates, and refusal of real video revisions in manual photo lists. See [query isolation runbook](../../operations/video-photo-query-isolation.md).
- Existing schema-33 photo defaults remain unchanged, no new video scanning or background processing, and no OneDrive hydration. Other photo-specific legacy query paths still require complete workflow-based regression at final acceptance; this checkpoint is not a claim that every SQL query was automatically proven.
- WI-0151 remains in progress pending conservative HDR/HLG poster validation (the local pilot identified nine HLG clips), comprehensive isolation/privacy regression and the maintainer's one consolidated end-to-end verification.

## Checkpoint 13 — isolated HLG evaluation preview profile (2026-10-16)

- Added explicit FFprobe parsing for per-stream colour primaries and colour matrix (in addition to the already stored transfer evidence). An HDR evaluation poster is admitted only for local HEVC Main 10 `yuv420p10le` explicitly signalling **BT.2020 + BT.2020 nonconstant-luminance + HLG**. Missing/mismatched metadata, PQ, unsupported media or nonlocal placeholders remain deferred; no HDR assumption is made based on codec or camera.
- The existing isolated `video pilot` command accepts an **additional** `--preview-hlg` switch. Its new `pilot-hlg-tone-map-v1` output uses FFmpeg zscale linearization, Mobius tone mapping and Rec.709 conversion. The existing SDR poster filter/profile and default operation remain unchanged. Output is confined to private pilot derivatives, with original-file stability checks and bounded JPEG verification; it is **not** persisted or served.
- A synthetic FFmpeg 7.1.5 HEVC Main 10 HLG fixture was rendered to a 1280×720 JPEG, providing filter execution evidence rather than a claim of correct real-world appearance. Tests cover strict HLG eligibility, negative/missing colour metadata, PQ refusal and the exact filter pipeline. See [HLG evaluation runbook](../../operations/video-hlg-preview.md).
- This is an **engineering preparation checkpoint**, not end-to-end HDR acceptance. The maintainer's real HLG review remains deferred as requested. A later checkpoint must integrate any visually accepted profile through durable publication, exclusion/purge cleanup and safe browser delivery; remaining photo-only regressions and the consolidated real-file review also remain open. WI-0151 stays **in_progress**.

## Checkpoint 14 — photo background queues and legacy photo evidence isolation (2026-10-16)

- Following checkpoint 12's primary photo search/gallery/collection isolation, audited photo processing selection and found a separate, actionable gap: archive-analysis pending/completed selectors, review-proxy post-analysis/pending queues and evaluation-only photo-quality sampling did not constrain `media_kind`. The presence of a real video in the same source could therefore enqueue it into ordinary photo background work, or let deliberately malformed legacy companion evidence affect the counts/samples.
- All four selector families now explicitly require photo asset and current revision media kind. The existing photo-analysis completion writer and photo review-proxy writer reject video revision IDs; related photo completion lookups and single/bulk review-proxy reads suppress legacy video companion evidence. Video-specific repository reads, the opt-in video CLI and gallery remain unchanged.
- Disposable PostgreSQL mixed-media regression seeds both photo and video revisions sharing one source, including video legacy analysis/review-proxy rows, and checks photo-only candidates/completed totals, writer refusal and the photo-quality sample. See [isolation runbook](../../operations/video-photo-query-isolation.md). Existing snapshot UI/collection regressions and final maintainer acceptance remain necessary.
- No PostgreSQL migration, no new ingestion/worker, no OneDrive hydration, no change to original files and no HLG video promotion. WI-0151 stays **in_progress** until the HDR policy and one consolidated local acceptance are resolved.

## Checkpoint 15 — photo annotations and metadata backfill isolation (2026-10-16)

- Audited the next layer of photo-only repository APIs after checkpoint 14 and found manual photo person/tag/place/capture-date adapters that checked revision existence but not media-kind, together with photo metadata backfill that scanned all local revisions. Hardened both asset and revision `media_kind='photo'` guards on those photo-specific boundaries, preserving the manual capture-date revision lock.
- Photo capture/extended metadata and extraction-inspection writes now refuse video revision IDs at the SQL gate; their reads hide legacy video companion rows. Forced photo metadata backfill excludes videos. The opt-in video CLI retains its own video-specific metadata and does not gain photo annotation semantics (those belong to WI-0153).
- Disposable PostgreSQL tests seed a photo and a video in the **same source**, assert all affected photo-only reads/writes refuse the video and photo metadata backfill still processes the photo normally. See [query and write isolation](../../operations/video-photo-query-isolation.md).
- No schema migration, no automatic video analysis or OneDrive hydration, no new UI, and no originals modified. Remaining: assess any final photo/video privacy regressions, decide an explicitly conservative HDR/HLG treatment based on real-file quality review, and perform the maintainer's one consolidated acceptance before completing WI-0151.

## Checkpoint 16 — maintained launcher integration and one consolidated acceptance gate (2026-10-16)

- Confirmed **PR #557 merged** and its GitHub build run #2771 passed build-and-test, both integration shards, launcher verification and package verification. The first video slice's essential engineering work is now implemented: explicit local video asset/revision + technical evidence persistence, safe SDR still poster generation/cleanup/exclusion, video-only read-only library and photo-query/worker/annotation/metadata isolation. The accepted operational policy stays **SDR bt709 durable posters only**. HLG/PQ and unknown colour profiles are reportable as videos but do not produce a misleading live JPEG; the separate `pilot-hlg-tone-map-v1` is a private evaluation and must not be promoted without visual evidence and a new lifecycle-safe versioned profile.
- Final integration closes a Windows operator gap: the approved, default-off launcher now explicitly accepts `PhotoIdentity__VideoLibraryEnabled` and an existing absolute non-junction `PhotoIdentity__VideoPosterRoot` through its allowlisted settings. Enabling video in the launcher requires an explicit poster root so the renderer, serving and source-copy purge cannot silently use different defaults. The launcher verifier covers valid opt-in and invalid flag/missing/relative derivative roots; example launcher JSON stays disabled.
- Added a [single consolidated maintained-host acceptance procedure](../../operations/video-wi0151-consolidated-acceptance.md), comprising production-safe preflight and backup, small opt-in dry-run/ingest, SDR poster dry-run/apply, default-off and enabled browser checks, unchanged photo collection/search/slideshow counts, nonhydration/privacy checks, bounded stale cleanup dry-run and optional private HLG visual comparison.
- **Engineering implementation ready for review; not accepted locally yet.** The maintainer must perform the single requested real-file acceptance and report aggregate outcomes before WI-0151 can be marked completed. Do not silently require HDR tone-map production activation as part of this initial photo-safe SDR work item; if a new HDR policy is desired after visual review, separately approve the full versioned durability/exclusion/serving implementation.

## Completion notes

- Files changed:
- Trade-offs:
- Deferred work:
- Commands run:

## Maintained photo-only regression discovered — 2026-10-18

During schema-v33 WI-0151 Windows acceptance the operator verified video metadata/posters but encountered long-running Archive status, partial Slideshow library and person-filtered Smart Collection failures. Sanitized logs showed repeat ~30-second `PostgresSmartCollectionQueryRepository.QueryAsync` timeouts and cancelled Archive status requests; PostgreSQL activity samples showed parallel active queries. Representative photo-table planner statistics had no recorded `ANALYZE` and unreliable very-low row estimates.

The separate high-priority [WI-0202](WI-0202-photo-catalogue-query-performance-regression.md) / [issue #589](https://github.com/erikwasa/Photo-Identity-Indexer/issues/589) tracks read-only diagnosis, controlled statistics refresh and any needed query correction. The [runbook](../../operations/postgres-photo-query-regression-wi0202.md) explicitly prohibits destructive recovery. **WI-0151 remains in_review and must not be marked complete until Archive, saved/ad hoc Smart Collections and the full Slideshow library pass maintained photo-only regression checks.** Existing video checks do not override those failed photo checks.

## WI-0202 photo-only acceptance recovery — 2026-10-18

After targeted PostgreSQL `ANALYZE`, the maintainer reported **5/5** photo-only regression checks passing: Archive and its items load, previously failing people-filtered Smart Collections return photos, previously working collections remain correct, Slideshow library/covers populate, and repeat navigation succeeds. The new API log confirms HTTP 200 for all seven formerly failing saved Smart Collection filters and the ad hoc Smart Collection query, and Archive status now finishes around one second. See [WI-0202 recovery evidence](WI-0202-photo-catalogue-query-performance-regression.md).

This resolves the **observed photo-only acceptance symptoms** that blocked progress. WI-0202 itself remains `in_review` for causal/operational closeout; WI-0151 separately remains `in_review` until the remaining consolidated video-specific acceptance work has been reviewed. The observed recovery is not proof that a particular missing PostgreSQL planner-statistics value caused the issue, nor is it a reason to relax media-kind isolation.
