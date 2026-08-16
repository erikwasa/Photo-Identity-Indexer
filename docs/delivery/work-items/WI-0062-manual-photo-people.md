---
id: WI-0062
title: Add manual photo-level people
milestone: M19
status_source: ../status/work-items.yaml
depends_on: [WI-0050, WI-0061]
related_adrs: []
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Persistence.Sqlite, PhotoIdentity.Api, PhotoIdentity.Web]
---

# WI-0062: Add manual photo-level people

## Objective

Allow a maintainer to state that a canonical person appears in a photo even when no usable face was detected, while keeping that photo-level statement completely separate from face detection, face assignment, embeddings and identity evidence.

## Core semantic boundary

A manual photo-person assignment means only **this person is present in this image**. It must not fabricate a face occurrence or imply that Photo Identity detected, cropped, embedded or identified a face.

Manual photo-level presence must therefore not create or mutate:

- `face_occurrences`;
- face observations or bounding boxes;
- face crops;
- embeddings;
- face review assignments/rejections;
- identity suggestions or rankings; or
- model-training/evaluation evidence.

## In scope

- Add revision-bound, auditable photo-person presence state for canonical active people.
- Prefer an append-only add/remove action model so manual presence changes retain history in the same spirit as manual photo tags.
- Add Photo Details controls to search/select canonical people, add a person to the photo and remove a manual photo-level assignment.
- Show one consolidated person entry when the same canonical person is supported by both a confirmed face and a manual photo-level assignment; the UI may distinguish evidence sources.
- Extend the Smart Collections People dimension so it queries the union of confirmed face assignments and active manual photo-level presence.
- Preserve existing `all`/`any` Smart Collection people semantics over that union.
- Keep the legacy `/collections` people/evidence workspace face-evidence based; photo-level manual presence must not masquerade as confirmed face evidence there.
- Extend person merge handling so manual photo-person assignments follow the canonical merge target without duplication or loss of auditability.
- Reject assignments to missing or already-merged people unless the canonical target is explicitly resolved.
- Keep operations metadata-only and safe for online-only originals.

## Persistence model

`photo_person_actions` is a dedicated append-only relation keyed by immutable asset revision plus canonical person. Each row records `add`/`remove`, actor and timestamp; effective presence is the latest action for a revision/person pair.

The table and indexes are created by an idempotent SQLite schema guard, following the existing catalogue pattern used by capture metadata. A merge trigger copies effective source-person presence to the active canonical target only when the target is not already effectively present. Historical source-person actions are retained unchanged, so merge transfer does not erase audit history or let source/target action timestamps accidentally cancel a valid assignment.

This relation is intentionally separate from `person_labels` and `review_actions`, which remain tied to face occurrences.

## Implementation

- Draft PR #155 implements the work item on `agent/WI-0062-manual-photo-people`.
- Photo Details mutation endpoints are catalogue-only and return the existing consolidated details response after each add/remove.
- `SqlitePhotoDetailsRepository` unions active manual presence with confirmed face assignments and keeps `ConfirmedFaceCount` and `ManualPresence` independent.
- The Photo Details People editor searches active canonical people, labels face/manual evidence explicitly, and only exposes removal for the manual evidence source.
- `SqliteSmartCollectionQueryRepository` evaluates People filters against the union of confirmed face people and active manual photo people; existing `all`/`any` semantics are unchanged.
- No legacy `/collections` face-evidence query is changed.
- Integration tests use source roots that do not exist locally and verify zero writes to face/evidence tables during manual add/remove.

## Out of scope

- Drawing a manual face box.
- Creating synthetic face occurrences when detection failed.
- Generating crops or embeddings from a manually tagged person.
- Feeding manual photo-level presence into identity matching, confidence calculation or suggestion generation.
- Automatically inferring people from filenames, tags, captions or other metadata.

## Acceptance criteria

- [x] A maintainer can add an active canonical person to a photo from Photo Details even when the photo has no detected faces. Implementation and automated API coverage complete; local browser verification is deferred to the consolidated M19 pass.
- [x] A maintainer can remove that manual photo-level person assignment and the add/remove history remains auditable.
- [x] Manual photo-person assignment does not create or change any face occurrence, crop, embedding, face review action or identity suggestion.
- [x] Photo Details consolidates confirmed-face and manual-presence evidence without showing duplicate person rows.
- [x] Smart Collections People filters include manual photo-level presence and preserve existing `all`/`any` behavior.
- [x] The legacy face-evidence collection/review flows remain face-based and are not contaminated by photo-only presence.
- [x] Person merge consolidates photo-level assignments onto the canonical target safely while preserving source history.
- [x] Adding/removing manual people does not hydrate or modify an online-only original.
- [x] Automated tests cover no-face photos, combined face/manual evidence, add/remove audit history, Smart Collection filtering and merge behavior.

## Verification requirements

Automated persistence/API/query tests plus a local browser pass on a photo where a known person was not detected are required. Per the maintainer's M19 verification plan, the local browser pass is intentionally deferred until the remaining M19 work items are implemented. The consolidated pass must confirm that manual presence becomes available to Smart Collections while Face Review remains unchanged.
