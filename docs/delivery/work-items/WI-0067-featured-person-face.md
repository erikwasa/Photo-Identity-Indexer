---
id: WI-0067
title: Add featured representative faces for people
milestone: M19
status_source: ../status/work-items.yaml
depends_on: [WI-0033, WI-0042]
related_adrs: []
affected_modules: [PhotoIdentity.Persistence.Sqlite, PhotoIdentity.Api, PhotoIdentity.Web]
---

# WI-0067: Add featured representative faces for people

## Objective

Allow a canonical person to have one explicitly selected representative face for portrait-oriented UI, while providing a stable automatic fallback when no explicit face is selected.

The durable reference should identify the assigned face occurrence rather than merely a photo or filename. A photo can contain several people; the representative image must therefore remain tied to the particular face that represents the person.

## User contract

- Face Details exposes `Set as featured photo` when the current face is assigned to a named person.
- A person can have at most one explicit featured face.
- The user can clear the explicit choice and return to automatic selection.
- The selected featured face is presentation metadata only. It does not change assignments, recognition training/evidence, suggestions or favorite status.
- Maintain People shows the resolved representative portrait so the preference can be inspected outside Face Details.

## Automatic fallback

When no explicit featured face is set, resolve one valid assigned face automatically using a deterministic rule. Do not choose randomly on each request or page load.

The first implementation may use a simple stable ordering rule. The contract must allow the fallback algorithm to improve later, for example by preferring a larger or higher-confidence face, without changing the persisted explicit-selection model.

If an explicitly selected face becomes invalid because it is reassigned, removed from the person's valid assignments, or its underlying revision is no longer available to the resolver, the UI must safely fall back to the automatic representative. The implementation may clear the stale reference eagerly or ignore it during resolution, but it must not show another person's face as the representative.

## Persistence and merge semantics

Persist a person-to-face reference or equivalent durable presentation record. The stored reference must be stable and privacy-safe and must not depend on source paths or original filenames.

Person merge behavior must be deterministic:

- if the surviving person already has an explicit featured face, keep it;
- otherwise an explicit featured face from the merged source may be retained only if that face now validly belongs to the survivor;
- stale/invalid references must fall back safely rather than blocking the merge.

## Presentation contract

Expose a resolved representative-face image URL and enough state to distinguish explicit versus automatic selection where useful. Reuse the existing durable face-review derivative/preview path rather than creating an unrelated thumbnail pipeline unless a concrete quality limitation requires it.

The representative portrait is intended for person-oriented UI such as Maintain People and, through WI-0068, the Smart Collection people selector. It should not replace the actual face image in review queues where the current face occurrence itself is the subject.

## In scope

- Durable explicit featured-face preference.
- Deterministic automatic representative fallback.
- Face Details set/clear controls with assignment validation.
- Maintain People representative portrait display.
- Safe invalidation/reassignment behavior.
- Deterministic person-merge semantics.
- API contract suitable for reuse by Smart Collections.

## Out of scope

Automatic face-quality scoring as a new ML feature, editing/cropping portraits, changing recognition embeddings, replacing face-review occurrence images, or choosing an entire photo instead of a face occurrence.

## Acceptance criteria

- [x] An assigned named face can be set as that person's explicit featured face from Face Details.
- [x] A person has at most one explicit featured face.
- [x] The explicit choice survives application restart.
- [x] The user can clear the explicit choice and return to automatic representative selection from Face Details or through the API contract.
- [x] When no explicit choice exists, the same valid automatic representative is selected deterministically for unchanged catalogue state.
- [x] An explicit featured face is never accepted for a different person.
- [x] If the selected face is reassigned or otherwise becomes invalid, representative resolution safely falls back without showing the wrong identity.
- [ ] Maintain People displays the resolved representative portrait.
- [ ] Person merge behavior preserves a valid survivor preference according to the documented deterministic rule.
- [x] Featured-face changes do not modify canonical assignments, suggestions, embeddings or recognition model data.
- [ ] Automated coverage verifies persistence, validation, fallback and merge behavior.

## Suggested implementation slices

1. Persistence, representative resolver and API contracts with deterministic fallback tests.
2. Face Details set/clear controls and invalidation/reassignment coverage.
3. Maintain People portrait presentation and merge behavior coverage.

## Implementation status

Slice 1 merged in PR #175. It adds a durable, idempotently guarded person-to-face preference, validates explicit choices against the current active assignment, resolves stale preferences safely, uses the earliest currently assigned face as the first deterministic fallback, exposes GET/PUT representative-face contracts that reuse `/api/review/faces/{id}/image`, and adds integration coverage for persistence, clear-to-automatic behavior, reassignment fallback and wrong-person rejection.

Slice 2 is implemented on `agent/WI-0067-face-details-featured-controls`. Face Details now shows representative-photo state only when the current face is assigned to a named person, can set the current face as the explicit featured photo, and can clear an explicit choice back to automatic selection. The review image remains the current face occurrence rather than being replaced by the representative portrait, and the controls reuse the Slice 1 API/invalidation behavior without adding another host-heavy integration-test path.

Slice 3 remains: show the resolved representative portrait in Maintain People and add deterministic person-merge behavior coverage.
