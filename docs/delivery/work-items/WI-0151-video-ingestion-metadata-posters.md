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
- Explicit photo revision lookup now excludes media_kind video for both asset and revision. Disposable PostgreSQL tests cover unchanged hash, new content, reversion, photo-only exclusion, online-only refusal, private source tombstones, and photo-owned collision safety.
- **No actual video files are read or ingested by this PR**, no production records inserted and no automatic video functionality enabled. This is only the revision registration seam. Upcoming checkpoints must wire opt-in hash verification, broader photo-only query protections, video metadata/poster generation, revision invalidation and safe library presentation before maintained review.
- The maintainer requested **one end-to-end review only after the full WI-0151 scope is developed**. Keep the work item in progress until then.

## Completion notes

- Files changed:
- Trade-offs:
- Deferred work:
- Commands run:
