---
id: WI-0021
title: Add Azure checkpointing
milestone: M10
status_source: ../status/work-items.yaml
depends_on: [WI-0020]
affected_modules: [infra/azure, PhotoIdentity.Worker]
---

# WI-0021: Add Azure checkpointing

## Historical objective

The original plan was to make temporary Azure jobs recoverable using durable VM-local result retrieval or private Blob storage with narrowly scoped short-lived SAS access.

## Retirement — 2026-09-12

This work item is retired without implementation. ADR-0010 removes Azure from the planned production execution path, so Azure-specific checkpoint storage is no longer required. Local restart/resume, PostgreSQL persistence and backup/restore are governed by the current local architecture instead.

The canonical status is an administrative closeout and does not claim the historical Azure acceptance criteria were executed.

## Historical acceptance criteria

- Abrupt termination loses only bounded work.
- Credentials never appear in logs or result bundles.
- Temporary cloud data can be deleted safely.
- The worker still has no permanent cloud identity.
