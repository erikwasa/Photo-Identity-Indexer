---
id: M09
title: Azure VM pilot without identities
status_source: ../status/milestones.yaml
depends_on: [M07]
---

# M09: Azure VM pilot without identities

## Outcome

A small bundle runs on a temporary Azure VM using the same worker and SSH/SCP transfer.

## Work items

- [WI-0020](../work-items/WI-0020-azure-pilot.md)

## Exit criteria

- No app registration, service principal or managed identity is created.
- No OneDrive credential enters Azure.
- Results match local execution within tolerance.
- Actual cost is recorded and the VM is deallocated.
