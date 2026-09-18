---
id: M29
title: PostgreSQL-only catalogue cleanup
status_source: ../status/milestones.yaml
depends_on: [M24]
---

# M29: PostgreSQL-only catalogue cleanup

## Outcome

Photo Identity has one active catalogue implementation: PostgreSQL. SQLite is no longer selectable at runtime, required by active tests/tools, compiled as a persistence implementation, or described as current in active documentation. Historical delivery records remain intact.

## Work items

- [WI-0147](../work-items/WI-0147-postgres-only-runtime-composition.md) - make PostgreSQL unconditional at runtime.
- [WI-0148](../work-items/WI-0148-retire-sqlite-test-tool-dependencies.md) - retire SQLite-dependent active tests/tools.
- [WI-0149](../work-items/WI-0149-remove-sqlite-implementation.md) - remove the SQLite project and obsolete migration-era active references.

## Exit criteria

- [ ] API/CLI/package startup has no supported SQLite catalogue mode.
- [ ] Active tests/verification protect PostgreSQL/provider-neutral behavior without a second persistence implementation.
- [ ] The solution no longer includes PhotoIdentity.Persistence.Sqlite.
- [ ] Active docs consistently describe PostgreSQL as the catalogue.
- [ ] Historical records are preserved rather than rewritten.
