---
id: WI-0024
title: Add ongoing local synchronisation
milestone: M13
status_source: ../status/work-items.yaml
depends_on: [WI-0014, WI-0023]
affected_modules: [PhotoIdentity.Source.OneDriveSync, PhotoIdentity.Cli]
---

# WI-0024: Add ongoing local synchronisation

## Objective

Periodically scan the local OneDrive folder, detect new and changed files, queue hydration and processing, and rematch unknown faces after new exemplars.

## Acceptance criteria

- [ ] New photos are found without direct cloud API access.
- [ ] Changed files create new revisions.
- [ ] Reconciled moves preserve canonical labels.
- [ ] Backup and restore of canonical data are documented and tested.
