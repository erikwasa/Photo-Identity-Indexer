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

The YAML registries remain canonical. Mutating commands validate the repository before and after writing, update milestone statuses, and regenerate the human-readable views.
