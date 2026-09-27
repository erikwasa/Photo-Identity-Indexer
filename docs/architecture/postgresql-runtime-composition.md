# PostgreSQL runtime composition

WI-0101 separated catalogue-provider selection from application services, WI-0102 completed the controlled maintainer production cutover on 2026-09-11, WI-0147 made PostgreSQL unconditional at runtime, and WI-0148 moved active tests/review verification to PostgreSQL. WI-0149 completes that transition by removing the SQLite implementation and active compatibility commands.

## Runtime composition

`PhotoIdentity:Postgres:ConnectionString` is required at API startup. Missing configuration fails immediately with a message that PostgreSQL is the only supported runtime catalogue. Review and identity, people and presentation state, Smart Collections, metadata and Places, detector state, source scanning, processing, archive state, hydration ownership and derivative metadata are bound directly to PostgreSQL implementations. PostgreSQL initialization must succeed before the application starts.

The Windows launcher requires `postgresConnectionEnvironmentVariable`, resolves the private connection string from Process, User or Machine environment scope, and supplies it only to the child process. Launcher settings do not accept provider selection or a database-path setting. `/health` always reports `catalogueProvider: postgresql` for a supported runtime host.

`PhotoIdentity.Api`, `PhotoIdentity.Cli`, active test projects and review-verification tooling no longer reference a SQLite persistence project or `Microsoft.Data.Sqlite`. Integration tests use isolated disposable PostgreSQL databases. The CLI retains PostgreSQL-backed catalogue operations plus provider-neutral operations such as media inspection, proxy measurement and database-free portable bundle processing; migration-era commands that opened SQLite catalogues were retired under WI-0149.

`PhotoIdentity.Worker` remains catalogue-independent. It processes explicit portable bundles and has no access to the authoritative catalogue, people or human review history.

## Slideshow persistence boundary

A Smart Collection slideshow snapshot is not a durable database entity. `ISmartCollectionQueryRepository.CreateSlideshowSnapshotAsync` evaluates the saved authoritative collection definition and returns an immutable ordered list of asset revision identifiers to the caller. `PostgresSmartCollectionQueryRepository` owns that evaluation, so snapshot membership is computed entirely from PostgreSQL catalogue state. Once returned, the snapshot is intentionally stable even if the saved collection or catalogue changes.

Original-preparation sessions are intentionally process-scoped. `SlideshowOriginalPreparationService` keeps progress, retry/cancellation state and the active task in memory because the browser owns the live preparation interaction. A process restart invalidates the session and requires the browser to prepare again. `SlideshowOriginalLeaseRegistry` is likewise a short, renewable eviction-protection lease for a live slideshow and must not become durable catalogue state.

Durable OneDrive hydration ownership, release state, availability and storage accounting remain PostgreSQL-backed archive state. The practical boundary is therefore:

- saved Smart Collection definitions, people/tags/Places metadata, asset revisions and archive hydration/accounting state are authoritative PostgreSQL data;
- generated slideshow snapshot lists are request-time presentation artifacts;
- preparation sessions and slideshow eviction-protection leases are transient coordination state; and
- durable hydration ownership created while preparing originals remains authoritative PostgreSQL archive state.

## Smart Collection and query boundary

`PostgresSmartCollectionQueryRepository` owns both paged Smart Collection queries and slideshow snapshot membership. Both paths use the same provider-neutral filter semantics for confirmed face assignments, manual photo people, generic tags, hierarchical Places, capture date and geographic bounds.

The PostgreSQL schema exposes indexes aligned with those predicates and history lookups: face occurrence/review-action history, photo-person history by revision and person, photo-tag history by revision and tag, photo-place history by revision and tag, capture-date and latitude/longitude indexes, asset source/presence indexes, and the Smart Collection normalized-name index. Performance work may tune those queries without changing storage ownership or snapshot semantics.

## Acceptance verification

`verify-postgres.ps1` is the explicit live PostgreSQL acceptance entry point. It retains the Windows/Podman/WSL connectivity and PostgreSQL-protocol checks, exports `PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING` only for the child verification process, builds the Release solution, and runs live PostgreSQL persistence/runtime acceptance. The connection string is not printed.

Normal CI additionally exercises disposable PostgreSQL integration databases, the published review verifier, Windows launcher/package verification, fast assemblies, and living-documentation validation/generation. WI-0148 established the PostgreSQL-only active test surface; WI-0149 requires the same gates to pass after the retired implementation and compatibility commands are removed.

## Historical cutover evidence

On 2026-09-10 the maintainer ran the WI-0101 PostgreSQL verifier against the configured Podman PostgreSQL service. WI-0102 subsequently imported and verified the maintainer catalogue, completed the single-authority production switch, exercised rollback from a working copy of the preserved migration backup, and restored PostgreSQL as the accepted authority.

Those SQLite-to-PostgreSQL migration and rollback records remain historical evidence. They do not constitute a current SQLite runtime/provider path. Current day-to-day backup, restore, restart and upgrade procedures are owned by [PostgreSQL production operations](../operations/postgresql-operations.md).
