---
id: M15
title: Operator documentation and system guide
status_source: ../status/milestones.yaml
depends_on: [M08, M14]
---

# M15: Operator documentation and system guide

## Outcome

A new operator can understand the system, run the complete local workflow, compare models, recover from common failures and verify results without reconstructing knowledge from pull requests.

## Work items

- [WI-0031](../work-items/WI-0031-documentation-rewrite.md) — completed
- [WI-0032](../work-items/WI-0032-documentation-validation.md) — independent clean-setup validation in progress

## Current gate

The documentation rewrite, architecture reconciliation, glossary, PowerShell cleanup and automated documentation validation are complete. M15 now depends only on a human maintainer following the documented workflow from a clean Windows checkout and validating the trusted-network Pixel path without relying on project memory.

Any confusing instruction, hidden prerequisite or failed command discovered during WI-0032 must be corrected and merged before the milestone completes. Azure access is not required; Azure remains explicitly optional and deferred.

## Exit criteria

- One start-here path explains installation, model setup, local processing, review, evaluation, queries and backup.
- Architecture documentation explains applications, data ownership, model revisions, review state and optional Azure scale-out.
- Commands are copyable PowerShell examples with expected outputs and failure recovery.
- A clean-setup validation proves the runbooks on Windows and the trusted-network Pixel workflow.
- Stale or duplicated guidance is removed or clearly redirected.
