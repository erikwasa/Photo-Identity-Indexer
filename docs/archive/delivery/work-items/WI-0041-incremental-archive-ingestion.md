---
id: WI-0041
title: Add incremental permanent archive ingestion
milestone: M12
status_source: ../status/work-items.yaml
depends_on: [WI-0013, WI-0014, WI-0030, WI-0037]
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Source.Local, PhotoIdentity.Source.OneDriveSync, PhotoIdentity.Persistence.Sqlite, PhotoIdentity.Worker, PhotoIdentity.Cli, PhotoIdentity.Api, PhotoIdentity.Web]
---

# WI-0041: Add incremental permanent archive ingestion

## Objective

Create the permanent local catalogue directly from the real OneDrive-backed photo archive while allowing the maintainer to expand coverage one folder at a time without changing source identity, duplicating assets or repeatedly analysing unchanged photos.

The permanent local source root is conceptually stable. Selected recursive folders are relative coverage beneath that root, for example `1970/01`, `2026/08` or `em-wedding`. Adding a parent such as `1970` must subsume previously included children such as `1970/01` and `1970/02`.

## Intended operator workflow

1. Create a fresh permanent catalogue whose local source root is the archive root.
2. Include one or more relative folders beneath that root.
3. Synchronise all included coverage to discover new, changed and missing files plus current OneDrive local/cloud availability.
4. Hydrate any required OneDrive placeholders through the sync client and synchronize again; Photo Identity does not intentionally hydrate placeholders itself.
5. Analyse only locally available current revisions that have not already completed the selected detector/embedder processing profile.
6. Review faces, maintain identities and regenerate suggestions as normal.
7. Repeat synchronization whenever archive folders gain new photos or new folders are brought into coverage.

The initial target workflow must support this progression:

```text
include 1970/01
sync

include 1970/02
sync

include 1970
sync
```

The final `1970` sync must reuse the already catalogued `1970/01` and `1970/02` assets, discover any newly added photos in those folders, and discover previously uncovered files under other `1970` children without creating duplicate source identities.

## Acceptance criteria

- [x] One permanent local source root remains stable while included folder coverage expands.
- [x] Included folders are stored as normalized root-relative paths; a parent inclusion subsumes redundant child inclusions.
- [x] Synchronization scans every included folder recursively and discovers new or changed immutable revisions.
- [x] Missing-file marking is scoped to the synchronized coverage and never tombstones catalogue assets outside included folders.
- [x] Expanding from child folders to their parent reuses the same catalogue assets and revisions for unchanged files.
- [x] Unchanged current revisions that already completed the selected processing profile are not run through detector/embedding inference again.
- [x] Zero-face results count as successful analysis so they are not repeatedly reprocessed.
- [x] The operator can see included folders plus discovered, analysed, pending, failed, unavailable and missing counts, with drill-down to individual images where useful.
- [x] The operator can add an archive folder and trigger synchronization without changing the permanent source root.
- [x] OneDrive-local availability is surfaced distinctly from processing failure.
- [x] The permanent catalogue defaults to the selected local CenterFace confidence `0.5` single-pass detector and SFace FP32 embedder unless an explicit governed model change is made.
- [x] The `1970/01` -> `1970/02` -> `1970` progression is covered by automated integration tests and Windows operator verification.

## Implementation slices

The first merged slice established the safety-critical source semantics:

- root-relative archive coverage normalization collapses redundant child selections when a parent is added;
- partial source scans scope missing-file marking to the scanned relative root rather than the entire source;
- a local archive sync coordinator normalizes multiple included folders and synchronizes each recursive scope under one source identity; and
- integration coverage exercises expansion from `1970/01` and `1970/02` to `1970` without duplicating unchanged assets.

The second merged slice added the persistent operator boundary:

- catalogue schema version 10 stores exactly one permanent archive source plus normalized included folders;
- `archive include` configures the root and adds recursive relative coverage while refusing a later root replacement;
- `archive list` reports the stored root and normalized coverage;
- `archive sync` synchronizes all stored coverage and reports supported, new, unchanged and missing counts; and
- integration coverage exercises child-to-parent collapse, persisted configuration and discovery of a new image added to a previously covered child.

The third merged slice added exact-profile incremental analysis:

- a canonical analysis-profile identity combines the exact detector-pipeline hash with detector and embedder model hashes plus the alignment protocol;
- successful immutable-revision completion is recorded independently of face count, so zero-face images are durable successes;
- `archive analyze` uses the governed CenterFace confidence `0.5` single-pass detector plus SFace FP32 and schedules only current revisions missing that exact profile;
- `archive resume` reconstructs an interrupted run and rejects a profile mismatch before inference resumes;
- `archive status` reports the registered analysis-profile hash and durable processing progress; and
- a changed image revision becomes pending again while an unchanged completed revision remains skipped.

The fourth merged slice added the local archive operator workspace:

- `/archive` shows the normalized included-folder set and current, analysed, pending, failed and missing counts for the whole archive and each included coverage root;
- the page configures the initial permanent root without returning that full source path to browser status responses, then adds later folders as root-relative coverage;
- operators can synchronize included folders directly from the review application;
- analysis advances through bounded one-image HTTP steps backed by the durable processing run, resuming an existing non-terminal run before creating another run; and
- the review application resolves the governed profile from `PhotoIdentity__RepositoryRoot` or a nearby checkout and keeps analysis disabled with an actionable message when that configuration cannot be resolved.

The fifth merged slice closed the pre-production availability and diagnosis boundary:

- archive synchronization uses the existing OneDrive Files On-Demand classifier even though the permanent source identity remains the same `local-folder` root;
- local, online-only, downloading, unavailable and availability-error states are persisted independently of immutable revisions, so placeholders are catalogued without opening or hashing them;
- only assets currently marked local are eligible for archive-analysis scheduling, while a queued job rechecks Windows cloud-file attributes immediately before opening bytes to close the sync-to-analysis race;
- `/archive` shows local/cloud availability separately from detector/embedder failures and warns that hydration remains a user-managed OneDrive action;
- `/api/archive/items` and the Archive image drill-down expose only root-relative paths with availability, analysis state and latest processing error; and
- regression coverage verifies `local -> online-only -> local` without opening the placeholder, without creating a duplicate revision and with analysis eligibility restored after hydration.

## Pre-verification storage boundary

Before starting the human Windows verification against the real archive, the maintainer identified a production constraint that changes the required steady-state workflow: the target computer has roughly 150 GB free while the logical archive is roughly 330 GB. Full or long-lived hydration of the complete archive is therefore not a valid prerequisite.

WI-0041 is blocked on [WI-0042](WI-0042-bounded-archive-storage.md), which adds bounded original hydration, permanent review proxies, explicit full-resolution retrieval and local-capacity controls. The already-merged WI-0041 ingestion, availability and exact-profile semantics remain valid; final real-archive verification resumes after WI-0042 is implemented and locally verified.

## Scope boundary

This work starts a fresh permanent catalogue from original archive paths. It does not migrate the disposable 560-image pilot catalogue into production. Detector migration of an already reviewed permanent catalogue remains the dedicated reconciliation problem solved by WI-0038.

## Verification completion

The maintainer completed the required real Windows/OneDrive verification on 2026-08-10. Privacy-sensitive local details remain outside Git.
