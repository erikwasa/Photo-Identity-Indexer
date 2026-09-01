# Build context

This file is intentionally a short handoff for the next development or verification session. It should describe only the current focus, the next concrete step and the small set of documents needed to continue.

Formal work-item lifecycle status and evidence are resolved by PhotoIdentity.Docs from the current registry plus archived terminal history.

## Current focus

**M24 — PostgreSQL catalogue migration and archive-scale operation** is planned and ready to start with WI-0097.

The maintainer selected PostgreSQL as the long-term authoritative catalogue after real-catalogue match regeneration produced severe SQLite contention and `ArchiveAdvancementHostedService` ultimately terminated the host on an unhandled `SQLite Error 6: database table is locked`.

The implementation goal is progress toward completing the entire existing archive and then operating on smaller daily updates. Comparative SQLite/PostgreSQL benchmark exercises are explicitly not required. Low-overhead application metrics are acceptable and planned.

The primary local deployment target is PostgreSQL in a Podman-compatible container on the maintainer's existing WSL2/Podman Desktop environment.

M22 remains a separate maintainer-verification stream and must not be folded into M24 acceptance. M23 remains ready but is not a prerequisite for the PostgreSQL migration.

## Next concrete step

1. Review and merge the M24 planning PR.
2. Start **WI-0097 — Establish PostgreSQL runtime and migration foundation**.
3. Keep the existing SQLite catalogue authoritative and untouched until WI-0102 performs the verified migration/cutover.
4. Implement migration slices in dependency order; do not introduce new SQLite-only authoritative persistence.

## Relevant files

- docs/decisions/ADR-0009-postgresql-authoritative-catalogue.md
- docs/delivery/milestones/M24-postgresql-catalogue-and-scale.md
- docs/delivery/work-items/WI-0097-postgresql-runtime-foundation.md
- docs/delivery/work-items/WI-0098-persistence-boundary-foundational-schema.md
- docs/delivery/work-items/WI-0099-postgresql-archive-background-persistence.md
- docs/delivery/work-items/WI-0100-postgresql-review-identity-persistence.md
- docs/delivery/work-items/WI-0101-postgresql-library-remaining-persistence.md
- docs/delivery/work-items/WI-0102-sqlite-postgresql-catalogue-migration.md
- docs/delivery/work-items/WI-0103-scalable-match-regeneration.md
- docs/delivery/work-items/WI-0104-operator-query-ui-performance.md
- docs/delivery/work-items/WI-0105-operational-metrics-observability.md
- docs/delivery/work-items/WI-0106-postgresql-operations-and-archive-catchup.md
- docs/delivery/status/work-items.yaml
- docs/delivery/status/milestones.yaml

## Repository validation

    ./build.ps1
    ./test.ps1
    dotnet run --project tools/PhotoIdentity.Docs -- validate
    dotnet run --project tools/PhotoIdentity.Docs -- generate --check
    ./verify-review.ps1 -Mode Smoke -Configuration Release
