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

- [x] Detects duplicate or missing IDs and cyclic dependencies.
- [x] Rejects completed work without evidence and blocked work without blockers.
- [x] Generates roadmap and current-status views.
- [x] Supports start, block, review and complete operations safely.
- [x] CI rejects invalid registries or stale generated views.

## Commands

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

## Verification

Pull request [#4](https://github.com/erikwasa/Photo-Identity-Indexer/pull/4) was merged as commit `46b4a549cd8ad23def2a321c0cdc55b1bb7611da`.

GitHub Actions run [30132402177](https://github.com/erikwasa/Photo-Identity-Indexer/actions/runs/30132402177) successfully restored, built, tested, validated documentation links and registries, and verified generated files on Windows with .NET 10.
