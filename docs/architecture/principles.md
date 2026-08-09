# Architectural principles

## Local ownership

The local system owns assets, revisions, face occurrences, crops, model definitions, embeddings, people, assignments, rejections, suggestions, evaluations and processing history.

## Disposable optional Azure compute

Azure receives finite input bundles and returns finite result bundles. Destroying Azure resources must not lose project data, and Azure is not required for the version-1 permanent-catalogue path.

## Replaceable models

Detection and embedding implementations sit behind narrow application-owned interfaces. Model changes may regenerate derived data but cannot silently rewrite canonical people, assignments or review history.

## Model-independent canonical identity

A canonical identity assignment belongs to a stable face occurrence and person, not to an embedding, cluster, model-specific identifier or cloud run.

The current runtime creates assignments through human review. ADR-0006 permits an explicitly enabled exact-model policy to create canonical automatic assignments once WI-0043 is implemented. Automatic assignments must retain full provenance and remain manually correctable through append-only history.

## Modular monolith first

Use enforceable module boundaries without premature distributed services.

## C# by default

Use C# for orchestration, inference, persistence, APIs, UI, bundles and Azure execution. Isolate Python behind neutral files when it provides a material advantage.

## Read-only photo archive

Never modify original photos. Store all derived and canonical catalogue data separately.

## Conservative automatic decisions

Prefer an unassigned or Unknown face over a weak confident assignment. Automatic identity assignment, when implemented and enabled, is restricted to a deliberately configured High-confidence policy and must be auditable and reversible.
