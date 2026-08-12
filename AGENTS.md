# AI agent instructions

## Read before changing the repository

1. Read `BUILD_CONTEXT.md` for the current handoff only.
2. Read the active work-item file.
3. Read only the linked ADRs and module documents needed for that item.
4. Check dependencies and formal lifecycle status in `docs/delivery/status/work-items.yaml`.

`BUILD_CONTEXT.md` should stay short and current. Do not turn it into a project history or repeat completion details that already live in work-item documents, ADRs, milestone documents or the canonical registries.

## Architecture constraints

- Use C# and .NET by default. Use Python only for isolated model experiments, conversion or analysis where it is materially better.
- Keep the solution a modular monolith until an accepted ADR says otherwise.
- Core/domain code must not expose EF Core, OpenCV, ONNX Runtime, Azure SDK or Microsoft Graph types.
- Personal OneDrive is accessed through the Windows sync client, not Microsoft Graph.
- Azure is disposable optional compute. It receives portable bundles and has no OneDrive credentials, managed identity or service principal.
- Canonical people and identity assignments are model-independent and auditable. ADR-0006 permits opt-in canonical automatic assignments with exact-model/policy provenance.
- Original photos are read-only and must not be modified.
- The permanent archive uses one stable source identity with bounded local materialization; see ADR-0007.

## Scope discipline

- Work on one work item at a time.
- Do not broaden a work item silently. Create a follow-up item for unrelated work.
- A contract change must be explicit and documented.
- Avoid unrelated refactoring.
- Keep model preprocessing beside the relevant adapter.
- Keep Azure scripts outside recognition modules.

## Privacy

Never commit personal photos, face crops, embeddings, biometric datasets, model binaries, credentials, tokens, SAS URLs, private paths or large generated logs.

## Status workflow

The YAML registries are canonical machine/audit records. `docs/delivery/status/work-items.yaml` intentionally retains completed work because dependency calculation, milestone status and completion evidence rely on that history; it is not intended to be the normal human current-status page.

Use `PhotoIdentity.Docs` instead of hand-editing status when the required command is available.

```powershell
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- next
dotnet run --project tools/PhotoIdentity.Docs -- start WI-0005 --owner ai-agent --branch agent/WI-0005
dotnet run --project tools/PhotoIdentity.Docs -- review WI-0005
```

- `proposed` → identified but not ready
- `ready` → scoped and unblocked
- `in_progress` → actively implemented
- `blocked` → cannot proceed; blockers required
- `in_review` → implementation complete; verification pending
- `completed` → acceptance criteria verified with evidence
- `cancelled` → no longer planned; reason required

Before work, mark the item `in_progress`. After implementation, add evidence and mark it `in_review`. Mark it `completed` only after required verification passes.

## Definition of done

- Relevant code builds.
- Relevant tests pass.
- Cancellation and errors are handled where applicable.
- Logging contains no sensitive data.
- Database changes include migrations.
- Idempotency has been considered.
- The affected documentation is updated.
- `BUILD_CONTEXT.md` reflects only the next concrete step and essential continuation pointers.
- Evidence is recorded in `work-items.yaml`.
- `PhotoIdentity.Docs validate` and `generate --check` pass.
