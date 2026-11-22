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

## Completion

WI-0014 implemented placeholder-safe availability, user-managed hydration, independently verified staging fingerprints and cleanup restricted to current verified files. Pull request #26 merged and passed the full workflow. The human maintainer then validated the implementation against a real Personal OneDrive Files On-Demand folder. M03 completed on 2026-07-26.
