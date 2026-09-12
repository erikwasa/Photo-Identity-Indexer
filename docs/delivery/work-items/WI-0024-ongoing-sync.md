---
id: WI-0024
title: Add ongoing local synchronisation
milestone: M13
status_source: ../status/work-items.yaml
depends_on: [WI-0014, WI-0023]
affected_modules: [PhotoIdentity.Source.OneDriveSync, PhotoIdentity.Cli]
---

# WI-0024: Add ongoing local synchronisation

## Historical objective

The original plan was to add a separately scheduled periodic local OneDrive scan that automatically discovered new and changed files, queued processing and refreshed matching.

## Retirement — 2026-09-12

This separate work item is retired. Current archive advancement already synchronizes included local OneDrive coverage and processes newly discovered photos incrementally. M24/WI-0106 verified a real small increment without full-catalogue regeneration.

A continuously scheduled watcher is not currently required. If unattended scheduling becomes useful later, it should be newly scoped against the current PostgreSQL/archive-advancement architecture rather than reviving this older item.

The canonical status is an administrative closeout and does not claim the original autonomous-periodic-scan contract was implemented.

## Historical acceptance criteria

- New photos are found without direct cloud API access.
- Changed files create new revisions.
- Reconciled moves preserve canonical labels.
- Backup and restore of canonical data are documented and tested.
