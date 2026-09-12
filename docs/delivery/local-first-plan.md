# Local production strategy

Local operation is the durable product strategy.

The maintainer-controlled Windows computer is the trusted application/control environment. It owns the normal Photo Identity runtime, private configuration, model execution and access to Personal OneDrive through the Windows sync client. PostgreSQL running on the maintainer's hardware is the sole writable production catalogue. Personal photos, derived biometric data and review history remain under local control.

[ADR-0010](../decisions/ADR-0010-local-production-execution.md) supersedes the earlier disposable-Azure execution plan. Azure is not a planned processing target. Existing Azure and portable-bundle documentation may remain as historical design/reference material, but current delivery work does not depend on a cloud pilot, cloud checkpointing or local/cloud consistency evidence.

## Current operating shape

### Permanent archive

The archive uses one stable source identity and incremental included-folder coverage. Synchronization revisits included coverage for new, changed, missing or newly available files while unchanged completed revisions are reused.

Authoritative originals remain in Personal OneDrive. Normal review uses durable local derivatives/proxies; full-resolution originals are materialized only when processing or explicit viewing requires them and remain governed by bounded hydration/storage policy.

### Production catalogue

M24 migrated production authority to PostgreSQL and verified backup/restore, container and PC restart persistence, sustained archive catch-up and a real small daily-style increment. The previous SQLite catalogue is no longer a second writable authority; preserved SQLite state is historical migration/rollback material only.

### Archive completion and increments

The original M12 WI-0023 full-archive batch plan is superseded by M24/WI-0106 production evidence. The real production catalogue reached full catch-up and subsequently processed new photos incrementally without regenerating the completed catalogue.

The separate M13 periodic-synchronization milestone is retired. Explicit archive advancement already performs synchronization and incremental processing. If unattended scheduling is wanted later, it should be newly scoped against the current PostgreSQL/archive-advancement architecture rather than reviving WI-0024.

### Models

The production archive uses the governed local detector/embedder configuration documented by the recognition and operator material. The old M11 requirement for Azure consistency/cost evidence is retired. Future model changes remain evidence-driven local model-governance work.

## Retired cloud milestones

M09, M10 and M11 are closed as retired planning work:

- M09/WI-0020 — Azure VM pilot: not executed and no longer planned.
- M10/WI-0021 — Azure checkpointing: not executed and no longer needed.
- M11/WI-0022 — Azure-dependent production-model selection contract: replaced by the governed local production configuration.

A future remote/cloud execution path would require a new ADR and new work items.

## Delivery rule

Do not create a second production catalogue or cloud control plane for later features. New review, slideshow, privacy/exclusion, metadata and collection capabilities extend the accepted local production system through explicit migrations and versioned derived data.
