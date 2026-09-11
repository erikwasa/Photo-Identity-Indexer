# PhotoIdentity.Docs

Small repository-local tool for validating and updating the living delivery documentation.

## Commands

```powershell
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
dotnet run --project tools/PhotoIdentity.Docs -- next
dotnet run --project tools/PhotoIdentity.Docs -- show WI-0005
dotnet run --project tools/PhotoIdentity.Docs -- migrate-work-items
dotnet run --project tools/PhotoIdentity.Docs -- start WI-0005 --owner human --branch feature/WI-0005
dotnet run --project tools/PhotoIdentity.Docs -- block WI-0005 --on WI-0003 --note "Dependency needs revision"
dotnet run --project tools/PhotoIdentity.Docs -- review WI-0005
dotnet run --project tools/PhotoIdentity.Docs -- complete WI-0005 --evidence-type workflow --evidence-value URL --verified-by human
```

## Work-item status storage

Canonical work-item lifecycle status is stored one item per YAML file:

```text
docs/delivery/status/
  work-items/
    registry.yaml
    active/
      WI-0109.yaml
    archive/
      WI-0057.yaml
  work-items.yaml
  work-items-index.md
```

`registry.yaml` contains only schema/status metadata. Every canonical work-item shard contains exactly one work item and is named from its ID. Active shards must be non-terminal; archive shards must be `completed` or `cancelled`. The tool loads both areas as one logical registry for dependency checks, milestone calculation, validation, `next` and `show`.

`work-items.yaml` is a generated compatibility view containing only current non-terminal work. `work-items-index.md` is a generated compact human overview. Neither file is canonical or hand-edited. `generate --check` fails when these views drift from the canonical shards.

Lifecycle writes update exactly one active shard. Completing or cancelling an item moves its canonical status to the archive area. Existing archived shards are read-only to lifecycle commands. `show WI-XXXX` locates a current or historical item without requiring callers to know which area contains it.

## Migration command

`migrate-work-items` is the one-time legacy-layout converter retained for reproducibility. Before cutover it converts the old logical registry into per-item shards without rewriting the legacy source files, reuses identical partial shards, rejects duplicate/conflicting/unexpected shards and writes `registry.yaml` last so an interrupted migration cannot activate an incomplete store.

After `registry.yaml` exists, the repository is already on canonical shards; rerunning the migration command must leave that canonical state unchanged. New work should be created and transitioned through the sharded model rather than by recreating legacy batch registries.
