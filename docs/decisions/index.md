# Architecture decision index

Accepted ADRs describe current architectural intent. When a decision changes materially, retain the earlier ADR and supersede it explicitly rather than rewriting the historical choice.

| ADR | Status | Current relevance |
|---|---|---|
| [ADR-0001](ADR-0001-modular-monolith.md) | accepted | The application remains a modular monolith with replaceable module boundaries. |
| [ADR-0002](ADR-0002-model-independent-labels.md) | accepted | Canonical identity remains independent of recognition-model revisions. ADR-0006 expands which audited actors may create canonical assignments. |
| [ADR-0003](ADR-0003-local-onedrive-sync.md) | accepted | Personal OneDrive remains a local Windows filesystem source. ADR-0007 adds stable permanent archive identity and bounded materialization. |
| [ADR-0004](ADR-0004-disposable-azure.md) | superseded | Historical disposable-Azure design. ADR-0010 replaces it with local production execution. |
| [ADR-0005](ADR-0005-precision-before-recall.md) | superseded | Its mandatory-human-confirmation rule is superseded by ADR-0006. Conservative precision remains an operating preference, not a requirement for every canonical assignment. |
| [ADR-0006](ADR-0006-canonical-auto-assignment.md) | accepted | Allows configurable canonical automatic identity assignment with provenance and correction. |
| [ADR-0007](ADR-0007-permanent-archive-bounded-storage.md) | accepted | Establishes one stable archive identity, incremental coverage and bounded local hydration/proxies. |
| [ADR-0008](ADR-0008-source-copy-exclusion-and-purge.md) | accepted | Makes privacy exclusion source-copy-specific, prevents exclusion from following duplicates/moves and requires local Photo Identity data purge while preserving the source original. |
| [ADR-0009](ADR-0009-postgresql-authoritative-catalogue.md) | accepted | PostgreSQL is the sole writable production catalogue; SQLite is retained only for explicit migration/rollback compatibility. |
| [ADR-0010](ADR-0010-local-production-execution.md) | accepted | Production model execution, archive processing and canonical data remain on maintainer-controlled local hardware; Azure is not a planned execution target. |
