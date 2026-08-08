---
id: WI-0041
title: Add incremental permanent archive ingestion
milestone: M12
status_source: ../status/work-items.yaml
depends_on: [WI-0013, WI-0014, WI-0030, WI-0037]
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Source.Local, PhotoIdentity.Persistence.Sqlite, PhotoIdentity.Worker, PhotoIdentity.Cli, PhotoIdentity.Api]
---

# WI-0041: Add incremental permanent archive ingestion

## Objective

Create the permanent local catalogue directly from the real OneDrive-backed photo archive while allowing the maintainer to expand coverage one folder at a time without changing source identity, duplicating assets or repeatedly analysing unchanged photos.

The permanent local source root is conceptually stable. Selected recursive folders are relative coverage beneath that root, for example `1970/01`, `2026/08` or `em-wedding`. Adding a parent such as `1970` must subsume previously included children such as `1970/01` and `1970/02`.

## Intended operator workflow

1. Create a fresh permanent catalogue whose local source root is the archive root.
2. Include one or more relative folders beneath that root.
3. Synchronise all included coverage to discover new, changed and missing files.
4. Analyse only current revisions that have not already completed the selected detector/embedder processing profile.
5. Review faces, maintain identities and regenerate suggestions as normal.
6. Repeat synchronization whenever archive folders gain new photos or new folders are brought into coverage.

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

- [ ] One permanent local source root remains stable while included folder coverage expands.
- [ ] Included folders are stored as normalized root-relative paths; a parent inclusion subsumes redundant child inclusions.
- [ ] Synchronization scans every included folder recursively and discovers new or changed immutable revisions.
- [ ] Missing-file marking is scoped to the synchronized coverage and never tombstones catalogue assets outside included folders.
- [ ] Expanding from child folders to their parent reuses the same catalogue assets and revisions for unchanged files.
- [ ] Unchanged current revisions that already completed the selected processing profile are not run through detector/embedding inference again.
- [ ] Zero-face results count as successful analysis so they are not repeatedly reprocessed.
- [ ] The operator can see included folders plus discovered, analysed, pending, failed, unavailable and missing counts, with drill-down to individual images where useful.
- [ ] The operator can add an archive folder and trigger synchronization without changing the permanent source root.
- [ ] OneDrive-local availability is surfaced distinctly from processing failure.
- [ ] The permanent catalogue defaults to the selected local CenterFace confidence `0.5` single-pass detector and SFace FP32 embedder unless an explicit governed model change is made.
- [ ] The `1970/01` -> `1970/02` -> `1970` progression is covered by automated integration tests and Windows operator verification.

## Current implementation slice

The first implementation slice establishes the safety-critical source semantics before adding persistence and UI:

- root-relative archive coverage normalization collapses redundant child selections when a parent is added;
- partial source scans scope missing-file marking to the scanned relative root rather than the entire source;
- a local archive sync coordinator normalizes multiple included folders and synchronizes each recursive scope under one source identity; and
- integration coverage exercises expansion from `1970/01` and `1970/02` to `1970` without duplicating unchanged assets.

Next slices will persist the included-folder registry, expose archive include/list/sync operations, schedule only pending analysis profiles, and add archive coverage/status controls to the review application.

## Scope boundary

This work starts a fresh permanent catalogue from original archive paths. It does not migrate the disposable 560-image pilot catalogue into production. Detector migration of an already reviewed permanent catalogue remains the dedicated reconciliation problem solved by WI-0038.
