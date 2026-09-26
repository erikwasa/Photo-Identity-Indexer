# PostgreSQL runtime composition

WI-0101 separated catalogue-provider selection from application services, and WI-0102 completed the controlled maintainer production cutover on 2026-09-11. WI-0147 removes that transitional provider switch: the API and Windows launcher now compose PostgreSQL unconditionally and never dual-write authoritative catalogue state.

## Runtime composition

`PhotoIdentity:Postgres:ConnectionString` is required at API startup. Missing configuration fails immediately with a message that PostgreSQL is the only supported runtime catalogue. Review and identity, people and presentation state, Smart Collections, metadata and Places, detector state, source scanning, processing, archive state, hydration ownership and derivative metadata are bound directly to PostgreSQL implementations. PostgreSQL initialization must succeed before the application starts.

The Windows launcher requires `postgresConnectionEnvironmentVariable`, resolves the private connection string from Process, User or Machine environment scope, and supplies it only to the child process. Launcher settings no longer accept `PhotoIdentity__CatalogueProvider` or `PhotoIdentity__DatabasePath`. `/health` always reports `catalogueProvider: postgresql` for a supported runtime host.

`PhotoIdentity.Api` temporarily retains a project reference to the SQLite adapter only for the isolated `IntegrationTest` host used by existing API tests. The shared test factory selects that environment and explicitly enables static-web assets; the production launcher cannot select it and it is not an operator runtime mode. WI-0148 ports or retires those tests, and WI-0149 removes the adapter reference and compatibility composition. `PhotoIdentity.Worker` has no SQLite project reference; worker application services cannot silently acquire a concrete SQLite dependency.

The portable bundle CLI remains an explicit compatibility boundary. Bundle export consumes Core store/revision contracts, while the current CLI `--database` workflow deliberately composes the SQLite provider. Bundle result import remains an SQLite compatibility adapter. Detector-evaluation session, comparison and ground-truth JSON stores are provider-independent portable private artifacts, not catalogue persistence.

## Slideshow persistence audit

A Smart Collection slideshow snapshot is not a durable database entity. `ISmartCollectionQueryRepository.CreateSlideshowSnapshotAsync` evaluates the saved authoritative collection definition and returns an immutable ordered list of asset revision identifiers to the caller. PostgreSQL mode uses `PostgresSmartCollectionQueryRepository`, so snapshot membership is computed entirely from PostgreSQL catalogue state. Once returned, the snapshot is intentionally stable even if the saved collection or catalogue changes; persisting it server-side would change the existing M22 snapshot contract rather than migrate authoritative state.

Original-preparation sessions are also intentionally process-scoped. `SlideshowOriginalPreparationService` keeps progress, retry/cancellation state and the active task in memory because the browser owns the live preparation interaction. A process restart invalidates the session and requires the browser to prepare again. This avoids resurrecting stale work or promising best-quality playback after the coordinating process has disappeared.

`SlideshowOriginalLeaseRegistry` is a short, renewable eviction-protection lease for a live slideshow. It is deliberately ephemeral and expires when the browser stops refreshing it or the process restarts. It must not become durable catalogue state. Durable OneDrive hydration ownership, release state, availability and storage accounting remain in the archive persistence contracts, which resolve to PostgreSQL in PostgreSQL mode. Therefore a transient slideshow lease does not replace or bypass durable archive ownership.

The practical boundary is:

- saved Smart Collection definitions, people/tags/Places metadata, asset revisions and archive hydration/accounting state are authoritative and provider-backed;
- generated slideshow snapshot lists are request-time presentation artifacts;
- preparation sessions and slideshow eviction-protection leases are transient coordination state;
- durable hydration ownership created while preparing originals remains authoritative PostgreSQL archive state.

No additional PostgreSQL slideshow-session or snapshot tables are required for WI-0101. WI-0108 may optimize snapshot/query execution, but it must preserve these semantics rather than make transient presentation state authoritative.

## Smart Collection and slideshow query boundary

`PostgresSmartCollectionQueryRepository` owns both paged Smart Collection queries and slideshow snapshot membership. Both paths use the same provider-neutral filter semantics for confirmed face assignments, manual photo people, generic tags, hierarchical Places, capture date and geographic bounds. Snapshot creation therefore does not fall back to SQLite or a second query implementation when PostgreSQL is selected.

The PostgreSQL schema already exposes indexes aligned with those predicates and history lookups: face occurrence/review-action history, photo-person history by revision and person, photo-tag history by revision and tag, photo-place history by revision and tag, capture-date and latitude/longitude indexes, asset source/presence indexes, and the Smart Collection normalized-name index. These indexes establish the persistence/query boundary that WI-0108 can tune without changing storage ownership or snapshot semantics.

WI-0101 does not claim slideshow latency acceptance from the presence of these indexes. Query plans and real-library latency remain WI-0108 work. The WI-0101 conclusion is narrower: PostgreSQL mode has one authoritative Smart Collection/slideshow query implementation and schema-level index support for every current filter dimension.

## Acceptance verification

`verify-postgres.ps1` is the explicit live acceptance entry point for WI-0101. It retains the Windows/Podman/WSL connectivity and PostgreSQL-protocol checks, then exports `PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING` only for the child verification process. The connection string is not printed.

After connectivity succeeds, the verifier builds the Release solution and runs the full `Postgres*` persistence test set with live PostgreSQL enabled. That set includes clean/idempotent schema initialization and upgrade coverage plus repository behavior for review/identity, people/presentation, metadata/Places, Smart Collections, detector state, source scanning, processing, archive state and derivatives. It then runs PostgreSQL runtime/composition integration coverage, including the selected-host proof that no `SqliteCatalogueDatabase` is registered or created.

`PostgresArchiveRuntimeAcceptanceTests` adds a cross-repository archive scenario in one disposable database rather than proving repositories only in isolation. The scenario exercises archive coverage, status and item paging, availability transitions, revision/source hydration ownership transfer during re-verification, storage accounting and post-analysis proxy completion through the Core contracts used by the runtime.

A normal CI run still cannot claim live PostgreSQL acceptance when the private connection setting is absent; those opt-in tests return without connecting. Existing-catalogue import/cutover acceptance is recorded under WI-0102.

## WI-0101 accepted runtime boundary

On 2026-09-10 the maintainer ran `verify-postgres.ps1` from `codex/m24-6` against the configured Podman PostgreSQL service. Podman authentication and the Windows-localhost PostgreSQL protocol check passed. The Release solution built in 24.96 seconds with zero warnings and zero errors. The complete live PostgreSQL persistence set passed 28/28 tests in 6 seconds with zero skips, and PostgreSQL runtime/composition acceptance passed 4/4 tests in 1 second with zero skips.

PR #277 CI run #1550 also completed successfully. Both integration shards, the normal build/test lane, documentation validation/generated-output checks, published review verification and Windows mixed-media verification passed. This CI evidence is complementary to the local live run: CI proves the normal repository/application gates remain intact, while the explicit verifier proves the PostgreSQL-only tests actually connected and executed.

The accepted WI-0101 boundary is therefore: PostgreSQL mode provides the authoritative runtime graph without SQLite catalogue reads/writes or dual writes; remaining SQLite code is confined to the explicit SQLite provider plus CLI/import/migration/test compatibility boundaries. PostgreSQL schema initialization/upgrade and the cross-domain archive/runtime behavior have live acceptance evidence.

WI-0102 subsequently imported the maintainer catalogue, verified representative user-visible state, completed the single-authority production switch, exercised rollback from a working copy of the preserved SQLite backup, and restored PostgreSQL as the accepted authority. Longer-term PostgreSQL operational backup/recovery and sustained archive catch-up remain WI-0106.
