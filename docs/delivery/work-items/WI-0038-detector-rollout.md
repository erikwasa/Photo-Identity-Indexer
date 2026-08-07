---
id: WI-0038
title: Roll out the selected detector pipeline
milestone: M16
status_source: ../status/work-items.yaml
depends_on: [WI-0037]
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Persistence.Sqlite, PhotoIdentity.Worker, PhotoIdentity.Web]
---

# WI-0038: Roll out the selected detector pipeline

## Objective

Adopt the first detector pipeline that meets the M16 target without attaching new detections to the wrong reviewed face occurrences or losing existing identity-review history.

## Activation

WI-0037 completed on 2026-08-07 after the maintainer:

- explicitly accepted the documented CenterFace model-weight/training-data uncertainty for local evaluation;
- completed the unchanged 100-photo comparison against the frozen WI-0034 ground truth;
- reported that CenterFace confidence `0.5`, `single-pass` passed the complete M16 gate; and
- instructed the project to continue with WI-0038 rollout engineering.

The selected local pipeline is therefore:

- detector `centerface-2019-fp32` at SHA-256 `77e394b51108381b4c4f7b4baf1c64ca9f4aba73e5e803b2636419578913b5fe`;
- OpenCV DNN with a fresh native `Net` per image;
- confidence `0.5`;
- `single-pass`;
- direct RGB float32 input with scale `1.0`, zero mean and a maximum source long edge of `1600` before multiple-of-32 rounding;
- detector NMS `0.30` and top-K `5000`;
- SFace `sface-2021dec-fp32` using `sface-five-point-v1`; and
- padding `0.25` for the existing local inspection workflow.

The licence/training-data acceptance is explicitly for local evaluation. Proceeding with WI-0038 is an engineering instruction for this private local project; it does not broaden the licence conclusion or establish redistribution rights.

## Why rollout needs reconciliation

The canonical SQLite schema gives a face occurrence a stable identity and currently enforces a unique `(asset_revision_id, ordinal)` pair. Person labels and append-only review actions refer to that stable `face_occurrence_id`.

The local inspection worker, however, deterministically sorts the current detector result and writes each item using the resulting zero-based ordinal. When a different detector changes confidence ordering, misses an old face or finds an additional face, the same ordinal can refer to a different physical person. The current persistence path resolves an ordinal collision to the already-persisted occurrence.

Therefore **ordinal equality is not evidence of face identity during detector migration**. WI-0038 must reconcile geometry and five-point landmarks before any new detector result is allowed to reuse a reviewed occurrence.

## Delivery slices

### Slice 1 — rollout identity and conservative reconciliation foundation

The active branch introduces two reusable contracts before canonical persistence is changed:

1. `DetectorPipelineDefinition` produces a versioned SHA-256 identity over detector behaviour that can change the face population or geometry. The canonical representation includes implementation ID, exact detector model ID/hash, runtime, confidence, pipeline mode, resize policy, input dimensions/shape policy, colour order, data type, normalisation, detector NMS/top-K, tile/overlap/merge settings where applicable, and rotation policy.
2. `FaceDetectionReconciliationPlanner` compares old and new detections by normalized bounding-box IoU and all five landmarks. A persisted occurrence may be proposed for reuse only when the eligibility graph is one-to-one. A candidate with no eligible old occurrence is a new occurrence. Any many-to-one or one-to-many relation is `Ambiguous` and may not be auto-applied.

The worker-side pipeline identity factory freezes the currently implemented YuNet and CenterFace behaviour into that generic identity contract. This makes a future persistence migration auditable without making model SHA-256 stand in for preprocessing or threshold semantics.

### Slice 2 — persist rollout provenance and reconciliation state

Next, wire the versioned pipeline identity into durable run/face provenance and add persistence for reconciliation plans. The persistence boundary must:

- never remap an existing reviewed occurrence merely because its ordinal collides with the new result;
- preserve existing `people`, `person_labels`, `review_actions` and suggestion history;
- add a new occurrence for a genuinely new candidate;
- retain old occurrences that the selected detector no longer returns rather than deleting their history; and
- persist ambiguous cases without mutating identity state.

Schema changes must be forward-migrated and covered by SQLite integration tests.

### Slice 3 — explicit ambiguity/new-face review

Expose pending reconciliation cases to the local review workflow. The operator must be able to:

- confirm a proposed old-to-new face mapping;
- select the correct old occurrence when geometry is ambiguous;
- mark the candidate as a genuinely new occurrence; or
- leave the case deferred without changing existing assignments.

No detector score, embedding score or geometry score may assign a person automatically.

### Slice 4 — pilot migration and rollback verification

Before processing the full archive:

1. back up the canonical catalogue and generated-output roots;
2. run the selected pipeline on a disposable copy of the established pilot/catalogue scope;
3. apply only unambiguous reconciliations and complete the surfaced manual reconciliation queue;
4. verify that reviewed assignments/rejections and their append-only history still refer to the same physical faces;
5. verify genuinely new faces receive new occurrence IDs and enter normal review;
6. confirm the selected pipeline still behaves consistently with the accepted M16 decision; and
7. exercise rollback by discarding/restoring the pilot copy rather than deleting or rewriting historical review data in place.

Only after that verification may the full-archive local rollout be authorised.

## Invariants

- Detector migration is based on source revision plus geometry/landmarks, never ordinal alone.
- Existing reviewed occurrences are immutable identity anchors unless a human explicitly resolves an ambiguity.
- Existing people, labels, rejections and review history survive rollout unchanged.
- Newly detected faces receive new stable occurrence IDs.
- An old occurrence that is not returned by CenterFace is retained; rollout does not erase historical evidence.
- Ambiguity blocks automatic application for that candidate.
- Pipeline provenance distinguishes material detector/preprocessing/configuration changes even when the ONNX bytes are unchanged.
- Detailed private face geometry, filenames and migration decisions stay outside Git.

## Acceptance criteria

- [x] Detector-pipeline identity distinguishes materially different detection behaviour at the contract level.
- [ ] Pipeline identity is persisted with production/local-catalogue detector results and reconciliation evidence.
- [ ] Existing reviewed faces cannot silently change person because detection ordering changed.
- [ ] New faces are added without overwriting existing occurrences.
- [ ] Ambiguous reconciliation requires human review in the local application/workflow.
- [ ] Pilot reprocessing passes the accepted detector target and catalogue-history invariants.
- [ ] Operator documentation and an exercised procedure explain migration, rollback and evidence retention.

## Completion boundary

WI-0038 remains in progress until the selected CenterFace pipeline has been safely applied to a pilot copy of the canonical catalogue, all ambiguous cases are reviewable, identity history invariants have been verified, and rollback has been exercised.

Do **not** run a full-archive canonical migration merely because WI-0037 passed the detector gate. The selected detector is the engineering target for this local rollout work; the current ordinal-based persistence behaviour is not yet safe for detector replacement.
