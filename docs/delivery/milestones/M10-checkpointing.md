---
id: M10
title: Azure checkpointing
status_source: ../status/milestones.yaml
depends_on: [M09]
---

# M10: Azure checkpointing

## Outcome

Interrupted Azure jobs resume safely using durable VM-local results or private Blob storage with short-lived SAS access.

## Work items

- [WI-0021](../work-items/WI-0021-azure-checkpointing.md)

## Exit criteria

- Credentials never enter logs.
- Temporary cloud data can be deleted.
- The worker still has no permanent identity.
- Abrupt termination loses at most the current asset or bounded batch.
