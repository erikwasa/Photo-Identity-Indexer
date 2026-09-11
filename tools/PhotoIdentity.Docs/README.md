# PhotoIdentity.Docs

Small repository-local tool for validating and updating the living delivery documentation.

## Commands

```powershell
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
dotnet run --project tools/PhotoIdentity.Docs -- next
dotnet run --project tools/PhotoIdentity.Docs -- migrate-work-items
dotnet run --project tools/PhotoIdentity.Docs -- start WI-0005 --owner human --branch feature/WI-0005
dotnet run --project tools/PhotoIdentity.Docs -- block WI-0005 --on WI-0003 --note "Dependency needs revision"
dotnet run --project tools/PhotoIdentity.Docs -- review WI-0005
dotnet run --project tools/PhotoIdentity.Docs -- complete WI-0005 --evidence-type workflow --evidence-value URL --verified-by human
```

## Work-item status storage

The legacy layout uses `docs/delivery/status/work-items.yaml` for current work and immutable `docs/delivery/status/archive/work-items-*.yaml` files for older terminal history. `PhotoIdentity.Docs` continues to support that layout while WI-0109 is being migrated.

The sharded layout is selected when `docs/delivery/status/work-items/registry.yaml` exists:

```text
docs/delivery/status/work-items/
  registry.yaml
  active/
    WI-0109.yaml
  archive/
    WI-0057.yaml
```

`registry.yaml` contains only schema/status metadata. Every canonical work-item shard contains exactly one work item and must be named from its ID. Active shards must be non-terminal; archive shards must be `completed` or `cancelled`. The tool loads both areas as one logical registry for dependency checks, milestone calculation, validation and `next`.

Lifecycle writes in sharded mode update exactly one active shard. Completing an item moves its canonical status to the archive area. Existing archived shards are read-only to lifecycle commands.

`migrate-work-items` converts the legacy logical registry into per-item shards without deleting or rewriting the legacy source files. It is reproducible: identical partially written shards are reused, while duplicate IDs, unexpected shards, wrong-area shards or conflicting content fail explicitly. The shard metadata file is written last so an interrupted first migration does not switch normal reads to an incomplete sharded store.

Until the repository migration is accepted, do not hand-create `work-items/registry.yaml`; use the migration command so the combined logical registry can be checked before the legacy editable layout is retired.
