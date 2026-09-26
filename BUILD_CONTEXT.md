# Build context

This file is intentionally a short handoff for the next development or verification session. Formal work-item lifecycle status is resolved through `PhotoIdentity.Docs`.

## Current focus

WI-0147 makes PostgreSQL unconditional for normal API, detector-rollout CLI and packaged launcher composition. Implementation and automated verification are complete on `codex/WI-0147`; the item is ready for maintainer review and PR creation.

SQLite remains only in the isolated API integration-test compatibility graph and explicit migration/test/tooling surfaces assigned to WI-0148 and WI-0149. Do not expand this item into that cleanup.

## Next concrete step

Create and review the WI-0147 PR. After it lands, use `PhotoIdentity.Docs show WI-0148` before starting the next M29 item.

## Relevant files

- docs/delivery/work-items/WI-0147-postgres-only-runtime-composition.md
- docs/delivery/status/work-items/active/WI-0147.yaml
- src/PhotoIdentity.Api/Program.cs
- src/PhotoIdentity.Api/CataloguePersistenceComposition.cs
- src/PhotoIdentity.Cli/DetectorRolloutCommand.cs
- Start-PhotoIdentity.ps1
- docs/architecture/postgresql-runtime-composition.md

## Verification

- Solution build passed with zero warnings/errors.
- `./test.ps1` passed all suites, including 652/652 integration tests.
- Launcher validation passed with PostgreSQL as the unconditional provider.
- Missing PostgreSQL API configuration failed with the expected explicit startup error.
