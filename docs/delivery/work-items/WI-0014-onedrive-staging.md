---
id: WI-0014
title: Add OneDrive availability and staging
milestone: M03
status_source: ../status/work-items.yaml
depends_on: [WI-0012, WI-0013]
affected_modules: [PhotoIdentity.Source.OneDriveSync]
---

# WI-0014: Add OneDrive availability and staging

## Objective

Add a filesystem source for the local OneDrive directory with placeholder detection, user-managed hydration, verified staging copies and content fingerprints.

## Acceptance criteria

- [ ] Online-only, local and failed availability states are distinct.
- [ ] Staged files are verified before processing.
- [ ] No OneDrive credentials or Graph permissions are requested.
- [ ] Staging cleanup cannot remove unverified source content.
