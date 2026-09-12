---
id: M10
title: Azure checkpointing
status_source: ../status/milestones.yaml
depends_on: [M09]
---

# M10: Azure checkpointing

## Historical outcome

This milestone originally proposed recoverable temporary-Azure jobs using durable VM-local results or private Blob storage with short-lived SAS access.

## Work items

- [WI-0021](../work-items/WI-0021-azure-checkpointing.md)

## Retirement — 2026-09-12

M10 is retired without implementation because [ADR-0010](../../decisions/ADR-0010-local-production-execution.md) removes Azure from the planned production execution path. The local production system has its own restart/resume, persistence, backup/restore and bounded-processing contracts, so Azure-specific checkpoint storage is no longer roadmap work.

WI-0021 is closed administratively. A future remote/cloud execution design would need to define its own recovery contract rather than assuming this historical plan.

## Historical exit criteria

- Credentials never enter logs.
- Temporary cloud data can be deleted.
- The worker still has no permanent identity.
- Abrupt termination loses at most the current asset or bounded batch.
