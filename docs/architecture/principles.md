# Architectural principles

## Local ownership and execution

The local system owns assets, revisions, face occurrences, crops, model definitions, embeddings, people, assignments, rejections, suggestions, evaluations and processing history. Normal model execution and archive processing run locally; ADR-0013 permits explicitly authorized, bounded inference on operator-managed Azure infrastructure without transferring catalogue/control authority.

The authoritative PostgreSQL catalogue, private source access, review history and derived biometric data remain local. Personal OneDrive is accessed through the Windows sync client rather than a cloud API.

## Replaceable models

Detection and embedding implementations sit behind narrow application-owned interfaces. Model changes may regenerate derived data but cannot silently rewrite canonical people, assignments or review history.

## Model-independent canonical identity

A canonical identity assignment belongs to a stable face occurrence and person, not to an embedding, cluster, model-specific identifier or remote processing run.

ADR-0006 permits an explicitly enabled exact-model policy to create canonical automatic assignments. Automatic assignments must retain full provenance and remain manually correctable through append-only history.

## Modular monolith first

Use enforceable module boundaries without premature distributed services.

## C# by default

Use C# for orchestration, inference, persistence, APIs, UI and bundles. Isolate Python behind neutral files when it provides a material advantage.

Portable bundles can support offline or isolated processing. ADR-0013 permits optional operator-controlled Azure inference; any new remote integration still requires separately scoped work, safeguards and acceptance evidence.

## Read-only photo archive

Never modify original photos. Store all derived and canonical catalogue data separately.

## Conservative automatic decisions

Prefer an unassigned or Unknown face over a weak confident assignment. Automatic identity assignment, when enabled, is restricted to a deliberately configured High-confidence policy and must be auditable and reversible.
