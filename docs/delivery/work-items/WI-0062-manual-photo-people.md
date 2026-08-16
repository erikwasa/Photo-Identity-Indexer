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

## Recommended persistence model

Introduce a dedicated photo-person action relation keyed by immutable asset revision plus canonical person, with `add`/`remove`, actor and timestamp. Effective presence is derived from the latest active action for each revision/person pair.

This relation is intentionally separate from `person_labels` and `review_actions`, which remain tied to face occurrences.

## Out of scope

- Drawing a manual face box.
- Creating synthetic face occurrences when detection failed.
- Generating crops or embeddings from a manually tagged person.
- Feeding manual photo-level presence into identity matching, confidence calculation or suggestion generation.
- Automatically inferring people from filenames, tags, captions or other metadata.

## Acceptance criteria

- [ ] A maintainer can add an active canonical person to a photo from Photo Details even when the photo has no detected faces.
- [ ] A maintainer can remove that manual photo-level person assignment and the add/remove history remains auditable.
- [ ] Manual photo-person assignment does not create or change any face occurrence, crop, embedding, face review action or identity suggestion.
- [ ] Photo Details consolidates confirmed-face and manual-presence evidence without showing duplicate person rows.
- [ ] Smart Collections People filters include manual photo-level presence and preserve existing `all`/`any` behavior.
- [ ] The legacy face-evidence collection/review flows remain face-based and are not contaminated by photo-only presence.
- [ ] Person merge consolidates photo-level assignments onto the canonical target safely.
- [ ] Adding/removing manual people does not hydrate or modify an online-only original.
- [ ] Automated tests cover no-face photos, combined face/manual evidence, add/remove audit history, Smart Collection filtering and merge behavior.

## Verification requirements

Automated persistence/API/query tests plus a local browser pass on a photo where a known person was not detected are required. The maintainer must confirm that the person becomes available to Smart Collections while Face Review remains unchanged.
