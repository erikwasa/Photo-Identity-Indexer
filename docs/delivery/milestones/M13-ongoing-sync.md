---
id: M13
title: Ongoing local synchronisation
status_source: ../status/milestones.yaml
depends_on: [M03, M12]
---

# M13: Ongoing local synchronisation

## Historical outcome

M13 originally proposed turning the local OneDrive source into a separately scheduled periodic scan/rematch workflow.

## Work items

- [WI-0024](../work-items/WI-0024-ongoing-sync.md)

## Retirement — 2026-09-12

This separate milestone is retired. Current production archive advancement already synchronizes included local OneDrive coverage and processes new material incrementally; M24/WI-0106 verified a real small daily-style increment without full-catalogue regeneration.

A continuously scheduled background watcher is not currently required or planned. WI-0024 is therefore closed administratively rather than claiming its original autonomous-periodic-scan contract was implemented. If unattended scheduling becomes desirable later, it should be scoped against the current PostgreSQL/archive-advancement architecture as new work.

## Historical exit criteria

- New photos are processed without a full rescan of content.
- Changed files create new revisions.
- Labels survive reconciled moves.
- Canonical data is backed up and restorable.
