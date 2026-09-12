# Build context

This file is intentionally a short handoff for the next development or verification session. It should describe only the current focus, the next concrete step and the small set of documents needed to continue.

Formal work-item lifecycle status and evidence are resolved by PhotoIdentity.Docs from the current registry plus archived terminal history.

## Current focus

**M24 WI-0101 through WI-0105 and WI-0108 are completed. WI-0106 PostgreSQL operations and sustained archive catch-up is now the final substantive M24 work.**

The production launcher selects PostgreSQL as the single authoritative catalogue. `/health` was maintainer-verified with `status: ok`, `catalogueProvider: postgresql`, PostgreSQL `status: ready` and schema version 23. The preserved final SQLite backup remains a rollback/migration artifact and must not be treated as a second writable authority.

WI-0108 closed with measured evidence rather than speculative PostgreSQL/hash optimization. The bounded prefetch correction reduced one representative 11-photo phone run from 39 to 12 preview opens, 50 to 13 collection API requests and 22 to 6 original hash reads while producing 10/11 prefetch hits and no progressive latency growth.

WI-0106 now has successful live backup/restore evidence. Because several retained rehearsal databases contain valid Photo Identity schemas, the maintainer identified the active production catalogue from PostgreSQL activity during real UI use rather than guessing from names or private connection-string formatting. With Photo Identity stopped, a custom-format production backup was created and hash-recorded, restored into an isolated database, and verified by schema version, complete public-table set, exact row counts and constraint validation. The isolated verification database was then explicitly removed while the verified backup/report were retained.

The PostgreSQL Compose service was subsequently stopped and started with Photo Identity quiesced. `verify-postgres.ps1 -SkipContainerStart` passed and normal application use remained healthy afterward, so container/service restart persistence is accepted. The combined work-item restart criterion remains open until an actual PC restart is also observed.

The first real PostgreSQL-backed catch-up pass is healthy. Initial synchronization discovered 304 additional source images. After synchronization, analysed images advanced from 15,730 to 15,792 while the unverified backlog moved from 712 to 649, with zero failed images. Advancement moved from the stale historical SQLite-lock blocked state through `syncing` to `running`; PostgreSQL stayed ready, transient hydration returned to zero in-progress, and aggregate diagnostics identified bounded persistence/hash work plus one analysis-session initialization cost rather than progressive database degradation. The progress/failure-metrics acceptance criterion is therefore satisfied, while the separate extended-duration catch-up criterion remains open for a longer run or completion of the remaining backlog.

The no-argument backup database resolver is not a blocker for M24 acceptance. On this maintainer installation the private launcher environment value has a wrapper shape that does not expose the database name to the generic parser reliably, so the accepted operational path uses explicit `-DatabaseName` after identifying the active authority from server activity. Do not continue connection-string unwrapping work unless it becomes a separate maintainability goal.

Consolidated real-phone M22 acceptance still has two separate functional gaps tracked by WI-0107: direct originating-gesture fullscreen launch and durable/revalidated prepared-original receipt state. Do not mix those functional corrections into M24 WI-0106.

A separate Collections / Library navigation gap remains outside this M24 thread. WI-0076 also remains separately recorded as in_progress and is not part of the M24 closeout.

## Next concrete step

For the M24 thread:

1. Keep the current **Advance archive** catch-up running and capture another health/status/storage/throughput checkpoint after substantially more backlog has been processed (or when catch-up completes). The acceptance question is continued forward progress with PostgreSQL healthy and no recurrence of lock/host-shutdown failure, not a SQLite/PostgreSQL performance comparison.
2. If that longer-run checkpoint remains healthy, mark the extended catch-up criterion complete and add a small real source increment. Verify normal synchronization, analysis, enrichment and review without full regeneration.
3. Before WI-0106 closeout, perform one actual Windows/PC restart and repeat the PostgreSQL/launcher health and representative catalogue checks to finish the combined restart criterion.
4. Reconcile the remaining WI-0106 acceptance evidence, decide when the preserved pre-cutover SQLite rollback snapshot can be retired under policy, and close M24 only after all operational exit criteria pass.

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
