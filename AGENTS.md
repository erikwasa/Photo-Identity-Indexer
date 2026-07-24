# AI agent instructions

## Read before changing the repository

1. Read `BUILD_CONTEXT.md`.
2. Read the active work-item file.
3. Read only the linked ADRs and module documents needed for that item.
4. Check dependencies and status in `docs/delivery/status/work-items.yaml`.

## Architecture constraints

- Use C# and .NET by default. Use Python only for isolated model experiments, conversion or analysis where it is materially better.
- Keep the solution a modular monolith until an accepted ADR says otherwise.
- Core/domain code must not expose EF Core, OpenCV, ONNX Runtime, Azure SDK or Microsoft Graph types.
- Personal OneDrive is accessed through the Windows sync client, not Microsoft Graph.
- Azure is disposable compute. It receives portable bundles and has no OneDrive credentials, managed identity or service principal.
- Human-confirmed face labels are canonical. Embeddings, matches and clusters are replaceable derived data.
- Do not modify original photos.

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

The YAML registries are canonical.

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
- `BUILD_CONTEXT.md` reflects the next concrete step.
- Evidence is recorded in `work-items.yaml`.
