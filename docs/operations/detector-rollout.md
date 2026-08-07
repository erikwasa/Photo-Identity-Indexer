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

## Implementation boundary

PR #93 added the versioned pipeline identity and conservative geometry/landmark reconciliation planner.

PR #94 added the SQLite rollout-persistence boundary:

- detector pipeline definitions and hashes can be bound to a processing run;
- reconciliation plans retain candidate geometry, dispositions, old-face options and unmatched old occurrences;
- unambiguous existing-face mappings can reuse only the occurrence explicitly named by the persisted plan;
- new candidates receive new occurrence IDs and ordinals above the existing range rather than candidate ordinals; and
- ambiguous candidates are persisted but cannot be applied before human resolution.

PR #95 added durable candidate payload and append-only human resolution state:

- the exact candidate detector identity/confidence, aligned-crop provenance, SFace model identity and embedding vector are persisted before canonical face mutation;
- candidate payload geometry must exactly match the already-persisted reconciliation plan;
- ambiguous human decisions select one eligible existing occurrence, mark the candidate as new, or defer;
- an existing-face selection is valid only when the planner recorded that occurrence as an ambiguity option on the same asset revision; and
- these actions resolve face-occurrence identity only and never assign or change a person label.

PR #96 adds the operator-facing Slice 3B workflow:

- `rollout start` processes only explicitly supplied immutable asset revision IDs and never scans a source root;
- CenterFace confidence `0.5`, `single-pass` is fixed by the rollout worker and cannot be replaced by command-line detector/threshold options;
- each revision uses the durable job engine and can be resumed;
- a reconciliation plan is persisted before mutation and reused on retry;
- every candidate aligned crop and embedding payload is durable before any unambiguous candidate is applied;
- the local `/detector-rollout/{RUN_ID}` workspace exposes only planner-produced old-face choices plus `new` and `defer`;
- review decisions do not mutate the catalogue until the separate `rollout apply` command is run; and
- reviewed ambiguous candidates are applied from the durable payload without inference replay, with replay-safe new-occurrence recovery.

The ordinary `batch start` processing path still has its historical deterministic ordinal semantics. **Do not use `batch start` as a detector-migration command.**

PR #96 enables only the disposable pilot described below after it is merged. It does not authorize a canonical full-archive migration.

## Why an ordinary rerun is unsafe

Existing face occurrences are unique by asset revision and ordinal. The legacy inspection worker sorts each current detector result and uses that sort position as the ordinal. A new detector can therefore cause ordinal `0` to refer to a different physical face than ordinal `0` from an earlier detector run.

A successful detector-quality comparison does not make ordinal identity safe. Detector migration must use the dedicated reconciliation boundary rather than the ordinary inspection entry point.

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

The rollout workflow makes planning durable before it changes canonical face occurrences:

1. create a processing run for an explicit set of immutable revision IDs;
2. register the exact detector-pipeline hash;
3. run the fixed CenterFace pipeline for a revision;
4. persist the geometry/landmark reconciliation plan;
5. persist every candidate aligned crop and embedding payload;
6. auto-apply only unambiguous existing/new decisions through the rollout path;
7. leave ambiguous candidates unapplied until a human records an existing/new decision; and
8. apply a reviewed ambiguous candidate from its persisted payload, not by re-running inference.

A `defer` resolution records that the operator deliberately left a candidate unresolved. It does not permit the rollout to treat that candidate as complete.

## Required invariants

A pilot must demonstrate all of the following:

1. No existing reviewed occurrence is reused solely because the new detection has the same ordinal.
2. Existing people and `person_labels` remain attached to the same physical face.
3. Existing rejection/assignment/undo history remains append-only and points to the same occurrence.
4. Genuinely new CenterFace detections receive new occurrence IDs.
5. Existing occurrences that CenterFace does not return are retained rather than deleted.
6. Ambiguous mappings do not mutate identity state until explicitly resolved and applied.
7. Detector pipeline provenance contains the versioned pipeline hash as well as the exact model hash.
8. Candidate detector/crop/embedding payload is durable before canonical mutation so review can be resumed without inference replay.
9. Human reconciliation history is append-only.
10. Rollout does not write automatic person labels from detector, embedding or geometry scores.
11. Re-running status/resume/apply does not create duplicate new occurrences.

## Disposable pilot preparation

Do this only after PR #96 is merged and local `main` contains it.

Use a deliberately limited pilot scope, not the whole archive. Prefer roughly 10–30 current revisions that exercise already-reviewed assignments, group photos, face-count changes and at least a few difficult small/profile/occluded cases. Detailed filenames and selected revision IDs stay private.

Stop the review application and all catalogue writers before copying the database. Then prepare a disposable working area. Replace the two canonical paths with the paths used on the local machine:

```powershell
Set-Location "C:\Kod\codex\Photo Identity Indexer"

git switch main
git pull --ff-only

$canonicalDb = "C:\PhotoIdentity\catalogue.db"       # replace if different
$pilotRoot = "C:\PhotoIdentity\WI-0038-pilot"
$pilotDb = Join-Path $pilotRoot "catalogue-pilot.db"
$rolloutOutput = Join-Path $pilotRoot "rollout-output"
$revisionFile = Join-Path $pilotRoot "pilot-revisions.txt"
$reviewApp = Join-Path $pilotRoot "review-app"

New-Item -ItemType Directory -Force -Path $pilotRoot,$rolloutOutput | Out-Null
Copy-Item -LiteralPath $canonicalDb -Destination $pilotDb -Force
foreach ($suffix in @("-wal", "-shm")) {
    $sourceSidecar = "$canonicalDb$suffix"
    if (Test-Path -LiteralPath $sourceSidecar) {
        Copy-Item -LiteralPath $sourceSidecar -Destination "$pilotDb$suffix" -Force
    }
}
```

The copied catalogue retains the original local-folder source roots and historical crop-output roots. During the pilot those are read-only inputs. New CenterFace rollout crops are written only under `$rolloutOutput`.

### Prepare the explicit revision list

Create `$revisionFile` with one current `AssetRevisionId` GUID per line. Blank lines and lines beginning with `#` are ignored. The list is deliberately explicit so the rollout command cannot silently expand to a source folder or the whole archive.

Revision IDs are available from the local catalogue/collection API (`RevisionId` in collection responses/manifests) or other local catalogue inspection tooling. A practical pilot is a small set of photos you already know contain reviewed people and challenging face layouts. Do not commit this file.

Example file shape:

```text
# WI-0038 private pilot revisions
00000000-0000-0000-0000-000000000001
00000000-0000-0000-0000-000000000002
```

Replace those example values with real current revision IDs before running anything.

## Run the pilot planning/application pass

Start the rollout against the **pilot database only**:

```powershell
Set-Location "C:\Kod\codex\Photo Identity Indexer"

dotnet run `
    --project .\src\PhotoIdentity.Cli `
    --configuration Release `
    -- `
    rollout start `
    --database $pilotDb `
    --output $rolloutOutput `
    --revision-file $revisionFile
```

The command prints a rollout `run:` GUID and counts including `ambiguous`, `awaiting-review`, `ready-to-apply`, `deferred`, and `unmatched-existing`. Keep that GUID:

```powershell
$runId = "REPLACE_WITH_PRINTED_RUN_ID"
```

If processing was interrupted or deliberately limited, resume the **same** run rather than starting another one:

```powershell
dotnet run `
    --project .\src\PhotoIdentity.Cli `
    --configuration Release `
    -- `
    rollout resume `
    --database $pilotDb `
    --run $runId
```

Check status at any point:

```powershell
dotnet run `
    --project .\src\PhotoIdentity.Cli `
    --configuration Release `
    -- `
    rollout status `
    --database $pilotDb `
    --run $runId
```

## Review ambiguous occurrence mappings

Publish and run the current local review application against the pilot database:

```powershell
Set-Location "C:\Kod\codex\Photo Identity Indexer"

Remove-Item -LiteralPath $reviewApp -Recurse -Force -ErrorAction SilentlyContinue
dotnet publish .\src\PhotoIdentity.Api --configuration Release --output $reviewApp

$env:PhotoIdentity__DatabasePath = $pilotDb
Set-Location -LiteralPath $reviewApp
dotnet .\PhotoIdentity.Api.dll --urls "http://127.0.0.1:5080"
```

Open:

```text
http://127.0.0.1:5080/detector-rollout/REPLACE_WITH_RUN_ID
```

For every ambiguous candidate, inspect the source overlay and candidate/old crops. Choose exactly one of:

- an eligible old occurrence shown by the page, when it is the same physical face;
- **new face occurrence**, when CenterFace found a legitimate face that has no old occurrence; or
- **defer**, when the mapping is not safe to decide yet.

A deferred candidate intentionally keeps the rollout incomplete. The page never assigns a person.

Stop the review app before the terminal apply step.

## Apply saved human decisions

Apply the latest non-deferred decisions from their already-persisted candidate payloads:

```powershell
Set-Location "C:\Kod\codex\Photo Identity Indexer"

dotnet run `
    --project .\src\PhotoIdentity.Cli `
    --configuration Release `
    -- `
    rollout apply `
    --database $pilotDb `
    --run $runId
```

Run `rollout status` again. A pilot is not complete while `awaiting-review`, `ready-to-apply`, or `deferred` is non-zero, or while revision processing has failures.

It is safe and expected to repeat `rollout status`, `rollout resume`, and `rollout apply` for the same run. The pilot must verify that doing so does not duplicate newly created face occurrences.

## Human pilot verification

Before accepting the pilot, verify privately:

1. Spot-check every ambiguous mapping you resolved.
2. Spot-check photos where the CenterFace face count differs from the old detector.
3. Confirm existing assigned/rejected faces still refer to the same physical person/face.
4. Confirm existing review-action history remains intact and append-only.
5. Confirm unmatched old occurrences remain present.
6. Confirm genuinely new CenterFace faces have new occurrence IDs and appear in ordinary face review as unreviewed unless a separate human review action assigns/rejects them.
7. Confirm the rollout UI did not create person labels.
8. Repeat `rollout apply` and confirm counts/occurrence IDs remain stable.
9. Record privacy-safe aggregate results and any ambiguous/deferred counts for WI-0038; keep filenames, face geometry and individual decisions private.

The rollout pilot is a migration-safety exercise, not a new detector-threshold experiment. Do not change the selected CenterFace settings to make the pilot look better.

## Rollback exercise

Rollback is restore-based, not destructive history rewriting.

For the required pilot rollback exercise:

1. stop the review application and all pilot writers;
2. retain the completed/failed pilot database and rollout output privately if they are useful evidence;
3. delete or move the disposable pilot copy;
4. recreate `$pilotDb` from the same pre-rollout canonical backup/copy; and
5. confirm the restored copy opens and contains the original reviewed state without relying on deletion of selected `face_occurrences` or edits to historical review actions.

If the pilot exposed an implementation/policy problem, fix it and rerun from a fresh database copy. Never repair a bad migration by bulk relabelling people or rewriting old review history in place.

## Full-archive authorization

Full-archive canonical rollout remains blocked until the maintainer reports that the disposable pilot:

- preserved assignment/rejection/history invariants;
- made every ambiguity reviewable and safely resumable;
- added genuinely new faces without overwriting old occurrences;
- remained replay-safe under repeated resume/apply;
- produced acceptable private operator workload; and
- passed the restore-based rollback exercise.

After those gates pass, record privacy-safe aggregate migration evidence in WI-0038 and only then prepare a separate full-archive authorization step. Detailed reconciliation evidence stays private.
