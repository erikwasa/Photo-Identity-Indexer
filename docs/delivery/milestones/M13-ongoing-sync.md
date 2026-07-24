---
id: M13
title: Ongoing local synchronisation
status_source: ../status/milestones.yaml
depends_on: [M03, M12]
---

# M13: Ongoing local synchronisation

## Outcome

Periodic local scans discover new and changed OneDrive files, queue hydration and processing, and rematch unknown faces after new exemplars.

## Work items

- [WI-0024](../work-items/WI-0024-ongoing-sync.md)

## Exit criteria

- New photos are processed without a full rescan of content.
- Changed files create new revisions.
- Labels survive reconciled moves.
- Canonical data is backed up and restorable.
