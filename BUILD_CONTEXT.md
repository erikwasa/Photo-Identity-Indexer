# Build context

This file is intentionally a short handoff for the next development or verification session. It should describe only the current focus, the next concrete step and the small set of documents needed to continue.

Formal work-item lifecycle status and evidence are resolved by PhotoIdentity.Docs from the current registry plus archived terminal history.

## Current focus

**M24 WI-0101 through WI-0105 and WI-0108 are completed. WI-0106 PostgreSQL operations and sustained archive catch-up is now the final substantive M24 work.**

The production launcher selects PostgreSQL as the single authoritative catalogue. `/health` was maintainer-verified with `status: ok`, `catalogueProvider: postgresql`, PostgreSQL `status: ready` and schema version 23. The preserved final SQLite backup remains a rollback/migration artifact and must not be treated as a second writable authority.

WI-0108 closed with measured evidence rather than speculative PostgreSQL/hash optimization. The bounded prefetch correction reduced one representative 11-photo phone run from 39 to 12 preview opens, 50 to 13 collection API requests and 22 to 6 original hash reads while producing 10/11 prefetch hits and no progressive latency growth.

WI-0106 is now implementing the production operations boundary. The active first slice adds a binary-safe PostgreSQL logical-backup wrapper, an isolated restore verifier that compares schema/table counts against a stopped production source, and a production operations runbook covering startup/restart, persistent volume ownership, controlled shutdown, PostgreSQL 18 updates, failure recovery, sustained archive catch-up and the final daily-style increment gate.

The restore verifier intentionally retains its isolated verification database until the maintainer has inspected the report. Production backup/restore acceptance therefore remains a live verification step after this slice merges; no script claims acceptance merely because it was added to the repository.

Consolidated real-phone M22 acceptance still has two separate functional gaps tracked by WI-0107: direct originating-gesture fullscreen launch and durable/revalidated prepared-original receipt state. Do not mix those functional corrections into M24 WI-0106.

A separate Collections / Library navigation gap remains outside this M24 thread. WI-0076 also remains separately recorded as in_progress and is not part of the M24 closeout.

## Next concrete step

For the M24 thread:

1. Merge the WI-0106 PostgreSQL operations tooling/runbook slice after green CI.
2. Stop Photo Identity, create a fresh production PostgreSQL backup and run isolated restore verification from that exact backup.
3. Review the restore JSON evidence, then remove only the isolated verification database documented by the report.
4. Verify database/container restart persistence and normal launcher recovery.
5. Resume sustained real-archive catch-up and observe `/health`, `/api/archive/status`, `/api/archive/storage` and `/api/archive/diagnostics/throughput` long enough to expose operational degradation rather than only a short smoke run.
6. After catch-up is stable, add a small real source increment and verify synchronization, analysis, enrichment and review without full regeneration.
7. Reconcile WI-0106 acceptance evidence and close M24 only after those operational exit criteria pass.

## Relevant files

- docs/delivery/work-items/WI-0106-postgresql-operations-and-archive-catchup.md
- docs/operations/postgresql-operations.md
- docs/operations/postgresql-local-runtime.md
- docs/operations/postgresql-catalogue-cutover.md
- backup-postgres-catalogue.ps1
- verify-postgres-backup-restore.ps1
- verify-postgres.ps1
- Start-PhotoIdentity.ps1
- docs/delivery/status/work-items.yaml

## Repository validation

    ./build.ps1
    ./test.ps1
    dotnet run --project tools/PhotoIdentity.Docs -- validate
    dotnet run --project tools/PhotoIdentity.Docs -- generate --check
    ./verify-review.ps1 -Mode Smoke -Configuration Release

Live PostgreSQL verification:

    ./verify-postgres.ps1
