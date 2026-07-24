---
id: WI-0021
title: Add Azure checkpointing
milestone: M10
status_source: ../status/work-items.yaml
depends_on: [WI-0020]
affected_modules: [infra/azure, PhotoIdentity.Worker]
---

# WI-0021: Add Azure checkpointing

## Objective

Make interrupted Azure jobs recoverable using durable VM-local result retrieval or private Blob storage with narrowly scoped short-lived SAS access.

## Acceptance criteria

- [ ] Abrupt termination loses only bounded work.
- [ ] Credentials never appear in logs or result bundles.
- [ ] Temporary cloud data can be deleted safely.
- [ ] The worker still has no permanent cloud identity.
