---
id: M03
title: OneDrive synchronised source
status_source: ../status/milestones.yaml
depends_on: [M02]
---

# M03: OneDrive synchronised source

## Outcome

The local OneDrive folder can be scanned safely with explicit placeholder availability, user-managed hydration, staging and content fingerprints.

## Work items

- [WI-0014](../work-items/WI-0014-onedrive-staging.md)

## Exit criteria

- Online-only and local files are distinguished.
- Hydrated files can be staged and verified.
- No OneDrive credentials are requested.
- Temporary staging content can be safely removed.

## Current work

WI-0014 is implementing the complete sync-root boundary. Filesystem placeholder attributes provide point-in-time availability without intentionally opening online-only content. Locally hydrated files are copied to an external staging directory, independently re-hashed and given verification sidecars. Cleanup is restricted to current verified files and never recursively removes directories.
