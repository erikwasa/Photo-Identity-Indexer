# Build context

## Current milestone

**M00 — Repository and architecture**

## Current work item

**WI-0004 — Add documentation status tooling**

Status: `in_review`

## Branch and pull request

- Branch: `agent/WI-0004-docs-tooling`
- Pull request: [#4 — Add documentation status tooling](https://github.com/erikwasa/Photo-Identity-Indexer/pull/4)

## Objective

Create a small .NET tool that validates the living-document registries and links, calculates milestone status, generates human-readable status pages, selects ready work, and performs safe status transitions.

## Relevant files

- `tools/PhotoIdentity.Docs/`
- `tests/PhotoIdentity.Docs.Tests/`
- `docs/delivery/status/work-items.yaml`
- `docs/delivery/status/milestones.yaml`
- `docs/delivery/status/current.md`
- `docs/delivery/roadmap.md`
- `docs/delivery/work-items/WI-0004-docs-tooling.md`

## Commands

```powershell
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
dotnet run --project tools/PhotoIdentity.Docs -- next
dotnet test tests/PhotoIdentity.Docs.Tests/PhotoIdentity.Docs.Tests.csproj
```

## Acceptance test

- Duplicate IDs, missing IDs, missing references and dependency cycles are reported.
- Completed items without evidence and blocked items without blockers are rejected.
- `current.md`, `roadmap.md` and milestone status are generated deterministically.
- Start, block, review and complete transitions enforce their preconditions.
- CI validates the registries and checks generated files.

## Verification

GitHub Actions run `30132244049` passed restore, build, tests, registry and link validation, and generated-file checks on Windows with .NET 10.

## Known issues

- The current agent container has no .NET SDK; GitHub Actions performs executable verification.
- Registry mutations rewrite YAML using YamlDotNet formatting.

## Next action

Review and merge pull request #4, mark WI-0004 completed with merge evidence, then begin WI-0005.
