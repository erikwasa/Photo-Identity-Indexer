# Architectural principles

## Local ownership

The local system owns assets, revisions, face occurrences, crops, model definitions, embeddings, people, labels, rejections, suggestions, evaluations and processing history.

## Disposable Azure compute

Azure receives finite input bundles and returns finite result bundles. Destroying Azure resources must not lose project data.

## Replaceable models

Detection and embedding implementations sit behind narrow application-owned interfaces. Model changes may regenerate derived data but cannot alter people or human labels.

## Canonical human labels

A human confirmation belongs to a stable face occurrence, not an embedding, cluster, model-specific identifier or cloud run.

## Modular monolith first

Use enforceable module boundaries without premature distributed services.

## C# by default

Use C# for orchestration, inference, persistence, APIs, UI, bundles and Azure execution. Isolate Python behind neutral files when it provides a material advantage.

## Read-only photo archive

Never modify original photos. Store all derived data separately.

## Precision before recall

Prefer unknown faces over incorrect confident assignments.
