# Build context

This file is intentionally a short handoff for the next development or verification session. It should describe only the current focus, the next concrete step and the small set of documents needed to continue.

Formal work-item lifecycle status and evidence are resolved by PhotoIdentity.Docs from the current registry plus archived terminal history.

## Current focus

**M24 WI-0101 through WI-0105 and WI-0108 are completed. WI-0106 PostgreSQL operations is now in final acceptance.**

The production launcher selects PostgreSQL as the single authoritative catalogue. `/health` is maintainer-verified with `status: ok`, `catalogueProvider: postgresql`, PostgreSQL `status: ready` and schema version 23. The preserved final SQLite backup remains a rollback/migration artifact and must not be treated as a second writable authority.

WI-0106 has successful live backup/restore evidence and accepted container/service restart persistence. The active production database was identified from server activity, backed up explicitly, restored into an isolated database with exact schema/table/count verification, and the verification database was removed after review while the verified backup was retained. The combined restart criterion remains open only for an actual Windows/PC restart.

The first PostgreSQL catch-up pass proved forward progress and aggregate diagnostic usefulness. A later run exposed a Podman/WSL runtime stop plus a missing resilience boundary in `IdentityMatchRegenerationHostedService`; PR #309 corrected the worker so transient catalogue failures retry instead of stopping the entire host. Host diagnostics showed the Podman machine had stopped while PostgreSQL itself had exited cleanly with no OOM/restart/crash evidence.

After #309 merged, the maintainer recovered the existing Podman machine and Compose service, reran `verify-postgres.ps1`, started Photo Identity normally, and completed a fresh sustained catch-up run. From a reset baseline of 452 unverified images (`15,990 / 16,442` analysed), the approximately 57-minute run reached `16,442 / 16,442` analysed with `unverifiedSourceImages=0`, `pendingImages=0`, `failedImages=0` and PostgreSQL continuously `ready`. Diagnostics recorded exactly 452 analysis attempts, bounded hash reads and low single-digit-millisecond result persistence. Hydration returned to zero in progress. The sustained full-archive catch-up criterion is accepted.

The no-argument backup database resolver is not a blocker for M24 acceptance. On this maintainer installation the private launcher environment value has a wrapper shape that does not expose the database name to the generic parser reliably, so the accepted operational path uses explicit `-DatabaseName` after identifying the active authority from server activity. Do not continue connection-string unwrapping work unless it becomes a separate maintainability goal.

Consolidated real-phone M22 acceptance still has two separate functional gaps tracked by WI-0107: direct originating-gesture fullscreen launch and durable/revalidated prepared-original receipt state. Do not mix those functional corrections into M24 WI-0106.

A separate Collections / Library navigation gap remains outside this M24 thread. WI-0076 also remains separately recorded as in_progress and is not part of the M24 closeout.

## Next concrete step

For the M24 thread:

1. Add a **small real daily-style source increment** (for example a few newly synced phone/OneDrive photos) without regenerating the full archive.
2. Run the normal synchronization/Advance archive path and verify the new items are discovered, analysed, enriched where applicable, and available for review while the already-complete archive is not unnecessarily reprocessed.
3. Capture `/health`, `/api/archive/status`, `/api/archive/storage` and `/api/archive/diagnostics/throughput` before/after the increment as acceptance evidence.
4. Before WI-0106 closeout, perform one actual Windows/PC restart and repeat PostgreSQL/launcher health plus representative catalogue checks to finish the combined restart criterion.
5. Reconcile the remaining WI-0106 acceptance evidence, decide when the preserved pre-cutover SQLite rollback snapshot can be retired under policy, and close M24 only after all operational exit criteria pass.

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
