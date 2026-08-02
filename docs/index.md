# Documentation index

## Start here

- [README and project orientation](../README.md)
- [Local operator guide](operations/local-operator-guide.md)
- [Architecture overview](architecture/overview.md)
- [Glossary](glossary.md)
- [Current delivery status](delivery/status/current.md)
- [Build context](../BUILD_CONTEXT.md)

The local operator guide is the authoritative end-to-end command sequence. Specialized documents explain individual subsystems and must not duplicate or contradict that path.

## Operations

- [Local operator guide](operations/local-operator-guide.md)
- [Single-revision local evaluation](operations/local-evaluation.md)
- [Multi-model comparison workflow](operations/multi-model-comparison.md)
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
- [Initial scope](product/scope.md)
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

Azure is not required for the accepted local workflow. It remains an optional later scale-out path after clean-setup documentation validation.

## Delivery

- [Local-first delivery plan](delivery/local-first-plan.md)
- [Roadmap](delivery/roadmap.md)
- [Milestones](delivery/milestones/)
- [Work items](delivery/work-items/)
- [Canonical work-item status](delivery/status/work-items.yaml)
- [Canonical milestone status](delivery/status/milestones.yaml)
- [Risks](delivery/risks.md)
- [Templates](delivery/templates/)

## Decisions

Architecture decisions are stored under [`docs/decisions`](decisions/). Accepted ADRs describe current intent; superseding decisions must use a new ADR rather than rewriting history.
