---
id: WI-0004
title: Add documentation status tooling
milestone: M00
status_source: ../status/work-items.yaml
depends_on: [WI-0002, WI-0003]
affected_modules: [tools/PhotoIdentity.Docs]
---

# WI-0004: Add documentation status tooling

## Objective

Create a small .NET tool to validate registries and links, generate `current.md`, calculate milestone status and select ready work items.

## Acceptance criteria

- [ ] Detects duplicate or missing IDs and cyclic dependencies.
- [ ] Rejects completed work without evidence and blocked work without blockers.
- [ ] Generates roadmap and current-status views.
- [ ] Supports start, block, review and complete operations safely.
