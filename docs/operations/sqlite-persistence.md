# Historical SQLite persistence operations (retired)

> **Historical record only.** SQLite is no longer a supported Photo Identity catalogue, runtime provider, test dependency, backup target or executable migration path. WI-0149 removed the SQLite persistence implementation and active compatibility commands after PostgreSQL became the sole supported catalogue.

This path is retained so completed work items and migration records can continue to link to the persistence policy that applied when SQLite was the canonical catalogue. Do not use it as current operator guidance.

## Historical boundary

Before the PostgreSQL cutover, the local SQLite catalogue contained canonical source/revision identity, people, human review history, processing state and model-derived metadata. It was treated as sensitive application data rather than a disposable cache. Historical procedures therefore required stopped-writer backups, preservation of database sidecars where applicable, restore verification, and explicit protection of biometric/review data.

WI-0102 records the accepted SQLite-to-PostgreSQL migration, representative verification, production authority switch and rollback rehearsal. The preserved stopped-source SQLite backup referenced by that work item is historical migration evidence; it is not a catalogue that current Photo Identity binaries can open.

## Current replacement

For supported operation use:

- [PostgreSQL production operations](postgresql-operations.md) for backup, restore, restart and upgrade procedures;
- [PostgreSQL local runtime](postgresql-local-runtime.md) for the local Podman/WSL service and connectivity checks; and
- [Local operator guide](local-operator-guide.md) for the normal application/archive workflow.

Historical delivery and ADR records intentionally continue to mention SQLite where that accurately describes what was implemented or verified at the time.
