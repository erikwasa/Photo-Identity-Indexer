# Detector pipeline rollout

Use this runbook for detector migration after a candidate has passed the complete M16 evaluation gate. The selected local pipeline is CenterFace confidence `0.5`, `single-pass`.

Detector replacement can change face count and detection ordering while person assignments and review actions remain attached to stable face-occurrence IDs. The ordinary batch path therefore remains unsafe for detector migration.

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

The maintainer accepted the documented CenterFace model-weight/training-data uncertainty for **local evaluation** on 2026-08-07 and separately instructed WI-0038 rollout engineering to proceed. Redistribution remains outside that acceptance.

## Implementation boundary

PR #93 added versioned pipeline identity and conservative geometry/landmark reconciliation.

PR #94 added the rollout-persistence boundary. Its invariant is that an existing occurrence can be reused only when the persisted plan explicitly identifies it; new candidates receive new occurrence IDs; unmatched old occurrences are retained; and ambiguous candidates cannot be applied before human resolution.

PR #95 added durable candidate crop/embedding payload plus append-only human ambiguity decisions. Those decisions resolve face-occurrence identity only and never assign or change a person.

PR #96 added the operator-facing `rollout start`, `rollout resume`, `rollout status` and `rollout apply` workflow plus `/detector-rollout/{RUN_ID}` human reconciliation UI.

PR #97 fixed two issues found during the first disposable pilot:

- ordinary face review now resolves rollout crops stored under `rollouts/<run-id>/...`; and
- CLI `rollout-complete` requires successful revision processing rather than candidate counts alone.

The ordinary `batch start` path still has its historical deterministic ordinal semantics. **Never use `batch start` as a detector-migration command.**

## PostgreSQL provider selection

All four rollout CLI actions require `--postgres-connection-env NAME`. The named environment variable contains the PostgreSQL connection string; keep credentials out of command arguments and shell history. The command initializes PostgreSQL migrations and uses PostgreSQL processing, revision lookup, reconciliation, review and application repositories throughout the rollout worker. It does not accept a database-path option or perform dual writes.

For example, after configuring the connection variable securely:

```powershell
dotnet run --project src/PhotoIdentity.Cli -- rollout status --postgres-connection-env PHOTOIDENTITY_ROLLOUT_CONNECTION --run RUN_ID
dotnet run --project src/PhotoIdentity.Cli -- rollout apply --postgres-connection-env PHOTOIDENTITY_ROLLOUT_CONNECTION --run RUN_ID
```

PostgreSQL is now the unconditional production authority. Use the canonical catalogue containing the run and its immutable revisions; the rollout command does not import another catalogue.

## Why ordinal migration is unsafe

Existing face occurrences are unique by asset revision and ordinal. The legacy inspection worker sorts the current detector result and uses that sort position as the ordinal. A different detector can therefore cause ordinal `0` to refer to a different physical face.

A successful detector-quality comparison does not make ordinal identity safe. Detector migration must use the dedicated reconciliation boundary.

## Reconciliation rule

For one immutable source revision, compare CenterFace candidates to persisted old detections using normalized geometry and all five landmarks.

The rollout planner uses these dispositions:

- one candidate eligible for exactly one old occurrence, and that old occurrence eligible for exactly that one candidate: **existing occurrence**;
- candidate with no eligible old occurrence: **new occurrence**;
- candidate or old occurrence participating in a many-to-one or one-to-many eligible relation: **ambiguous**.

Eligibility currently requires both:

- bounding-box IoU of at least `0.30`; and
- mean five-landmark distance no greater than `0.20` of the average old/new box diagonal.

These are migration-policy defaults, not detector-quality thresholds.

## Required invariants

A rollout must preserve all of the following:

1. Ordinal equality is never treated as physical-face identity evidence.
2. Existing people and `person_labels` stay attached to the same physical face.
3. Existing rejection/assignment/undo history remains append-only and points to the same stable occurrence.
4. Genuinely new CenterFace detections receive new occurrence IDs.
5. Existing occurrences that CenterFace does not return are retained.
6. Ambiguous mappings do not mutate catalogue identity state until explicitly resolved and applied.
7. Detector pipeline provenance contains the versioned pipeline hash and exact model hash.
8. Candidate detector/crop/embedding payload is durable before canonical mutation.
9. Human reconciliation history is append-only.
10. Rollout never writes automatic person labels from detector, embedding or geometry scores.
11. Repeated status/resume/apply does not create duplicate new occurrences.

## Completed disposable pilot

The WI-0038 migration-safety pilot passed on 2026-08-08 using rollout run `5794d5c5-26fe-45f4-8a70-3132aae45891` against a recoverable copy of the reviewed 560-image catalogue.

Privacy-safe aggregate evidence:

- 20/20 selected revisions succeeded;
- 77 candidates were applied: 43 existing-occurrence mappings and 34 new occurrences;
- 0 ambiguous candidates and 1 unmatched existing occurrence;
- all 43 reviewed existing mappings were inspected and confirmed correct, with 0 incorrect mappings;
- the 34 new occurrences had 0 person labels and 0 review actions;
- source and migrated pilot state remained 69 people, 454 person labels, 467 review actions and 10 ranked-suggestion rows;
- the unmatched old occurrence was inspected and retained;
- replay/resume/apply kept face occurrences stable at 492 before and after; and
- restore-based rollback succeeded.

Detailed filenames, geometry and identity decisions remain private.

This passed pilot authorizes a full rollout of the **current 560-image local catalogue** through the dedicated rollout path. It does **not** start M12 full-archive processing and does not broaden model licence or redistribution conclusions.

## Running a rollout

Before starting, stop catalogue writers and take a verified logical backup as described in [PostgreSQL production operations](postgresql-operations.md). That backup is the rollback point.

`rollout start` cannot scan a source directory. It needs explicit immutable `AssetRevisionId` values (`--revision ID` or a one-GUID-per-line `--revision-file`). Do not commit the revision list. Use a permanent `--output` directory: each processing run records its output root, and face review finds rollout crops there.

```powershell
dotnet run --project src/PhotoIdentity.Cli -- rollout start --postgres-connection-env PHOTOIDENTITY_ROLLOUT_CONNECTION --output OUTPUT_DIR --revision-file REVISIONS.txt
dotnet run --project src/PhotoIdentity.Cli -- rollout status --postgres-connection-env PHOTOIDENTITY_ROLLOUT_CONNECTION --run RUN_ID
dotnet run --project src/PhotoIdentity.Cli -- rollout resume --postgres-connection-env PHOTOIDENTITY_ROLLOUT_CONNECTION --run RUN_ID
```

Resume the same run after an interruption; do not start another run. If `awaiting-review` is greater than zero, open `/detector-rollout/RUN_ID` in the normal application. For each ambiguity choose the eligible old occurrence when it is the same physical face, **new face occurrence** for a real new detection, or **defer** when uncertain. The reconciliation UI never assigns a person. Then run `rollout apply` for the same run. Repeat status/review/apply until:

```text
revisions-failed: 0
awaiting-review: 0
ready-to-apply: 0
deferred: 0
rollout-complete: true
```

`unmatched-existing` may be greater than zero; those old occurrences are retained. Running `rollout resume` and `rollout apply` again must not change the face-occurrence count or create duplicate occurrences.

## Rollback

Rollback restores the PostgreSQL backup taken before the rollout. Never repair a detector migration by bulk relabelling people, deleting unmatched old occurrences, or rewriting historical review actions in place.

## Scope boundary

The successful WI-0038 pilot and this procedure authorize migration of the **current reviewed 560-image local catalogue** to the selected CenterFace detector pipeline.

They do not authorize M12 full-archive processing, cloud/archive expansion, model redistribution, or automatic identity assignment. Those remain separate governed decisions.
