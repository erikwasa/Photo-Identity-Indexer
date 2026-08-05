# Detector comparison runs

Use this procedure only after the WI-0039 comparison slice has been merged and the completed confidence-0.9 baseline session is still available in the private detector-evaluation root.

Do not process confidence `0.8`, `0.7`, `0.6` or `0.5` before the comparison slice is available. Each candidate must use an isolated catalogue and the unchanged 100-photo evaluation set.

## Invariants

Keep these inputs unchanged for every candidate:

- the exact 100 staged filenames;
- the source bytes for every photo;
- the private manifest metadata and countable-face rule;
- the frozen confidence-0.9 face-level ground truth;
- the IoU threshold, which defaults to `0.50`; and
- the detector model and preprocessing configuration except for the confidence value being evaluated.

The comparison API verifies the complete filename set and full SHA-256 revision hash for every source photo. A changed, missing, extra or duplicate source prevents comparison creation.

## Step 1: freeze the baseline before switching catalogues

Run the application against the completed confidence-0.9 baseline catalogue and the existing private detector-evaluation root.

```powershell
$publish = "C:\PhotoIdentity\M16\review-app"
$baselineDb = "C:\PhotoIdentity\M16\runs\confidence-090\catalogue.db"
$evaluationRoot = "C:\PhotoIdentity\M16\private\evaluation-sessions"

$env:PhotoIdentity__DatabasePath = $baselineDb
$env:PhotoIdentity__DetectorEvaluationRoot = $evaluationRoot

Set-Location -LiteralPath $publish
dotnet .\PhotoIdentity.Api.dll --urls "http://127.0.0.1:5080"
```

Open:

```text
http://localhost:5080/detector-comparisons
```

Select `M16 confidence 0.9 baseline` and choose **Freeze reusable ground truth**. Freezing succeeds only when all 100 baseline photos are complete and their arithmetic still matches:

```text
countable_faces = correct_or_background_detections + manually_marked_misses
```

The frozen snapshot copies accepted baseline detection boxes and manually marked missed-face boxes into a private immutable ground-truth file under:

```text
<DetectorEvaluationRoot>\ground-truth
```

After this succeeds, stop the application. The candidate catalogue does not need the baseline processing run, but it must use the same detector-evaluation root so the frozen snapshot remains available.

## Step 2: create one isolated catalogue per confidence

Use separate paths, for example:

```text
C:\PhotoIdentity\M16\runs\confidence-080\catalogue.db
C:\PhotoIdentity\M16\runs\confidence-070\catalogue.db
C:\PhotoIdentity\M16\runs\confidence-060\catalogue.db
C:\PhotoIdentity\M16\runs\confidence-050\catalogue.db
```

Never reuse or mutate the confidence-0.9 baseline catalogue. Stage the unchanged 100-photo set for each candidate and process exactly one confidence value into its catalogue.

Run candidates in this order:

1. `0.8`
2. `0.7`
3. `0.6`
4. `0.5`

Do not run later candidates merely to collect extra data after an earlier candidate has met the governed M16 target unless the milestone decision explicitly requires it.

## Step 3: attach a candidate run

Start the application against one candidate catalogue while retaining the same private detector-evaluation root.

```powershell
$candidateDb = "C:\PhotoIdentity\M16\runs\confidence-080\catalogue.db"
$evaluationRoot = "C:\PhotoIdentity\M16\private\evaluation-sessions"

$env:PhotoIdentity__DatabasePath = $candidateDb
$env:PhotoIdentity__DetectorEvaluationRoot = $evaluationRoot

Set-Location -LiteralPath "C:\PhotoIdentity\M16\review-app"
dotnet .\PhotoIdentity.Api.dll --urls "http://127.0.0.1:5080"
```

Open `http://localhost:5080/detector-comparisons`, select the frozen baseline, select the candidate processing run and create a comparison such as `M16 confidence 0.8`.

Comparison creation:

- requires the exact frozen photo set;
- verifies every full source SHA-256;
- snapshots candidate detections into the private comparison file;
- applies deterministic IoU matching; and
- surfaces only unmatched, duplicate or ambiguous components.

Clean one-to-one matches are counted automatically and do not appear in the manual queue.

## Step 4: resolve only surfaced exceptions

For each exception photo:

- match a candidate detection to one ground-truth face;
- classify an unmatched candidate as `False detection`;
- classify an additional detection of an already counted face as `Duplicate detection`;
- mark a ground-truth face as missed when no candidate should match it; and
- add neutral notes only when they help explain the correction.

Manual matches are one-to-one. Every surfaced candidate and ground-truth node must be resolved before the comparison is complete. Corrections are saved atomically under:

```text
<DetectorEvaluationRoot>\comparisons
```

The application can be restarted and the comparison resumed without repeating automatic matches or prior manual corrections.

## Step 5: assess and export the M16 gate

After exception review, record whether a material failure category remains incompatible with the intended archive workflow. The gate remains `pending` until both conditions are true:

- every exception node is resolved; and
- the material-category assessment is recorded.

The comparison calculates:

- overall recall;
- recall for photos with five or more countable faces;
- source-group summaries;
- primary-category summaries;
- false and duplicate totals; and
- the four-part M16 gate.

The fixed M16 target is:

- overall recall at least `90%`;
- five-plus-face recall at least `85%`;
- no more than `10` false or duplicate detections; and
- no material failure category.

Use **Export summaries** to create a spreadsheet-compatible CSV. Keep the detailed comparison files and export private. Commit only privacy-safe aggregate evidence.

## Step 6: proceed or stop

When a confidence candidate meets the complete M16 gate, stop the threshold sweep and continue with the governed rollout work. When all four threshold candidates fail, use the recorded category evidence to decide whether WI-0036 multi-scale YuNet is required.

Do not copy candidate detections into the canonical reviewed catalogue during comparison. Any accepted detector change still requires WI-0038 rollout and provenance controls.
