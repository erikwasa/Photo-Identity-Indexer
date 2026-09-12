# Architecture overview

Photo Identity Indexer is a local-first modular monolith. The maintainer-controlled Windows computer is the trusted application/control environment and runs the production workflow on local hardware.

It accesses Personal OneDrive through the Windows sync client, runs governed model processing, hosts the API and responsive browser application, and connects to the authoritative PostgreSQL catalogue running on the maintainer's machine. Personal source paths, people, review history and derived biometric data remain inside the local trust boundary.

```text
Personal OneDrive or local folder
        │ Windows sync client / local filesystem
        ▼
Trusted Windows application/control environment
        ├── PhotoIdentity.Cli
        │     local diagnostics, evaluation and compatibility/admin tools
        ├── PhotoIdentity.Api + PhotoIdentity.Web
        │     archive advancement, review, people, collections and slideshow
        ├── PostgreSQL authoritative catalogue
        │     sources, revisions, people, review history and run state
        ├── governed local artefact storage
        │     analysis output, proxies, derivatives, reports and backups
        └── exact model manifests and installed ONNX files
```

Production processing is local. [ADR-0010](../decisions/ADR-0010-local-production-execution.md) supersedes the earlier disposable-Azure plan. Portable bundle support remains as an isolation/file-transfer contract, but no active roadmap milestone assumes Azure or another remote worker.

## Runtime applications

- **`PhotoIdentity.Cli`** provides local diagnostics, evaluation, migration/compatibility, portable-bundle and administrative workflows.
- **`PhotoIdentity.Worker`** contains headless processing components used by governed analysis and portable processing contracts.
- **`PhotoIdentity.Api`** hosts archive advancement, review, people, audit, progress, photo delivery, collection and slideshow endpoints.
- **`PhotoIdentity.Web`** is the responsive Blazor application used from Windows and supported devices on a trusted private network.

See [Applications](applications.md) for executable responsibilities and [Module boundaries](module-boundaries.md) for project dependencies.

## Data ownership

Canonical local data includes:

- source, asset and immutable revision identity;
- people and canonical assignments/rejections;
- append-only review and person-maintenance history;
- processing-run and job state needed for safe resume; and
- governed provenance and operational state.

PostgreSQL is the sole writable production catalogue. SQLite support is retained only where explicitly required for migration, rollback or compatibility and is not a second production authority.

Derived, replaceable data includes:

- detector observations;
- aligned crops and face-review derivatives;
- model-versioned embeddings;
- ranked identity suggestions;
- review proxies and collection thumbnails;
- portable processing outputs; and
- evaluation exports and reports.

Derived artefacts remain sensitive even when replaceable. They must not be committed or exposed outside the trusted boundary.

See [Canonical data model](data-model.md), [ADR-0009](../decisions/ADR-0009-postgresql-authoritative-catalogue.md) and the [Glossary](../glossary.md).

## Immutable revisions and model provenance

A changed photo creates a new asset revision. Detection, crops and embeddings attach to immutable revision and face-occurrence identities so old results cannot silently be reused for changed content.

A model revision is identified by its model ID, exact SHA-256 hash and material preprocessing contract. Baseline and candidate embeddings can coexist in one catalogue. Suggestions and evaluation reports identify the exact revision that produced them.

Scores from different revisions are not assumed to share one threshold or distribution.

## Review and identity boundary

People and canonical identity decisions are model-independent and auditable. Human actions remain governed history, while ADR-0006 also permits explicitly enabled automatic canonical assignment under an exact-model policy with provenance and correction semantics.

Identity suggestions remain derived and regenerable. Rejected face-person evidence is preserved and matching behavior remains exact-model scoped.

See [Recognition and identity matching](identity-matching.md).

## Collection and slideshow boundary

Collection queries and slideshow snapshots operate from the local production catalogue and derived media. Browser-facing contracts use opaque identifiers and HTTP resource URLs rather than exposing source roots or private local paths.

Originals are hydrated only through explicit governed access/preparation paths; normal browsing remains derivative/proxy-backed where possible.

## Portable compute boundary

Portable job bundles contain explicitly selected neutral inputs, exact model manifests and checksums. Result bundles contain derived processing results and checkpoints. Import validates provenance, revision identity and checksums before changing local derived state.

The portable worker contract has no access to OneDrive credentials, people, assignments, rejections or the authoritative catalogue. It is retained for isolation/offline transfer, not as a currently planned cloud deployment path. See [Portable processing bundles](portable-bundles.md).

## Deployment and trust

The browser application is unauthenticated and is intended for localhost or a trusted private network only. PostgreSQL data, backups and local derivatives stay on maintainer-controlled storage rather than a synchronised cloud folder.

Original photos are read-only inputs. The system does not modify them.

See [Security and privacy](security-and-privacy.md), [PostgreSQL operations](../operations/postgresql-operations.md) and the [Local operator guide](../operations/local-operator-guide.md).
