# Architecture decision index

Accepted ADRs describe current architectural intent. When a decision changes materially, retain the earlier ADR and supersede it explicitly rather than rewriting the historical choice.

| ADR | Status | Current relevance |
|---|---|---|
| [ADR-0001](ADR-0001-modular-monolith.md) | accepted | The application remains a modular monolith with replaceable module boundaries. |
| [ADR-0002](ADR-0002-model-independent-labels.md) | accepted | Canonical identity remains independent of recognition-model revisions. ADR-0006 expands which audited actors may create canonical assignments. |
| [ADR-0003](ADR-0003-local-onedrive-sync.md) | accepted | Personal OneDrive remains a local Windows filesystem source. ADR-0007 adds stable permanent archive identity and bounded materialization. |
| [ADR-0004](ADR-0004-disposable-azure.md) | accepted, optional | Azure remains disposable optional compute and is not a version-1 dependency. |
| [ADR-0005](ADR-0005-precision-before-recall.md) | superseded | Its mandatory-human-confirmation rule is superseded by ADR-0006. Conservative precision remains an operating preference, not a requirement for every canonical assignment. |
| [ADR-0006](ADR-0006-canonical-auto-assignment.md) | accepted | Allows configurable canonical automatic identity assignment with provenance and correction. |
| [ADR-0007](ADR-0007-permanent-archive-bounded-storage.md) | accepted | Establishes one stable archive identity, incremental coverage and bounded local hydration/proxies. |