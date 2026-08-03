# Detector recall pilot

Use this procedure to measure face-detection recall without reviewing the complete archive. The sample contains 100 unique photos: 50 representative photos selected mechanically and 50 deliberately difficult photos.

Keep the completed tally outside Git because image names, notes and counts may reveal private archive information. Only privacy-safe aggregate totals belong in milestone evidence.

## Decision target

The initial stop/go target is:

- at least 90% recall across all countable faces;
- at least 85% recall in photos containing five or more countable faces;
- no more than 10 false or duplicate detections across the 100 photos; and
- no obvious failure category that would materially harm the intended archive workflow.

These values are a product decision threshold for this pilot, not a general accuracy claim. Record the result before changing the target.

## Step 1: select 50 representative photos

1. Sort the 560-photo pilot by a stable rule, such as relative path or capture date.
2. Select every eleventh photo until 50 unique photos have been collected.
3. Do not replace an inconvenient photo unless it cannot be decoded. If one must be replaced, take the next photo in the same ordering and record that a replacement occurred.

This prevents selecting only photographs that already look easy or difficult.

## Step 2: select 50 difficult photos

Select 50 additional unique photos that are not in the representative set:

- 20 group photos containing five or more visible faces;
- 10 photos with small or distant faces;
- 10 photos dominated by profile, partially occluded, blurred or low-light faces; and
- 10 scanned, old or low-resolution photos.

A photo can have several difficulties, but assign it one primary category so the totals remain clear.

## Step 3: decide which faces count

Count a face when a person is visible and the face is recognisable at 100% zoom well enough that indexing the person would be useful.

Include:

- frontal faces;
- profiles;
- children and babies;
- partially occluded faces when enough facial structure remains visible; and
- blurred or low-light faces when a human can still recognise that a face is present.

Exclude from the primary metric:

- backs of heads;
- faces so tiny that they are only a few indistinguishable pixels at 100% zoom;
- statues, drawings, posters, television screens and reflections; and
- faces outside the intended photographic scene.

Record excluded screen, poster or reflection faces separately when they occur. Do not change the counting rule partway through the sample.

## Step 4: create the private manifest

Use the private detector-recall spreadsheet with one row per photo and these fields:

```text
sample_id
image_name
sample_group
source_group
primary_category
countable_faces
correct_detections
missed_faces
false_detections
duplicate_detections
likely_background_or_unknown_detections
miss_reason
notes
row_check
```

Use neutral sample IDs such as `R001` to `R050` and `D001` to `D050`. Enter the exact staged image name in the private workbook, but do not commit the completed workbook.

Use stable `source_group` values such as:

```text
Pilot representative
Pilot difficult
External difficult
```

`likely_background_or_unknown_detections` is optional. It estimates how many correctly detected faces are likely to create review work without becoming named identities. It is not a person assignment and does not require deciding exactly who somebody is.

Before detector review, complete at least these columns:

```text
Sample ID
Image Name
Sample Group
Source Group
Primary Category
Countable Faces
```

Export the **Photo Review** sheet as CSV UTF-8. The workspace accepts comma or semicolon separators and finds the header after the spreadsheet preamble rows. An optional `Source SHA-256` column adds a full content-hash check; exact staged filenames and persisted revision hashes are retained in the private session even when this column is absent.

## Step 5: start the private evaluation workspace

Publish and run the local application against the isolated detector database. In Windows PowerShell 5.1:

```powershell
$repo = "C:\Kod\codex\Photo Identity Indexer"
$publish = "C:\PhotoIdentity\M16\review-app"
$baselineDb = "C:\PhotoIdentity\M16\runs\confidence-090\catalogue.db"
$evaluationRoot = "C:\PhotoIdentity\M16\private\evaluation-sessions"

Set-Location -LiteralPath $repo

Remove-Item `
    -LiteralPath $publish `
    -Recurse `
    -Force `
    -ErrorAction SilentlyContinue

dotnet publish `
    .\src\PhotoIdentity.Api\PhotoIdentity.Api.csproj `
    --configuration Release `
    --output $publish

if ($LASTEXITCODE -ne 0) {
    throw "Publishing the review application failed."
}

$env:PhotoIdentity__DatabasePath = $baselineDb
$env:PhotoIdentity__DetectorEvaluationRoot = $evaluationRoot

Set-Location -LiteralPath $publish

dotnet .\PhotoIdentity.Api.dll `
    --urls "http://127.0.0.1:5080"
```

Leave that PowerShell window running. Open this address in a browser:

```text
http://localhost:5080/detector-evaluation
```

The detector-evaluation root contains private JSON session files. Keep it outside the repository and include it in the private M16 backup process.

## Step 6: import and review the baseline

1. Select the completed confidence-0.9 processing run.
2. Enter a session name such as `M16 confidence 0.9 baseline`.
3. Select the private Photo Review CSV.
4. Create the evaluation session. Creation fails when an image is missing, extra, duplicated or attached to a different optional SHA-256.
5. Review one complete source photo at a time.
6. Classify every detector box as one of:
   - `Correct` — one useful detection of a countable face;
   - `Background / unknown` — a correctly detected countable face likely to remain unnamed;
   - `False` — not a countable human face under the fixed rule; or
   - `Duplicate` — an additional detection of a face already counted once.
7. For every countable face with no matching detector box, choose **Mark missed face**, click two opposite corners around the face and retain one missed box per missed face.
8. Add a neutral miss reason such as `small`, `profile`, `occluded`, `blur`, `low_light` or `scan` when useful.
9. Save the photo. A row is complete only when every detector box is classified and:

```text
countable_faces = correct_or_background_detections + missed_face_boxes
```

10. Use **Save and next** to continue. The JSON session is updated atomically and can be resumed after restarting the application.
11. Export CSV when review is complete. The export retains per-photo values compatible with the private spreadsheet and marks incomplete rows explicitly.

Do not identify people during this pass. Detector-evaluation classifications do not write assignments, rejections or suggestions to the canonical identity review history.

## Step 7: calculate the results

Calculate:

```text
overall_recall = total_correct_detections / total_countable_faces
group_recall = group_correct_detections / group_countable_faces
false_or_duplicate_total = total_false_detections + total_duplicate_detections
background_or_unknown_share = likely_background_or_unknown_detections / total_correct_detections
```

Also calculate recall separately for:

- representative photos;
- group photos;
- small or distant faces;
- profile, occluded, blurred or low-light faces; and
- scanned, old or low-resolution photos.

## Step 8: make the milestone decision

- When every decision target passes and no material failure category remains, complete WI-0034, cancel WI-0035 through WI-0038 as unnecessary, and complete M16.
- When recall fails but most misses have low confidence, continue to WI-0035 for a threshold sweep.
- When threshold tuning is insufficient and misses are mainly small faces, continue to WI-0036 for multi-scale YuNet detection.
- When multi-scale YuNet remains insufficient, continue to WI-0037 for another governed detector candidate.
- Any changed detector pipeline must complete WI-0038 before it is used with the canonical reviewed catalogue.
- When the accepted detector pipeline materially expands the face population, rerun the exact-model embedding comparison using the same new detections and aligned crops for every embedder.
- Use the likely-background-or-unknown total to decide the priority of a later non-identity workflow with `Unknown person`, `Background / ignore`, `Not a face` and `Deferred` outcomes.

## Privacy-safe result template

Return only this aggregate summary to the repository:

```text
Sample: 100 photos; 50 representative and 50 difficult
Countable faces: N
Correct detections: N
Missed faces: N
Overall recall: N%
Five-or-more-face recall: N%
False detections: N
Duplicate detections: N
Likely background or unknown detections: N
Likely background or unknown share: N%
Representative recall: N%
Group recall: N%
Small/distant recall: N%
Profile/occluded/blur/low-light recall: N%
Scanned/old/low-resolution recall: N%
Decision target: pass/fail
Dominant privacy-safe miss categories:
Next action: stop / threshold sweep / multi-scale YuNet / alternate detector
Model comparison rerun required: yes/no
Unknown/background workflow priority: low/medium/high
```
