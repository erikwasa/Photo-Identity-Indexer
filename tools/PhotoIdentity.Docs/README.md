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

## Work-item registries

`docs/delivery/status/work-items.yaml` contains current work and stays intentionally small. Historical terminal entries live under `docs/delivery/status/archive/work-items-*.yaml`.

The tool reads those files as one combined view for dependency checks, milestone calculation, validation and `next`. Only `completed` and `cancelled` rows are taken from archive files. The legacy migration snapshot also contains older non-terminal snapshots, which are ignored so the current registry remains authoritative.

Status writes keep the archive files unchanged and keep the active-file boundary intact.

If the current registry grows materially again, use another bounded migration rather than returning to one unbounded file. The immediate continuation point belongs in the short root `BUILD_CONTEXT.md` handoff.
