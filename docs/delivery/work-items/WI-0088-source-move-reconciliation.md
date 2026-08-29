---
id: WI-0088
title: Reconcile exact unambiguous source moves for included photos
milestone: M23
status_source: ../status/work-items.yaml
depends_on: [WI-0041, WI-0087]
related_adrs: [ADR-0008]
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Persistence.Sqlite, PhotoIdentity.Source.Local, PhotoIdentity.Source.OneDriveSync, documentation]
---

# WI-0088: Reconcile exact unambiguous source moves for included photos

## Objective

Preserve the existing AssetId and revision-linked Photo Identity history when an ordinary included source photo is renamed or moved and the source transition can be proven exactly and unambiguously.

## Why

The canonical data model intends asset identity to survive path reconciliation, but current scanning keys assets by source path. Without reconciliation, a rename/move appears as one missing asset plus one new asset and disconnects faces, review history, tags and metadata.

## In scope

- Add a reconciliation phase that compares newly discovered verified source copies with eligible missing candidates using authoritative SHA-256 identity.
- Reuse the existing AssetId/source-owned history when exactly one eligible missing included asset matches exactly one new source path.
- Update the asset's source key/path rather than creating a new logical asset for that proven move.
- Treat an old path that is still present plus a same-hash new path as a duplicate/copy, not a move.
- Refuse automatic reconciliation when multiple missing/current candidates make the mapping ambiguous.
- Exclude source locators with active exclusion state from move-candidate matching.
- Treat a same-hash new path corresponding to a previously excluded locator as a new independently controlled source copy.
- Make partial/included-folder scan behavior scope-safe so an unscanned path cannot be falsely declared moved.
- Require authoritative content verification before reconciling a newly discovered online-only/unverified item.
- Preserve archive source verification semantics and revision integrity.

## Out of scope

- Heuristic moves based only on filename, size, timestamps or directory similarity.
- Moving source files on disk.
- Operator-driven resolution of ambiguous duplicate/move cases.
- Carrying exclusion state to a new path.
- Perceptual similarity matching.

## Acceptance criteria

- [ ] Rename/move of one included local/verified file to one new path with the same authoritative SHA-256 preserves its AssetId.
- [ ] Existing revision-linked faces, people, tags, metadata and review history remain attached after the reconciled move.
- [ ] If both old and new same-hash paths exist, both remain separate source copies and no move reconciliation occurs.
- [ ] Multiple same-hash missing candidates remain unresolved rather than being guessed.
- [ ] A new unverified online-only path is not auto-reconciled from metadata alone.
- [ ] Scoped scans cannot reconcile against paths whose absence was not authoritatively established in the relevant scope.
- [ ] Excluded locators are never move candidates.
- [ ] Moving/renaming an excluded file results in a new non-excluded source copy at the new path.
- [ ] Repeated scans after a successful reconciliation are idempotent and do not create duplicate assets/revisions.

## Verification requirements

Automated scanner/persistence integration coverage is required for unique move, copy-not-move, ambiguous duplicates, excluded-source move, online-only verification and scoped-scan safety. Maintainer real-catalogue verification should include one harmless rename/move of an included test photo.

## Completion notes

- Files changed:
- Trade-offs:
- Deferred work:
- Commands run:
