---
id: WI-0020
title: Run Azure VM pilot
milestone: M09
status_source: ../status/work-items.yaml
depends_on: [WI-0018, WI-0032]
affected_modules: [PhotoIdentity.Transfer.Bundles, PhotoIdentity.Worker, docs/azure]
---

# WI-0020: Run Azure VM pilot

## Historical objective

The original plan was to run a small identity-free processing bundle on a temporary Azure VM and compare it with the proven local path.

## Retirement — 2026-09-12

This work item is retired without implementation. [ADR-0010](../../decisions/ADR-0010-local-production-execution.md) establishes maintainer-controlled local hardware as the production execution strategy. The accepted local archive workflow and M24 archive-scale operation remove the need for an Azure pilot.

The canonical status closes this item administratively; it does not assert that the historical Azure acceptance checks below were performed.

## Historical acceptance criteria

- Azure access is confirmed before any resource is created.
- The VM receives only explicit job bundles and pinned model files.
- No OneDrive credential, canonical database, person record or human label enters Azure.
- Result hashes and model provenance match the job contract.
- Local and Azure outputs agree within documented numerical tolerance.
- Actual runtime and cost are recorded and the VM is deallocated.
