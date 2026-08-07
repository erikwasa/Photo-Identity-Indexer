# Detector pipeline rollout

Use this runbook for WI-0038 after a detector candidate has passed the complete M16 evaluation gate. The selected local candidate is CenterFace confidence `0.5`, `single-pass`.

This procedure is intentionally more conservative than starting another batch run. Detector replacement can change face count and detection ordering, while canonical person assignments and review actions are attached to stable face occurrence IDs.

## Selected local pipeline

The selected M16 pipeline is fixed as:

- detector `centerface-2019-fp32`;
- detector SHA-256 `77e394b51108381b4c4f7b4baf1c64ca9f4aba73e5e803b2636419578913b5fe`;
- runtime `opencv-dnn` with a fresh native network per image;
- confidence `0.5`;
- pipeline `single-pass`;
- RGB float32 scale `1.0`, zero mean;
- source long edge bounded to `1600` before multiple-of-32 rounding;
- CenterFace NMS `0.30` and top-K `5000`;
- five-point landmark mapping into `sface-five-point-v1`;
- SFace `sface-2021dec-fp32`; and
- padding `0.25` in the local inspection workflow.

Changing any material detector/preprocessing parameter creates a different pipeline identity and requires a new governed decision.

The maintainer accepted the documented CenterFace model-weight/training-data uncertainty for local evaluation on 2026-08-07 and separately instructed WI-0038 rollout engineering to proceed. Redistribution remains outside that acceptance.

## Current implementation boundary

PR #93 added the versioned pipeline identity and conservative geometry/landmark reconciliation planner.

PR #94 added the SQLite rollout-persistence boundary:

- detector pipeline definitions and hashes can be bound to a processing run;
- reconciliation plans retain candidate geometry, dispositions, old-face options and unmatched old occurrences;
- unambiguous existing-face mappings can reuse only the occurrence explicitly named by the persisted plan;
- new candidates receive new occurrence IDs and ordinals above the existing range rather than candidate ordinals; and
- ambiguous candidates are persisted but cannot be applied before human resolution.

PR #95 adds the durable review state required before orchestration can be made resumable:

- the exact candidate detector identity/confidence, aligned-crop provenance, SFace model identity and embedding vector are persisted before canonical face mutation;
- candidate payload geometry must exactly match the already-persisted reconciliation plan;
- ambiguous human decisions are append-only actions to select one eligible existing occurrence, mark the candidate as new, or defer;
- an existing-face selection is valid only when the planner recorded that occurrence as an ambiguity option on the same asset revision; and
- these actions resolve face-occurrence identity only and never assign or change a person label.

The ordinary `batch start` processing path still writes through the legacy ordinal-based inspection repository. **Do not use `batch start` as a detector-migration command.** The rollout planner/repositories are not yet wired into the operator-facing planning/apply command and ambiguity-review UI.

Therefore there is currently **no local pilot command to run**. Pilot execution becomes available only after WI-0038 Slice 3B orchestration/review is implemented and merged.

## Why an ordinary rerun is unsafe

Existing face occurrences are unique by asset revision and ordinal. The legacy inspection worker sorts each current detector result and uses that sort position as the ordinal. A new detector can therefore cause ordinal `0` to refer to a different physical face than ordinal `0` from an earlier detector run.

A successful detector-quality comparison does not make ordinal identity safe. Detector migration must use the dedicated reconciliation boundary rather than the ordinary inspection writer.

## Reconciliation rule

For a fixed immutable source revision, compare the selected detector's candidates to the existing persisted detections using normalized geometry and all five landmarks.

The rollout planner applies these rules:

- one candidate eligible for exactly one old occurrence, and that old occurrence eligible for exactly that one candidate: **proposed existing occurrence**;
- candidate with no eligible old occurrence: **new occurrence**;
- candidate or old occurrence participating in a many-to-one or one-to-many eligible relation: **ambiguous**.

Eligibility currently requires both:

- bounding-box IoU of at least `0.30`; and
- mean five-landmark distance no greater than `0.20` of the average old/new box diagonal.

These are conservative migration defaults, not detector-quality thresholds. They must be validated on the pilot before full rollout. Changing them is a reconciliation-policy change and should be retained with migration evidence.

## Durable planning and review rule

The future rollout command must make planning durable before it changes canonical face occurrences:

1. register the exact detector-pipeline hash for the rollout processing run;
2. run CenterFace only against explicitly scoped immutable revision IDs;
3. persist the geometry/landmark reconciliation plan;
4. persist the exact candidate aligned crop and embedding payload;
5. auto-apply only unambiguous existing/new decisions through the rollout repository;
6. leave ambiguous candidates unapplied until a human records an existing/new decision; and
7. apply a reviewed ambiguous candidate from its persisted payload, not by re-running inference.

A `defer` resolution records that the operator deliberately left a candidate unresolved. It does not permit the rollout to treat that candidate as complete.

## Required invariants

A migration implementation and pilot must demonstrate all of the following:

1. No existing reviewed occurrence is reused solely because the new detection has the same ordinal.
2. Existing people and `person_labels` remain attached to the same physical face.
3. Existing rejection/assignment/undo history remains append-only and points to the same occurrence.
4. Genuinely new CenterFace detections receive new occurrence IDs.
5. Existing occurrences that CenterFace does not return are retained rather than deleted.
6. Ambiguous mappings are persisted as pending review and do not mutate identity state until explicitly resolved.
7. Detector pipeline provenance contains the versioned pipeline hash as well as the exact model hash.
8. Candidate detector/crop/embedding payload is durable before canonical mutation so review can be resumed without inference replay.
9. Human reconciliation history is append-only.
10. Rollout does not write automatic person labels from detector, embedding or geometry scores.

## Pilot preparation

The following steps are **future operator steps**, after Slice 3B is merged. Do not perform them yet.

1. Stop the review application and any worker writing to the catalogue.
2. Back up the canonical SQLite database and generated-output root together.
3. Create a disposable pilot copy of the database and output root.
4. Record the source database hash or backup identity, repository commit and selected detector-pipeline hash in the private rollout log.
5. Use the same source bytes as the copied catalogue; rescan only when source presence/revision checks require it.

Private filenames, geometry and reconciliation details stay outside Git.

## Pilot execution

Once Slice 3B orchestration and reconciliation review are implemented:

1. run reconciliation planning against the pilot copy before applying mutations;
2. confirm there are no ordinal-only remaps in the plan;
3. apply only one-to-one proposed mappings automatically;
4. create new occurrences only for candidates classified or explicitly resolved as new;
5. review every ambiguous case before considering the pilot complete;
6. resume the same rollout run and verify reviewed candidates apply from durable payload without duplicate new occurrences;
7. run ordinary face review for newly added faces where needed;
8. verify person counts, active person labels and review-action history before and after migration;
9. spot-check source photos where face count changed, especially group/small/profile/occluded cases; and
10. confirm the selected detector still shows behavior consistent with the accepted M16 result; the rollout pilot is not a new threshold-tuning exercise.

## Rollback

Rollback is restore-based, not destructive history rewriting.

If the pilot exposes incorrect mappings, unexpected identity-history changes, unusable review volume or other material problems:

1. stop writes to the pilot database;
2. retain the failed pilot copy and private reconciliation report as diagnostic evidence if useful;
3. restore or recreate the pilot from the pre-rollout backup;
4. correct the rollout implementation/policy; and
5. rerun the pilot from the clean copy.

Do not attempt rollback by deleting selected `face_occurrences`, relabelling people in bulk or editing old review actions in place.

## Full-archive authorization

Full-archive canonical rollout remains blocked until:

- pipeline identity is persisted with results;
- all detector-migration execution is routed through reconciliation without ordinal-only identity reuse;
- candidate payloads and human reconciliation decisions are durable;
- ambiguous cases have an operable human review path;
- a pilot migration preserves assignment/rejection/history invariants;
- genuinely new faces enter normal review without overwriting old occurrences; and
- backup and restore have been exercised successfully.

After those gates pass, record privacy-safe aggregate migration evidence in WI-0038 and retain detailed reconciliation evidence privately.
