---
id: WI-0041
title: Add unknown and background face review
milestone: M17
status_source: ../status/work-items.yaml
depends_on: [WI-0040]
affected_modules: [PhotoIdentity.Api, PhotoIdentity.Web, PhotoIdentity.Persistence.Sqlite, PhotoIdentity.Integration.Tests]
---

# WI-0041: Add unknown and background face review

## Objective

Let reviewers dispose of real but unnamed or irrelevant faces without creating artificial people.

## Scope

- Add individual actions for `Unknown person`, `Background / ignore`, `Not a face` and `Deferred`.
- Add bounded bulk actions for group-photo and low-priority background faces.
- Exclude ignored and false detections from normal identity suggestions and review queues.
- Provide optional filters or queues for unknown and deferred faces.
- Preserve crops, detector observations and audit history for reversible decisions.
- Keep collections and identity progress limited to named identities unless a query explicitly requests another state.

## Acceptance criteria

- [ ] A reviewer can resolve a detected face without selecting or creating a named person.
- [ ] Background and false-detection faces disappear from normal identity work immediately.
- [ ] Unknown-person faces can be revisited and assigned later.
- [ ] Bulk actions show their scope before mutation and remain auditable.
- [ ] Undo restores the prior disposition and queue membership.
- [ ] Suggestions are not generated for ignored or false-detection states.
- [ ] Existing named-person correction, merge and audit workflows continue to pass.
