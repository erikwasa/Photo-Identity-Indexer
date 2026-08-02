---
id: M17
title: Unknown and background face handling
status_source: ../status/milestones.yaml
depends_on: [M16]
---

# M17: Unknown and background face handling

## Outcome

Detected faces can be retained, ignored, deferred or marked as an unnamed person without forcing every real face into a named identity.

## Work items

- [WI-0040](../work-items/WI-0040-non-identity-face-states.md) — define durable non-identity face states and semantics
- [WI-0041](../work-items/WI-0041-background-face-review.md) — implement review, filtering and bulk actions for unknown and background faces
- [WI-0042](../work-items/WI-0042-background-face-validation.md) — validate review burden, reversibility and identity invariants

## Product principles

- A real face is not automatically a person identity.
- `Unknown person` represents a retained but unnamed person who may later be identified.
- `Background / ignore` suppresses a real but irrelevant face from normal review and suggestions.
- `Not a face` remains distinct detector-quality evidence.
- Every non-identity decision is auditable and reversible.
- Ignored faces do not distort identity progress, collection results or matcher evaluation.

## Exit criteria

- The canonical data model distinguishes identity assignment from face disposition.
- Reviewers can apply non-identity outcomes individually and in bounded groups.
- Ignored faces are excluded from normal suggestion and review queues while detector evidence remains available.
- Unknown-person faces can be revisited or clustered later without requiring a name.
- Automated and private-pilot evidence shows that background faces no longer create unbounded identity work.
