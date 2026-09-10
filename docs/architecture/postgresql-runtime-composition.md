# PostgreSQL runtime composition

WI-0101 separates catalogue-provider selection from application services. `PhotoIdentity:CatalogueProvider` selects one authoritative catalogue graph for the API and hosted workers. SQLite remains the default until WI-0102 performs the controlled production cutover; `postgresql` is an explicit opt-in mode for migration verification. The process never dual-writes authoritative catalogue state.

## Provider selection

When `PhotoIdentity:CatalogueProvider=sqlite` (or the setting is omitted), the API binds authoritative Core contracts to SQLite as before. If a PostgreSQL connection string is also configured, it is used only for the existing migration/readiness probe and is not bound to authoritative contracts.

When `PhotoIdentity:CatalogueProvider=postgresql`, `PhotoIdentity:Postgres:ConnectionString` is required. Review and identity, people and presentation state, Smart Collections, metadata and Places, detector state, source scanning, processing, archive state, hydration ownership and derivative metadata are bound to PostgreSQL implementations. PostgreSQL initialization runs before the application starts. The SQLite catalogue is not registered, opened or migrated in this mode, and the SQLite schema ensure helpers are not called.

`PhotoIdentity.Api` intentionally keeps a project reference to the SQLite adapter while SQLite remains the supported default provider. `Program.cs` and `CataloguePersistenceComposition.cs` therefore remain legitimate SQLite references until WI-0102. `PhotoIdentity.Worker` has no SQLite project reference; worker application services cannot silently acquire a concrete SQLite dependency.

The portable bundle CLI remains an explicit compatibility boundary. Bundle export now consumes Core store/revision contracts, but the current CLI `--database` workflow deliberately composes the SQLite provider. Bundle result import remains an SQLite import adapter until the import/cutover work owned by WI-0102. Detector-evaluation session, comparison and ground-truth JSON stores are provider-independent portable private artifacts, not catalogue persistence.

## Slideshow persistence audit

A Smart Collection slideshow snapshot is not a durable database entity. `ISmartCollectionQueryRepository.CreateSlideshowSnapshotAsync` evaluates the saved authoritative collection definition and returns an immutable ordered list of asset revision identifiers to the caller. PostgreSQL mode uses `PostgresSmartCollectionQueryRepository`, so snapshot membership is computed entirely from PostgreSQL catalogue state. Once returned, the snapshot is intentionally stable even if the saved collection or catalogue changes; persisting it server-side would change the existing M22 snapshot contract rather than migrate authoritative state.

Original-preparation sessions are also intentionally process-scoped. `SlideshowOriginalPreparationService` keeps progress, retry/cancellation state and the active task in memory because the browser owns the live preparation interaction. A process restart invalidates the session and requires the browser to prepare again. This avoids resurrecting stale work or promising best-quality playback after the coordinating process has disappeared.

`SlideshowOriginalLeaseRegistry` is a short, renewable eviction-protection lease for a live slideshow. It is deliberately ephemeral and expires when the browser stops refreshing it or the process restarts. It must not become durable catalogue state. Durable OneDrive hydration ownership, release state, availability and storage accounting remain in the archive persistence contracts, which resolve to PostgreSQL in PostgreSQL mode. Therefore a transient slideshow lease does not replace or bypass durable archive ownership.

The practical boundary is:

- saved Smart Collection definitions, people/tags/Places metadata, asset revisions and archive hydration/accounting state are authoritative and provider-backed;
- generated slideshow snapshot lists are request-time presentation artifacts;
- preparation sessions and slideshow eviction-protection leases are transient coordination state;
- durable hydration ownership created while preparing originals remains authoritative archive state and follows the selected catalogue provider.

No additional PostgreSQL slideshow-session or snapshot tables are required for WI-0101. WI-0108 may optimize snapshot/query execution, but it must preserve these semantics rather than make transient presentation state authoritative.

## Remaining verification boundary

Provider registration tests prove that representative contracts across the authoritative domains resolve to PostgreSQL without registering `SqliteCatalogueDatabase`. Repository tests cover individual PostgreSQL behavior. WI-0101 still needs live PostgreSQL startup/host verification and cross-domain archive behavior verification before it can claim that normal PostgreSQL operation is fully accepted. Those checks are distinct from WI-0102's production cutover and existing-catalogue import work.
