---
id: WI-0109
title: Migrate work-item status to bounded canonical shards
milestone: M00
status_source: PhotoIdentity.Docs
depends_on: [WI-0057]
affected_modules: [tools/PhotoIdentity.Docs, PhotoIdentity.Docs.Tests, delivery-status]
---

# WI-0109: Migrate work-item status to bounded canonical shards

## Objective

Replace the indefinitely growing work-item status registries with small canonical work-item files while preserving one logical, validated living-documentation system for lifecycle commands, dependency resolution, milestone status, audit history and human navigation.

The existing WI-0057 active/archive split established the correct logical boundary, but completed items remain in the writable `docs/delivery/status/work-items.yaml` unless a later manual migration moves them. The writable registry has therefore grown large again, and the legacy archive is itself a large batch file. This work item makes bounded partitioning and terminal archival part of the documentation model instead of an occasional cleanup.

## Target model

Canonical work-item status is stored one item per YAML file under a sharded status directory, for example:

```text
docs/delivery/status/
  milestones.yaml
  work-items/
    registry.yaml
    active/
      WI-0106.yaml
      WI-0107.yaml
      WI-0108.yaml
    archive/
      WI-0057.yaml
      WI-0105.yaml
  work-items.yaml            # generated compatibility/current-work view
  work-items-index.md        # generated compact human index
```

The invariants are that one work item is the canonical unit of storage, active and terminal history are physically separated, aggregate views are generated rather than hand-maintained, and agents do not need to load project history to update one item.

The narrative work-item documents under `docs/delivery/work-items/` remain separate from lifecycle/status YAML in this migration.

## Contract

- `PhotoIdentity.Docs` must load active and archived shards into the same logical `WorkItemRegistry` view used by validation, blocker resolution, milestone status and work selection.
- A canonical work-item status shard contains exactly one work item and has a deterministic path derived from its work-item ID.
- Active shards contain only non-terminal statuses. Archived shards contain only `completed` or `cancelled` items.
- Lifecycle commands update only the target item's canonical shard rather than rewriting a project-wide registry.
- Transitioning an item to `completed` or `cancelled` moves its canonical status from the active area to the archive as part of the lifecycle operation.
- Archived terminal status remains read-only during normal lifecycle commands.
- Work-item IDs remain globally unique across active and archived shards.
- Dependencies and milestone membership resolve across the combined logical view without requiring callers or agents to know which shard contains an item.
- Existing status fields and evidence must survive migration without semantic loss or silent normalization that changes audit history.
- Generated aggregate/index output must be deterministic and must never become a second editable source of truth.
- Normal agent guidance must point to the specific active work-item document/status shard and `PhotoIdentity.Docs`; it must not instruct agents to load all archived shards or a full historical aggregate.

## Migration

- Introduce shard-aware repository paths and registry loading while the existing registries are still readable.
- Add a deterministic migration path from the current `work-items.yaml` plus legacy `archive/work-items-*.yaml` files into canonical per-item shards.
- Detect duplicate IDs and conflicting copies during migration rather than silently choosing one.
- Preserve every supported work-item field, including lifecycle dates, owner/branch metadata, blocker notes and evidence.
- Verify the migrated combined logical registry is equivalent to the pre-migration logical registry before removing the old editable layout.
- Retain a generated current-work compatibility view only where it materially reduces migration risk; clearly mark it generated and remove any code path that treats it as canonical.
- Keep the migration reproducible/idempotent so a partially prepared branch can be checked safely without duplicating work items.

## Implementation progress

- PR #299 added shard-aware repository discovery, logical active/archive loading, one-shard lifecycle persistence, terminal movement, archive immutability and the deterministic legacy migration command.
- PR #300 executes the repository cutover. The migration command was run in CI against the exact PR state, transitioned WI-0109 to `in_progress`, generated 30 active and 78 archived canonical shards, and passed `PhotoIdentity.Docs validate` plus `generate --check` before the generated tree was committed.
- The old batch archive is removed. `work-items.yaml` is now a generated non-terminal compatibility view and `work-items-index.md` is the compact human discovery view.
- `PhotoIdentity.Docs show WI-XXXX` locates both active and historical canonical status without loading archive history manually.
- Agent/tooling guidance now treats per-item shards as canonical and generated aggregate/index files as read-only views.

## Acceptance criteria

- [x] Canonical work-item lifecycle/status data is stored as one work item per YAML file rather than in an indefinitely growing editable registry.
- [x] Active and archived canonical shards are physically separated and enforce non-terminal versus terminal status semantics.
- [x] `PhotoIdentity.Docs validate`, `next`, `start`, `review` and the other existing lifecycle transitions operate on the combined sharded model with unchanged user-facing lifecycle semantics.
- [x] A normal lifecycle update rewrites only the affected work-item shard, apart from deterministic generated outputs that genuinely need refresh.
- [x] Completing or cancelling a work item automatically moves it from active canonical storage to archived canonical storage.
- [x] Archived items remain available for blocker/dependency resolution, milestone calculation and audit inspection without being loaded routinely by agents.
- [x] The current registry and legacy archive are migrated losslessly; IDs, statuses, metadata, blocker notes and evidence are preserved and conflicting duplicates fail explicitly.
- [x] Any retained `work-items.yaml` aggregate is generated, bounded to current-work/compatibility needs, and documented as non-canonical.
- [x] A compact generated human overview or equivalent `PhotoIdentity.Docs` command makes it easy to discover current work and locate historical items without opening every shard.
- [x] Repository/agent guidance describes the new canonical layout and tells agents to read only the relevant work-item status shard plus linked documentation.
- [x] Tests cover shard discovery, duplicate detection, active/archive status constraints, archived dependency resolution, single-item persistence, terminal movement, read-only archive behavior and lossless migration from the existing layout.
- [ ] `PhotoIdentity.Docs validate` and `generate --check` pass after the final committed cutover state, and CI detects drift in generated aggregate/index output.

## Implementation notes

Prefer adapting the existing `WorkItem`, `WorkItemRegistry`, validation and lifecycle abstractions rather than making every command understand filesystem partitioning independently. The storage layer should own discovery, source-location tracking, single-item persistence and terminal movement; higher-level services should continue to work against the logical registry where practical.

Generated views should be deliberately compact. The purpose of this migration is defeated if a new routinely loaded generated file reproduces the full historical evidence payload that was removed from the canonical editing path.

## Non-goals

- Splitting or refactoring large application/source-code files.
- Converting narrative Markdown work-item documents into the canonical lifecycle/status store.
- Sharding ADRs, architecture documents, operational guides or milestones merely because they exist in the documentation tree.
- Changing work-item IDs, lifecycle statuses, milestone semantics or historical evidence content.
- Product/runtime behavior unrelated to documentation tooling.
