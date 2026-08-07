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
- the legacy `SqliteFaceCatalogueRepository.SaveInspectionAsync` path remains unchanged and is not an allowed detector-migration entry point.

The migration and repository behavior are covered by SQLite integration tests, including version-7-to-8 schema upgrade, explicit old-face reuse, non-ordinal new-face allocation and ambiguity refusal. The existing historical migration fixtures are also kept forward-compatible with schema version 8.

### Slice 3A — durable candidate payload and human resolution state

PR #95 establishes the persistence contract needed to review and resume ambiguous candidates without re-running detector/alignment/embedding inference:

- schema version `9` stores the exact candidate detector identity, confidence, aligned-crop provenance, SFace model identity and embedding vector before any canonical face occurrence is mutated;
- a persisted candidate payload must exactly match the geometry and five landmarks already recorded in the reconciliation plan;
- the candidate detector model/hash must match the processing run's registered detector-pipeline provenance;
- ambiguous decisions are append-only human actions with one of three meanings: select an eligible existing occurrence, mark the candidate as genuinely new, or defer the decision;
- selecting an existing occurrence is accepted only when it is one of the planner-persisted ambiguity options and belongs to the same immutable asset revision;
- human resolution is rejected for already-unambiguous candidates and for candidates that have already been applied; and
- repeated identical resolution requests are idempotent, while a later changed decision is preserved as another history row rather than rewriting earlier evidence.

These human actions resolve **face-occurrence identity only**. They never create or change a person label, and no detector, embedding or geometry score is allowed to assign a person automatically.

### Slice 3B — rollout orchestration and local reconciliation review

PR #96 implements the operator-facing rollout path on top of the Slice 1–3A contracts:

- `rollout start` accepts only explicitly named immutable asset revision IDs, either repeated with `--revision` or supplied one per line with `--revision-file`; it does not scan a source folder;
- the rollout worker fixes the governed detector choice to CenterFace confidence `0.5`, `single-pass`, with SFace alignment/embedding, so threshold/model drift cannot be introduced through command-line options;
- one durable processing job is created for each explicitly scoped immutable revision and may be resumed through `rollout resume`;
- a persisted plan is reused on retry instead of replanning against a catalogue that may already contain some safely applied candidates;
- **every candidate's aligned crop and embedding payload is persisted before any unambiguous candidate is applied**;
- unambiguous one-to-one and new-face decisions are applied automatically only through the rollout orchestration boundary;
- ambiguous candidates remain unapplied and are shown at `/detector-rollout/{RUN_ID}` with candidate/source geometry and only the planner-persisted old-face options;
- the operator can select one eligible old occurrence, mark the candidate as genuinely new, or defer; no arbitrary face or person can be selected;
- review and catalogue mutation are separate operations: `rollout apply` consumes the latest saved human decision and the persisted candidate payload without re-running detector/alignment/embedding inference;
- a reviewed new-face application allocates beyond the existing ordinal range and is replay-safe if execution stops after face persistence but before the candidate applied marker;
- `rollout status` distinguishes awaiting-review, ready-to-apply and deferred candidates and does not consider a deferred case complete; and
- rollout status/review rejects ordinary batch runs without registered rollout pipeline provenance, while resume rejects the legacy batch configuration shape.

The browser workflow resolves occurrence identity only. Person labels, rejections, assignments and append-only review history remain outside reconciliation and are not modified automatically.

PR #96 does **not** authorize full-archive rollout. Once merged, it authorizes only the disposable pilot in Slice 4, using the selected CenterFace pipeline and an explicitly scoped revision list.

### Slice 4 — pilot migration and rollback verification

After Slice 3B is merged:

1. stop writers and make a recoverable copy of the canonical catalogue before testing;
2. run the selected pipeline on a disposable database copy and a deliberately limited set of current immutable revisions;
3. inspect every surfaced ambiguous mapping in the local rollout workspace;
4. run `rollout apply` only after those decisions are saved and leave any uncertain case deferred;
5. verify reviewed assignments/rejections and append-only history still refer to the same physical faces;
6. verify genuinely new faces receive new occurrence IDs and enter ordinary face review;
7. repeat status/resume/apply as appropriate and confirm no duplicate new occurrences are introduced;
8. confirm the selected detector still behaves consistently with the accepted M16 decision; and
9. exercise rollback by discarding/restoring the pilot copy rather than deleting or rewriting historical review data in place.

Only after that human-verified pilot may the full-archive local rollout be authorised.

## Invariants

- Detector migration is based on source revision plus geometry/landmarks, never ordinal alone.
- Existing reviewed occurrences are immutable identity anchors unless a human explicitly resolves an ambiguity.
- Existing people, labels, rejections and review history survive rollout unchanged.
- Newly detected faces receive new stable occurrence IDs.
- An old occurrence that is not returned by CenterFace is retained; rollout does not erase historical evidence.
- Ambiguity blocks automatic application for that candidate.
- Human reconciliation history is append-only and does not assign a person.
- Candidate detector/crop/embedding payload is persisted before canonical mutation so reviewed decisions are resumable.
- Pipeline provenance distinguishes material detector/preprocessing/configuration changes even when the ONNX bytes are unchanged.
- A deferred ambiguity remains incomplete.
- Ordinary batch runs are not valid detector-rollout runs.
- Detailed private face geometry, filenames and migration decisions stay outside Git.

## Acceptance criteria

- [x] Detector-pipeline identity distinguishes materially different detection behaviour at the contract level.
- [x] A dedicated persistence boundary records pipeline identity with rollout detector results and durable reconciliation evidence.
- [x] Ambiguous candidates can retain exact candidate inspection payload and append-only human resolution evidence without mutating person labels.
- [x] The dedicated detector-migration execution path is routed through reconciliation so existing reviewed faces cannot silently change person because detection ordering changed.
- [x] New faces are surfaced and reviewable without overwriting existing occurrences.
- [x] Ambiguous reconciliation is operable through the local application/workflow and can be resumed safely.
- [ ] Pilot reprocessing passes the accepted detector target and catalogue-history invariants.
- [ ] Operator documentation and an exercised procedure explain migration, rollback and evidence retention.

## Completion boundary

WI-0038 remains in progress until the selected CenterFace pipeline has been safely applied to a pilot copy of the canonical catalogue, all ambiguous cases are reviewed or explicitly deferred, identity-history invariants have been verified, and rollback has been exercised.

Do **not** run a full-archive canonical migration merely because WI-0037 passed the detector gate or Slice 3B exists. The selected detector is the engineering target for this local rollout work; the legacy ordinal-based processing path is not a safe detector-replacement mechanism.
