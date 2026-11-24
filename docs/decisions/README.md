# Architecture decisions

ADRs are append-only. To change a decision, copy [adr-template.md](adr-template.md) to a new number, set `supersedes`,
mark the old ADR superseded in its front matter, and add a row here.

| ADR | Status | Decision |
|---|---|---|
| [ADR-0001](ADR-0001-modular-monolith.md) | accepted | Modular monolith with replaceable modules. |
| [ADR-0002](ADR-0002-model-independent-labels.md) | accepted | Canonical identity is independent of recognition models. |
| [ADR-0003](ADR-0003-local-onedrive-sync.md) | accepted | Personal OneDrive is read through the local Windows sync client. |
| [ADR-0004](ADR-0004-disposable-azure.md) | superseded by ADR-0010 | Treat Azure as disposable compute. |
| [ADR-0005](ADR-0005-precision-before-recall.md) | superseded by ADR-0006 | Precision before recall, with mandatory human confirmation. |
| [ADR-0006](ADR-0006-canonical-auto-assignment.md) | accepted | Configurable automatic assignment with provenance and correction. |
| [ADR-0007](ADR-0007-permanent-archive-bounded-storage.md) | accepted | One permanent archive identity with bounded local hydration and proxies. |
| [ADR-0008](ADR-0008-source-copy-exclusion-and-purge.md) | accepted | Exclusion applies to one source copy and purges its local data; the original is kept. |
| [ADR-0009](ADR-0009-postgresql-authoritative-catalogue.md) | accepted | PostgreSQL is the authoritative catalogue. The SQLite compatibility it allowed is now retired. |
| [ADR-0010](ADR-0010-local-production-execution.md) | superseded by ADR-0013 | Keep production processing on local hardware. |
| [ADR-0011](ADR-0011-operator-authorized-caption-burst-bridge.md) | accepted | Operator-authorized remote caption bursts through a localhost bridge. |
| [ADR-0012](ADR-0012-isolated-video-media-architecture.md) | accepted | Video is opt-in, revision-bound and isolated, using external FFmpeg. |
| [ADR-0013](ADR-0013-optional-operator-controlled-remote-compute.md) | accepted | Local by default; optional inference on our own Azure apps, no unvetted third parties. |
