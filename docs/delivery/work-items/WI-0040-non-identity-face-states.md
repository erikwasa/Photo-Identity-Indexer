---
id: WI-0040
title: Define non-identity face states
milestone: M17
status_source: ../status/work-items.yaml
depends_on: [WI-0039]
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Persistence.Sqlite, Documentation]
---

# WI-0040: Define non-identity face states

## Objective

Define durable face-disposition states that do not require assignment to a named person.

## Required states

- `unreviewed` — no decision yet;
- `named_identity` — assigned through the existing canonical person workflow;
- `unknown_person` — a real person worth retaining without a name;
- `background_ignore` — a real face that is not useful for this archive;
- `not_a_face` — a false detector result; and
- `deferred` — intentionally postponed for later review.

Names may change during implementation, but the semantics must remain distinct.

## Acceptance criteria

- [ ] Face disposition is represented independently from person identity.
- [ ] Existing named assignments and append-only review history remain valid.
- [ ] Every transition is auditable and reversible.
- [ ] `unknown_person` can later become a named identity without recreating the face occurrence.
- [ ] `background_ignore` and `not_a_face` remain distinguishable for detector evaluation.
- [ ] Progress, suggestion, evaluation and collection semantics are documented for every state.
- [ ] Database migration and rollback behaviour are specified before implementation.
