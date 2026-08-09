# Documentation index

## Start here

- [README and project orientation](../README.md)
- [Local operator guide](operations/local-operator-guide.md)
- [Operations documentation map](operations/index.md)
- [Architecture overview](architecture/overview.md)
- [Glossary](glossary.md)
- [Current delivery status](delivery/status/current.md)
- [Build context](../BUILD_CONTEXT.md)

The local operator guide is the authoritative normal operating path. Specialized operations documents are classified in the operations index so completed experiment runbooks are not mistaken for current product instructions.

## Operations

- [Operations documentation map](operations/index.md)
- [Local operator guide](operations/local-operator-guide.md)
- [Review-proxy serving and bounded originals](operations/review-proxy-serving.md)
- [Bounded archive acceptance](operations/bounded-archive-acceptance.md)
- [SQLite persistence operations](operations/sqlite-persistence.md)

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

## Azure — optional and deferred

- [Tenant and identity constraints](azure/constraints.md)
- [Identity-free execution](azure/identity-free-execution.md)
- [Cost controls](azure/cost-controls.md)

Azure is not required for version 1 or the accepted local permanent-catalogue workflow. It remains an optional later scale-out/experiment path.

## Delivery

- [Local-first delivery strategy](delivery/local-first-plan.md)
- [Roadmap](delivery/roadmap.md)
- [Milestones](delivery/milestones/)
- [Work items](delivery/work-items/)
- [Canonical work-item status](delivery/status/work-items.yaml)
- [Canonical milestone status](delivery/status/milestones.yaml)
- [Risks](delivery/risks.md)
- [Templates](delivery/templates/)

## Decisions

- [Architecture decision index](decisions/index.md)

Accepted ADRs describe current intent. Superseding decisions use a new ADR and retain the earlier record.