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

The local inspection worker, however, deterministically sorts the current detector result and writes each item using the resulting zero-based ordinal. When a different detector changes confidence ordering, misses an old face or finds an additional face, the same ordinal can refer to a different physical person. The legacy persistence path resolves an ordinal collision to the already-persisted occurrence.

Therefore **ordinal equality is not evidence of face identity during detector migration**. WI-0038 uses a separate rollout persistence boundary that requires explicit reconciliation before a new detector result may reuse a reviewed occurrence. The ordinary batch writer remains unchanged for its existing deterministic resume semantics and must not be used for detector migration.

## Delivery slices

### Slice 1 — rollout identity and conservative reconciliation foundation

PR #93 established two reusable contracts before canonical persistence was changed:

1. `DetectorPipelineDefinition` produces a versioned SHA-256 identity over detector behaviour that can change the face population or geometry. The canonical representation includes implementation ID, exact detector model ID/hash, runtime, confidence, pipeline mode, resize policy, input dimensions/shape policy, colour order, data type, normalisation, detector NMS/top-K, tile/overlap/merge settings where applicable, and rotation policy.
2. `FaceDetectionReconciliationPlanner` compares old and new detections by normalized bounding-box IoU and all five landmarks. A persisted occurrence may be proposed for reuse only when the eligibility graph is one-to-one. A candidate with no eligible old occurrence is a new occurrence. Any many-to-one or one-to-many relation is `Ambiguous` and may not be auto-applied.

The worker-side pipeline identity factory freezes the currently implemented YuNet and CenterFace behaviour into that generic identity contract. This makes the persistence migration auditable without making model SHA-256 stand in for preprocessing or threshold semantics.

### Slice 2 — persist rollout provenance and reconciliation state

PR #94 implements the dedicated SQLite persistence boundary for detector migration:

- schema version `8` records exact detector-pipeline definitions and binds one pipeline hash to a processing run;
- reconciliation plans persist candidate geometry and landmarks, `ExistingOccurrence` / `NewOccurrence` / `Ambiguous` disposition, possible old occurrence IDs, and old occurrences with no candidate;
- rollout-written detector observations retain the detector-pipeline hash in addition to the exact detector model identity;
- an existing occurrence is reused only when the persisted reconciliation plan explicitly names that occurrence;
- a genuinely new face receives a new stable occurrence ID and an ordinal above the existing range rather than inheriting candidate ordering;
- old occurrences with no CenterFace candidate are retained and never deleted by reconciliation;
- an ambiguous candidate throws before catalogue mutation and is left for Slice 3 human resolution; and
- the legacy `SqliteFaceCatalogueRepository.SaveInspectionAsync` path remains unchanged and is not an allowed detector-migration boundary.

The migration and repository behavior are covered by SQLite integration tests, including version-7-to-8 schema upgrade, explicit old-face reuse, non-ordinal new-face allocation and ambiguity refusal. The existing historical migration fixtures are also kept forward-compatible with schema version 8.

This slice deliberately does **not** yet provide an operator command that runs CenterFace against a copied catalogue. A safe pilot requires orchestration that produces a reconciliation plan first and routes every candidate through this rollout boundary.

### Slice 3 — explicit ambiguity/new-face review and rollout orchestration

Next, connect the selected CenterFace pipeline to the rollout planner and expose pending reconciliation cases to the local review workflow. The operator must be able to:

- inspect the old and candidate geometry on the same source revision;
- confirm a proposed old-to-new face mapping;
- select the correct old occurrence when geometry is ambiguous;
- mark the candidate as a genuinely new occurrence; or
- leave the case deferred without changing existing assignments.

The orchestration path must register the exact pipeline hash, persist the plan before applying candidate results, apply only unambiguous decisions automatically, and resume without creating duplicate new occurrences. No detector score, embedding score or geometry score may assign a person automatically.

Until this slice is implemented and tested, **do not run a CenterFace migration against either the canonical catalogue or a pilot copy**. The existing `batch start` command still uses the legacy ordinal-based writer.

### Slice 4 — pilot migration and rollback verification

After Slice 3 is merged:

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
- [x] A dedicated persistence boundary records pipeline identity with rollout detector results and durable reconciliation evidence.
- [ ] All detector-migration execution is routed through reconciliation so existing reviewed faces cannot silently change person because detection ordering changed.
- [ ] New faces are surfaced and reviewable without overwriting existing occurrences.
- [ ] Ambiguous reconciliation requires human review in the local application/workflow.
- [ ] Pilot reprocessing passes the accepted detector target and catalogue-history invariants.
- [ ] Operator documentation and an exercised procedure explain migration, rollback and evidence retention.

## Completion boundary

WI-0038 remains in progress until the selected CenterFace pipeline has been safely applied to a pilot copy of the canonical catalogue, all ambiguous cases are reviewable, identity history invariants have been verified, and rollback has been exercised.

Do **not** run a full-archive canonical migration merely because WI-0037 passed the detector gate. The selected detector is the engineering target for this local rollout work; the legacy ordinal-based processing path is not a safe detector-replacement mechanism.
