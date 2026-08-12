# PhotoIdentity.Docs

Small repository-local tool for validating and updating the living delivery documentation.

## Commands

```powershell
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
dotnet run --project tools/PhotoIdentity.Docs -- next
dotnet run --project tools/PhotoIdentity.Docs -- start WI-0005 --owner human --branch feature/WI-0005
dotnet run --project tools/PhotoIdentity.Docs -- block WI-0005 --on WI-0003 --note "Dependency needs revision"
dotnet run --project tools/PhotoIdentity.Docs -- review WI-0005
dotnet run --project tools/PhotoIdentity.Docs -- complete WI-0005 --evidence-type workflow --evidence-value URL --verified-by human
```

The YAML registries remain canonical. Mutating commands validate the repository before and after writing, update milestone statuses, and regenerate the roadmap.

`work-items.yaml` deliberately keeps completed entries because blockers, milestone calculation and completion evidence depend on that history. It is a machine/audit registry, not a human current-status document. The immediate continuation point belongs in the short root `BUILD_CONTEXT.md` handoff.
