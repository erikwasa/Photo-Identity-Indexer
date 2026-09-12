---
id: M09
title: Azure VM pilot without identities
status_source: ../status/milestones.yaml
depends_on: [M07, M08, M15]
---

# M09: Azure VM pilot without identities

## Historical outcome

This milestone originally proposed proving that a small identity-free bundle could run on temporary Azure compute using the same worker and portable transfer contract.

## Work items

- [WI-0020](../work-items/WI-0020-azure-pilot.md)

## Retirement — 2026-09-12

M09 is retired without running the Azure pilot. [ADR-0010](../../decisions/ADR-0010-local-production-execution.md) establishes maintainer-controlled local hardware as the current production execution strategy. The local archive workflow, PostgreSQL production catalogue and archive-scale catch-up have been accepted without requiring cloud compute.

WI-0020 is therefore closed administratively rather than treated as implemented. A future cloud-compute experiment would require a new decision and newly scoped work.

## Historical exit criteria

The following criteria describe the retired pilot and were not executed as current product acceptance:

- Azure access is available to the maintainer before execution begins.
- No app registration, service principal or managed identity is created.
- No OneDrive credential or canonical identity data enters Azure.
- Results match local execution within tolerance.
- Actual cost is recorded and the VM is deallocated.
