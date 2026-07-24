---
id: WI-0004
title: Add documentation status tooling
milestone: M00
status_source: ../status/work-items.yaml
depends_on: [WI-0002, WI-0003]
affected_modules: [tools/PhotoIdentity.Docs, PhotoIdentity.Docs.Tests]
---

# WI-0004: Add documentation status tooling

## Objective

Create a small .NET tool to validate registries and links, generate `current.md`, calculate milestone status and select ready work items.

## Acceptance criteria

- [ ] Detects duplicate or missing IDs and cyclic dependencies.
- [ ] Rejects completed work without evidence and blocked work without blockers.
- [ ] Generates roadmap and current-status views.
- [ ] Supports start, block, review and complete operations safely.
- [ ] CI rejects invalid registries or stale generated views.

## Planned commands

```powershell
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
dotnet run --project tools/PhotoIdentity.Docs -- next
dotnet run --project tools/PhotoIdentity.Docs -- start WI-0005 --owner human --branch feature/WI-0005
dotnet run --project tools/PhotoIdentity.Docs -- block WI-0005 --on WI-0003 --note "Reason"
dotnet run --project tools/PhotoIdentity.Docs -- review WI-0005
dotnet run --project tools/PhotoIdentity.Docs -- complete WI-0005 --evidence-type workflow --evidence-value URL --verified-by human
```
