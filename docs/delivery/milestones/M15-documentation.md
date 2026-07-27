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

- [WI-0031](../work-items/WI-0031-documentation-rewrite.md)
- [WI-0032](../work-items/WI-0032-documentation-validation.md)

## Exit criteria

- One start-here path explains installation, model setup, local processing, review, evaluation, queries and backup.
- Architecture documentation explains applications, data ownership, model revisions, review state and optional Azure scale-out.
- Commands are copyable PowerShell examples with expected outputs and failure recovery.
- A clean-setup validation proves the runbooks on Windows and the trusted-network Pixel workflow.
- Stale or duplicated guidance is removed or clearly redirected.
