# Documentation index

## Start here

- [README and project orientation](../README.md)
- [Build context](../BUILD_CONTEXT.md) — current development/verification handoff only
- [Local operator guide](operations/local-operator-guide.md)
- [PostgreSQL operations](operations/postgresql-operations.md)
- [Operations documentation map](operations/index.md)
- [Testing and CI strategy](operations/testing-and-ci-strategy.md)
- [Architecture overview](architecture/overview.md)
- [Glossary](glossary.md)

The local operator guide is the authoritative normal operating path. Specialized operations documents are classified in the operations index so completed experiment and migration records are not mistaken for current product instructions.

Formal delivery lifecycle status is kept in the canonical YAML registries. They are machine/audit records rather than the normal human current-status view; use `BUILD_CONTEXT.md` for the immediate continuation point.

## Operations

- [Operations documentation map](operations/index.md)
- [Local operator guide](operations/local-operator-guide.md)
- [PostgreSQL operations](operations/postgresql-operations.md)
- [Testing and CI strategy](operations/testing-and-ci-strategy.md)
- [Review-proxy serving and bounded originals](operations/review-proxy-serving.md)
- [Bounded archive acceptance](operations/bounded-archive-acceptance.md)
- [Azure burst captioning](operations/azure-burst-captioning.md) — optional temporary Remote caption inference through an operator-controlled Azure GPU endpoint
- [Historical SQLite persistence record — retired](operations/sqlite-persistence.md)

## Architecture

- [System overview](architecture/overview.md)
- [Principles](architecture/principles.md)
- [Applications](architecture/applications.md)
- [Module boundaries](architecture/module-boundaries.md)
- [Canonical data model](architecture/data-model.md)
- [Recognition and matching](architecture/identity-matching.md)
- [Portable processing bundles](architecture/portable-bundles.md)
- [Security and privacy](architecture/security-and-privacy.md)

## Product

- [Vision](product/vision.md)
- [Product scope](product/scope.md)
- [Protected Smart Collection slideshow](product/slideshow.md)
- [Source-copy lifecycle and privacy exclusion](product/source-copy-lifecycle.md)
- [Non-goals](product/non-goals.md)
- [Success criteria](product/success-criteria.md)

## Sources and processing

- [OneDrive synchronised source](sources/onedrive-sync.md)
- [Hydration and staging](sources/staging-and-hydration.md)

## Models

- [Evaluation method](models/evaluation-method.md)
- [Baseline models](models/baseline-models.md)
- [Candidate models](models/candidate-models.md)
- [Model manifests and governance](models/model-governance.md)

## Azure

- [Azure burst captioning](operations/azure-burst-captioning.md) — current optional operational use of ADR-0011 Remote caption inference
- [Tenant and identity constraints](azure/constraints.md) — historical reference
- [Identity-free execution](azure/identity-free-execution.md) — historical reference
- [Cost controls](azure/cost-controls.md) — historical general reference

The older Azure design documents are retained as historical reference. [ADR-0010](decisions/ADR-0010-local-production-execution.md) keeps the maintainer-controlled Windows computer as the production authority. [ADR-0011](decisions/ADR-0011-operator-authorized-remote-caption-inference.md) adds only a narrow, explicit exception: an operator may temporarily send bounded caption thumbnails to an Ollama-compatible Remote HTTPS endpoint, including one hosted on Azure. No active milestone depends on Azure infrastructure itself.

## Delivery

- [Local production strategy](delivery/local-first-plan.md)
- [Roadmap](delivery/roadmap.md)
- [Milestones](delivery/milestones/)
- [Work items](delivery/work-items/)
- [Canonical work-item registry](delivery/status/work-items.yaml)
- [Canonical milestone registry](delivery/status/milestones.yaml)
- [Risks](delivery/risks.md)
- [Templates](delivery/templates/)

## Decisions

- [Architecture decision index](decisions/index.md)

Accepted ADRs describe current intent. Superseding decisions use a new ADR and retain the earlier record.
