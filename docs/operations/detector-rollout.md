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

PR #94 added the SQLite rollout-persistence boundary so an existing occurrence can be reused only when the persisted plan explicitly identifies it; new candidates receive new occurrence IDs; unmatched old occurrences are retained; and ambiguous candidates cannot be applied before human resolution.

PR #95 added durable candidate crop/embedding payload plus append-only human ambiguity decisions. Those decisions resolve face-occurrence identity only and never assign or change a person.

PR #96 added the operator-facing `rollout start`, `rollout resume`, `rollout status` and `rollout apply` workflow plus `/detector-rollout/{RUN_ID}` human reconciliation UI.

PR #97 fixed two issues found during the first disposable pilot:

- ordinary face review now resolves rollout crops stored under `rollouts/<run-id>/...`; and
- CLI `rollout-complete` requires successful revision processing rather than candidate counts alone.

The ordinary `batch start` path still has its historical deterministic ordinal semantics. **Never use `batch start` as a detector-migration command.**

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

## Full current-catalogue rollout

Use a fresh recoverable database copy. Do not mutate the untouched reviewed source database in place.

### 1. Update the repository and stop writers

After the WI-0038 completion/status PR is merged:

```powershell
Set-Location "C:\Kod\codex\Photo Identity Indexer"
git switch main
git pull --ff-only
```

Stop the review application and every process that can write to the catalogue before making the copy.

### 2. Choose permanent rollout paths

Set the source database to the untouched reviewed 560-image catalogue. Use a **permanent** output root for the full rollout because each processing run records its `outputRoot`, and ordinary face review resolves rollout crops through that recorded root. Do not move or rename that output directory after adopting the migrated database.

```powershell
$sourceDb = "C:\REPLACE\WITH\YOUR\560-IMAGE\catalogue.db"
$fullRoot = "C:\PhotoIdentity\CenterFace-current-catalogue"
$fullDb = Join-Path $fullRoot "catalogue-centerface.db"
$rolloutOutput = Join-Path $fullRoot "rollout-output"
$revisionCsv = Join-Path $fullRoot "current-revisions.csv"
$revisionFile = Join-Path $fullRoot "current-revisions.txt"
$reviewApp = Join-Path $fullRoot "review-app"

New-Item -ItemType Directory -Force -Path $fullRoot,$rolloutOutput | Out-Null
```

### 3. Copy the reviewed source database

With all source-database writers stopped:

```powershell
Copy-Item -LiteralPath $sourceDb -Destination $fullDb -Force

foreach ($suffix in @("-wal", "-shm")) {
    $sidecar = "$sourceDb$suffix"
    if (Test-Path -LiteralPath $sidecar) {
        Copy-Item -LiteralPath $sidecar -Destination "$fullDb$suffix" -Force
    }
}
```

Keep `$sourceDb` untouched as the restore point.

### 4. Build the explicit current-revision list

`rollout start` deliberately cannot scan a source directory. It requires explicit immutable `AssetRevisionId` values.

In DB Browser for SQLite, run this against the **source** catalogue:

```sql
WITH ranked AS (
    SELECT
        ar.id AS revision_id,
        ROW_NUMBER() OVER (
            PARTITION BY ar.asset_id
            ORDER BY ar.observed_at_utc DESC, ar.id DESC
        ) AS row_number
    FROM asset_revisions AS ar
    INNER JOIN assets AS a
        ON a.id = ar.asset_id
    WHERE a.deleted_at_utc IS NULL
)
SELECT revision_id
FROM ranked
WHERE row_number = 1
ORDER BY revision_id;
```

Export the result with the header `revision_id` to `$revisionCsv`, then convert it to the one-GUID-per-line rollout file:

```powershell
Import-Csv -LiteralPath $revisionCsv |
    Select-Object -ExpandProperty revision_id |
    Set-Content -LiteralPath $revisionFile -Encoding ascii

$revisionCount = @(
    Get-Content -LiteralPath $revisionFile |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
).Count

$revisionCount
```

For the currently reviewed catalogue, the expected scope is **560 current revisions**. If the count is not the number of active photos you intend to migrate, stop and inspect the query/database before starting rollout.

Do not commit the revision list.

### 5. Start the full current-catalogue rollout

Run only against `$fullDb`:

```powershell
Set-Location "C:\Kod\codex\Photo Identity Indexer"

dotnet run `
    --project .\src\PhotoIdentity.Cli `
    --configuration Release `
    -- `
    rollout start `
    --database $fullDb `
    --output $rolloutOutput `
    --revision-file $revisionFile
```

Save the printed run GUID:

```powershell
$runId = "REPLACE_WITH_PRINTED_RUN_ID"
```

Do not start another run merely because processing is interrupted.

### 6. Resume or inspect failures on the same run

Check status:

```powershell
dotnet run `
    --project .\src\PhotoIdentity.Cli `
    --configuration Release `
    -- `
    rollout status `
    --database $fullDb `
    --run $runId
```

If processing was interrupted, resume the same run:

```powershell
dotnet run `
    --project .\src\PhotoIdentity.Cli `
    --configuration Release `
    -- `
    rollout resume `
    --database $fullDb `
    --run $runId
```

Do not proceed to adoption while `revisions-failed` is non-zero.

### 7. Review every ambiguity

If `awaiting-review` is greater than zero, publish the review application against `$fullDb`:

```powershell
Set-Location "C:\Kod\codex\Photo Identity Indexer"

Remove-Item -LiteralPath $reviewApp -Recurse -Force -ErrorAction SilentlyContinue

dotnet publish `
    .\src\PhotoIdentity.Api `
    --configuration Release `
    --output $reviewApp

$env:PhotoIdentity__DatabasePath = $fullDb
Set-Location -LiteralPath $reviewApp

dotnet .\PhotoIdentity.Api.dll --urls "http://127.0.0.1:5080"
```

Open:

```text
http://127.0.0.1:5080/detector-rollout/REPLACE_WITH_RUN_ID
```

For each ambiguity choose only:

- the displayed eligible old occurrence when it is the same physical face;
- **new face occurrence** when the candidate is a real new detection; or
- **defer** when uncertain.

A deferred candidate keeps the rollout incomplete. The reconciliation UI never assigns a person.

Stop the review app before applying saved decisions.

### 8. Apply saved human decisions

```powershell
Set-Location "C:\Kod\codex\Photo Identity Indexer"

dotnet run `
    --project .\src\PhotoIdentity.Cli `
    --configuration Release `
    -- `
    rollout apply `
    --database $fullDb `
    --run $runId
```

Repeat status/review/apply until all of the following are true:

```text
revisions-failed: 0
awaiting-review: 0
ready-to-apply: 0
deferred: 0
rollout-complete: true
```

`unmatched-existing` may be greater than zero. Those old occurrences must be retained; their presence is not by itself a rollout failure.

### 9. Verify catalogue-history invariants before adoption

Compare `$sourceDb` and `$fullDb`:

```sql
SELECT COUNT(*) AS people FROM people;
SELECT COUNT(*) AS person_labels FROM person_labels;
SELECT COUNT(*) AS review_actions FROM review_actions;
SELECT COUNT(*) AS ranked_suggestions FROM identity_suggestion_rankings;
SELECT COUNT(*) AS face_occurrences FROM face_occurrences;
```

Expected behavior:

- people, person labels and review actions remain unchanged by rollout;
- existing ranking rows are not deleted by rollout;
- `face_occurrences` may increase because CenterFace adds genuinely new detections.

For new rollout candidates, verify they have no automatic person labels or review actions. Inspect reviewed existing mappings and every ambiguous human decision closely enough to confirm they still refer to the intended physical face. Inspect unmatched existing occurrences as well.

Do **not** run matcher regeneration until these migration checks are complete, because suggestion-table changes would make before/after rollout comparison noisier.

### 10. Verify replay safety

Record the migrated occurrence count:

```sql
SELECT COUNT(*) AS face_occurrences FROM face_occurrences;
```

Then run `rollout resume` and `rollout apply` again for the same `$runId`, followed by status. The face-occurrence count and applied occurrence IDs must remain stable. No duplicate new occurrences may appear.

### 11. Adopt the migrated catalogue

Only after the full current-catalogue run and replay checks pass:

1. stop all catalogue writers;
2. keep `$sourceDb` unchanged as the rollback database;
3. keep `$rolloutOutput` at its permanent recorded path;
4. point the local application at `$fullDb`, or copy the migrated database to the intended active database location while leaving `$rolloutOutput` unchanged; and
5. reopen ordinary face review and spot-check existing reviewed people plus new unreviewed faces.

Do not delete or rewrite old occurrences to make the migrated catalogue look cleaner.

### 12. Regenerate identity suggestions only after adoption verification

New CenterFace occurrences intentionally have no ranked identity suggestions until matcher regeneration is run. After migration/adoption checks are complete, regenerate suggestions for the exact SFace FP32 model if desired:

```powershell
Set-Location "C:\Kod\codex\Photo Identity Indexer"

dotnet run `
    --project .\src\PhotoIdentity.Cli `
    --configuration Release `
    -- `
    match regenerate `
    --database $fullDb `
    --embedder-id sface-2021dec-fp32 `
    --embedder-hash 0ba9fbfa01b5270c96627c4ef784da859931e02f04419c829e83484087c34e79
```

Matcher suggestions remain suggestions only. They do not create person assignments automatically.

## Rollback

Rollback is restore-based, never historical-data rewriting.

If the full current-catalogue migration reveals a problem:

1. stop the review application and all writers;
2. preserve the failed migrated database/output privately if useful for diagnosis;
3. point the application back to the untouched `$sourceDb` or restore it to the active catalogue location; and
4. verify the original reviewed state opens intact.

Never repair a detector migration by bulk relabelling people, deleting unmatched old occurrences, or rewriting historical review actions in place.

## Scope boundary

The successful WI-0038 pilot and this procedure authorize migration of the **current reviewed 560-image local catalogue** to the selected CenterFace detector pipeline.

They do not authorize M12 full-archive processing, cloud/archive expansion, model redistribution, or automatic identity assignment. Those remain separate governed decisions.
