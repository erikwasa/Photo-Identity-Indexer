# Build context

This file is intentionally a short handoff for the next development or verification session. It should describe only the current focus, the next concrete step and the small set of documents needed to continue.

Formal work-item lifecycle status and evidence are resolved by PhotoIdentity.Docs from the current registry plus archived terminal history.

## Current focus

**M24 WI-0101 through WI-0105 and WI-0108 are completed. WI-0106 PostgreSQL operations and sustained archive catch-up is now the final substantive M24 work.**

The production launcher selects PostgreSQL as the single authoritative catalogue. `/health` was maintainer-verified with `status: ok`, `catalogueProvider: postgresql`, PostgreSQL `status: ready` and schema version 23. The preserved final SQLite backup remains a rollback/migration artifact and must not be treated as a second writable authority.

WI-0108 closed with measured evidence rather than speculative PostgreSQL/hash optimization. The bounded prefetch correction reduced one representative 11-photo phone run from 39 to 12 preview opens, 50 to 13 collection API requests and 22 to 6 original hash reads while producing 10/11 prefetch hits and no progressive latency growth.

WI-0106 has successful live backup/restore evidence and accepted container/service restart persistence. The active production database was identified from server activity, backed up explicitly, restored into an isolated database with exact schema/table/count verification, and the verification database was removed after review while the verified backup was retained. The combined restart criterion remains open only for an actual Windows/PC restart.

The first PostgreSQL-backed catch-up checkpoint showed real progress: synchronization discovered 304 additional source images, analysed images advanced from 15,730 to 15,792, the post-sync unverified backlog dropped from 712 to 649, failed images remained zero, hydration stayed bounded, and aggregate diagnostics were sufficient to explain stage costs without per-photo tracing. The metrics-observability criterion is accepted.

A later longer catch-up run exposed a new operational failure. PostgreSQL connectivity was first forcibly closed and then `127.0.0.1:5432` refused connections. The archive worker caught its own failure and tried to persist recovery state, but `IdentityMatchRegenerationHostedService` let its repository connection failure escape. Because the host uses the default `BackgroundServiceExceptionBehavior=StopHost`, that one worker fault shut down the entire API. The current corrective slice adds an outer retry boundary to the identity-regeneration worker plus a regression test so a transient catalogue interruption no longer stops Photo Identity. This application-resilience correction does not explain why the PostgreSQL endpoint disappeared; inspect Podman/container state and logs separately before rerunning sustained catch-up.

The no-argument backup database resolver is not a blocker for M24 acceptance. On this maintainer installation the private launcher environment value has a wrapper shape that does not expose the database name to the generic parser reliably, so the accepted operational path uses explicit `-DatabaseName` after identifying the active authority from server activity. Do not continue connection-string unwrapping work unless it becomes a separate maintainability goal.

Consolidated real-phone M22 acceptance still has two separate functional gaps tracked by WI-0107: direct originating-gesture fullscreen launch and durable/revalidated prepared-original receipt state. Do not mix those functional corrections into M24 WI-0106.

A separate Collections / Library navigation gap remains outside this M24 thread. WI-0076 also remains separately recorded as in_progress and is not part of the M24 closeout.

## Next concrete step

For the M24 thread:

1. Inspect the PostgreSQL Compose container state/restart count and recent PostgreSQL logs around the catch-up crash to determine whether the database process restarted, Podman/WSL connectivity disappeared, or another service event occurred.
2. Merge the identity-regeneration hosted-worker resilience correction after green CI.
3. Start PostgreSQL/Photo Identity through the normal supported path, confirm `/health` is PostgreSQL-ready, and rerun sustained **Advance archive** catch-up. A short PostgreSQL interruption must no longer stop the API; the underlying service interruption still needs classification if it recurs.
4. After sustained catch-up passes, add a small real source increment and verify synchronization, analysis, enrichment and review without full regeneration.
5. Before WI-0106 closeout, perform one actual Windows/PC restart and repeat the PostgreSQL/launcher health and representative catalogue checks.
6. Reconcile the remaining WI-0106 acceptance evidence, decide when the preserved pre-cutover SQLite rollback snapshot can be retired under policy, and close M24 only after all operational exit criteria pass.

## Relevant files

- docs/delivery/work-items/WI-0106-postgresql-operations-and-archive-catchup.md
- docs/operations/postgresql-operations.md
- src/PhotoIdentity.Api/IdentityMatchRegenerationHostedService.cs
- tests/PhotoIdentity.Integration.Tests/IdentityMatchRegenerationHostedServiceResilienceTests.cs
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
