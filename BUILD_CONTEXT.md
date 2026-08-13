# Build context

This file is intentionally a short handoff for the next development or verification session. It should describe only the current focus, the next concrete step and the small set of documents needed to continue.

Formal work-item lifecycle status and evidence are resolved by `PhotoIdentity.Docs` from the current registry plus archived terminal history.

## Current focus

**WI-0057 — Split active and archived work-item registries** is the current repository-tooling focus.

The migration preserves the former monolithic registry unchanged under `docs/delivery/status/archive/` and reduces `docs/delivery/status/work-items.yaml` to current work. The documentation tool combines current entries with terminal archive history for dependency and milestone calculations.

WI-0056 remains on its separate maintainer-verification path; this branch does not change its product implementation.

## Next concrete step

Run the repository build, test and documentation checks for WI-0057. Confirm archived completed blockers remain usable and the small current registry remains the normal update surface.

## Relevant files

- `docs/delivery/work-items/WI-0057-work-item-registry-archive.md`
- `docs/delivery/status/work-items.yaml`
- `docs/delivery/status/archive/work-items-legacy-through-0056.yaml`
- `tools/PhotoIdentity.Docs/RepositoryPaths.cs`
- `tools/PhotoIdentity.Docs/RegistryStore.cs`
- `tools/PhotoIdentity.Docs/Program.cs`
- `tests/PhotoIdentity.Docs.Tests/RegistryStoreTests.cs`
- `AGENTS.md`

## Repository validation

```powershell
./build.ps1
./test.ps1
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
```
