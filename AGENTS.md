# AI agent instructions

## Read before changing the repository

1. Read `BUILD_CONTEXT.md` for the current handoff only.
2. Use `PhotoIdentity.Docs show WI-XXXX` to locate the item's canonical status shard, then read that shard and the linked work-item document.
3. Read only the linked ADRs and module documents needed for that item.
4. Use `PhotoIdentity.Docs` for dependency and formal lifecycle status. Do not load archived work-item shards routinely.

`BUILD_CONTEXT.md` should stay short and current. Do not turn it into a project history or repeat completion details that already live in work-item documents, ADRs, milestone documents or canonical status shards.

## Architecture constraints

- Use C# and .NET by default. Use Python only for isolated model experiments, conversion or analysis where it is materially better.
- Keep the solution a modular monolith until an accepted ADR says otherwise.
- Core/domain code must not expose EF Core, OpenCV, ONNX Runtime, Azure SDK or Microsoft Graph types.
- Personal OneDrive is accessed through the Windows sync client, not Microsoft Graph.
- Production model execution and archive processing run on maintainer-controlled local hardware; ADR-0010 supersedes the earlier disposable-Azure strategy.
- PostgreSQL is the sole writable production catalogue. SQLite support is compatibility/migration/rollback tooling unless a future ADR changes that authority boundary.
- Canonical people and identity assignments are model-independent and auditable. ADR-0006 permits opt-in canonical automatic assignments with exact-model/policy provenance.
- Original photos are read-only and must not be modified.
- The permanent archive uses one stable source identity with bounded local materialization; see ADR-0007.

## Scope discipline

- Work on one work item at a time.
- Do not broaden a work item silently. Create a follow-up item for unrelated work.
- A contract change must be explicit and documented.
- Avoid unrelated refactoring.
- Keep model preprocessing beside the relevant adapter.
- Do not revive Azure/cloud execution work from historical documents; a future remote-compute path requires a new ADR and newly scoped work.

## Privacy

Never commit personal photos, face crops, embeddings, biometric datasets, model binaries, credentials, tokens, private paths or large generated logs.

## Status workflow

Canonical machine/audit status is stored one work item per YAML file under `docs/delivery/status/work-items/`. Non-terminal items live in `active/WI-XXXX.yaml`; terminal items live in `archive/WI-XXXX.yaml`. `registry.yaml` contains only schema/status metadata. `PhotoIdentity.Docs` combines active and archived shards for validation, blockers, milestone status and work selection.

`docs/delivery/status/work-items.yaml` and `docs/delivery/status/work-items-index.md` are deterministic generated current-work/discovery views. They are not editable sources of truth. Lifecycle commands update only the target canonical shard; completing an item moves that shard to the archive automatically.

Do not load archive files during normal handoff work. Use `show` when a specific historical item is needed, and use lifecycle commands rather than hand-editing status when the required command is available.

```powershell
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- next
dotnet run --project tools/PhotoIdentity.Docs -- show WI-0005
dotnet run --project tools/PhotoIdentity.Docs -- start WI-0005 --owner ai-agent --branch agent/WI-0005
dotnet run --project tools/PhotoIdentity.Docs -- review WI-0005
```

- `proposed` → identified but not ready
- `ready` → scoped and unblocked
- `in_progress` → actively implemented
- `blocked` → cannot proceed; blockers required
- `in_review` → implementation complete; verification pending
- `completed` → acceptance criteria verified or an explicit administrative closeout is documented with evidence
- `cancelled` → no longer planned when a supported lifecycle path records the reason

Before work, mark the item `in_progress`. After implementation, add evidence and mark it `in_review`. Mark it `completed` only after required verification passes or an explicit retirement/supersession decision is documented without claiming unperformed acceptance work.

## Testing and pull-request validation

- Put behavior at the lowest practical test layer. Use full HTTP-host integration tests for cross-layer wiring and contracts that cannot be established more cheaply.
- Generic API integration tests should reuse the shared test host and keep unrelated production background workers disabled. Worker-specific behavior should opt in explicitly or exercise the worker directly.
- The host-heavy integration assembly remains sequential in-process. Do not re-enable broad xUnit parallelism without evidence that host isolation has changed enough to make it safe.
- Do not normalize flaky tests with unconditional retries. Temporary quarantine must stay visible, have a tracked stabilization follow-up and an explicit condition for returning to the required gate.
- When adding host-heavy tests or required CI checks, state why the added signal justifies the runtime cost and include timing evidence when the cost is material.
- PR descriptions should state the test layer added or changed, whether the required CI gate changed, and any material timing or coverage tradeoff.
- Keep detailed rationale in `docs/operations/testing-and-ci-strategy.md`; keep this file concise and action-oriented.

## Definition of done

- Relevant code builds.
- Relevant tests pass.
- Cancellation and errors are handled where applicable.
- Logging contains no sensitive data.
- Database changes include migrations.
- Idempotency has been considered.
- The affected documentation is updated.
- `BUILD_CONTEXT.md` reflects only the next concrete step and essential continuation pointers.
- Evidence is recorded through the work-item registry workflow.
- `PhotoIdentity.Docs validate` and `generate --check` pass.
