---
id: M09
title: Azure VM pilot without identities
status_source: ../status/milestones.yaml
depends_on: [M07, M08, M15]
---

# M09: Azure VM pilot without identities

## Outcome

After the local workflow and documentation are proven, a small bundle runs on a temporary Azure VM using the same worker and SSH/SCP transfer.

## Work items

- [WI-0020](../work-items/WI-0020-azure-pilot.md)

## Exit criteria

- Azure access is available to the maintainer before execution begins.
- No app registration, service principal or managed identity is created.
- No OneDrive credential or canonical identity data enters Azure.
- Results match local execution within tolerance.
- Actual cost is recorded and the VM is deallocated.

## Scheduling note

Azure work is intentionally deferred while resources are unavailable. It must not block the local acceptance, multi-model comparison, collection-query or documentation tracks.
