# Build context

This file is intentionally a short handoff for the next development or verification session. Formal work-item lifecycle status is resolved through `PhotoIdentity.Docs`.

## Current focus

**M24 substantive implementation and acceptance are complete. WI-0106 now needs formal lifecycle reconciliation and milestone closeout.**

PostgreSQL is the accepted single writable production catalogue. WI-0106 has passed backup/restore verification, container restart persistence, sustained full-archive catch-up, a seven-photo daily-style increment with analysis/enrichment/Review visibility, and an actual Windows restart.

After the real PC restart, the existing Podman machine and retained PostgreSQL volume were reused. `verify-postgres.ps1` started the Compose service, passed PostgreSQL protocol checks, built Release successfully, passed all 29 persistence tests and all 7 runtime/composition integration tests. The normal launcher selected PostgreSQL, `/health` returned PostgreSQL `ready` at schema 23, archive totals remained `16,449 / 16,449` current/analysed with zero pending, failed or unverified images, and representative Smart Collection, Review and Archive state remained intact.

The preserved pre-cutover SQLite snapshot has completed its active M24 rollback/stabilization role. It is not modified by this closeout and may remain as an offline historical migration artifact.

Separate M22 functional gaps remain under WI-0107. WI-0076 also remains outside this M24 closeout.

## Next concrete step

1. Use `PhotoIdentity.Docs` on the closeout branch to transition WI-0106 through `start`, `review` and `complete` with human verification evidence. Do not hand-edit generated work-item views.
2. Run `PhotoIdentity.Docs generate`, `validate` and `generate --check`; completion should move WI-0106 from the active shard directory to the archive and refresh deterministic generated views.
3. Mark M24 completed in `docs/delivery/status/milestones.yaml` after WI-0106 is terminal and record milestone closeout evidence.
4. Merge the closeout PR after CI and documentation checks are green.

## Relevant files

- docs/delivery/work-items/WI-0106-postgresql-operations-and-archive-catchup.md
- docs/delivery/milestones/M24-postgresql-catalogue-and-scale.md
- docs/delivery/status/work-items/active/WI-0106.yaml
- docs/delivery/status/milestones.yaml
- tools/PhotoIdentity.Docs/README.md
- docs/operations/postgresql-operations.md

## Repository validation

    ./build.ps1
    ./test.ps1
    dotnet run --project tools/PhotoIdentity.Docs -- validate
    dotnet run --project tools/PhotoIdentity.Docs -- generate --check
    ./verify-review.ps1 -Mode Smoke -Configuration Release

Live PostgreSQL verification:

    ./verify-postgres.ps1
