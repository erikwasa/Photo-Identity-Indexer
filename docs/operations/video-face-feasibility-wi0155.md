# WI-0155 — isolated offline video-face feasibility checkpoint

This experimental tool remains on the long-running WI-0155 integration
branch (wi-0155), **not main**. All development PRs target wi-0155
until a specific **near-completion maintainer decision** to either integrate
video recognition into Photo Identity or keep it as a separate component.
No video recognition is added to the normal production API, worker,
UI or media processing flow merely by merging a prototype PR.
PRs #740, #741, #745, #748 and #751 are merged into wi-0155.

**Updated product boundary — 2026-11-10:** For a video, review
**provisional person groups**, not individual frame occurrences.
Each group should contain the best few crops and top-2/top-3
suggestions from **all eligible people** in the production
catalogue (the maintainer reports 100+ people). A V03 review
ideally has three groups, but extra groups must support explicit
merge, split and dismiss. Reviewed identity is an association
that a person **appears in the video**; no occurrence time
display is required. **Video crops and video-derived embeddings
must never enrich permanent still-image face references.**
See checkpoint 19 for the measured experimental limitations.

**Maintainer workflow clarification — 2026-11-10:** The video-identification
user flow is the **same as for still images**. Auto-assignment requires
both the governed similarity cutoff and meaningful runner-up margin,
and is expected to apply to only a minority (likely under 25%).
Most known-person observations should instead offer two or three
ranked suggestions for human confirmation (anticipated 50–60%),
with the remaining ~20% manually identified or newly created using
existing approved review/evidence paths. These percentages are rough
forecasts, not observed rates. For difficult oblique/distant V03,
**correctly suggesting a person in the top 2–3 is useful success**;
large confidence margins or automatic matches are *not* required.
Do not repeatedly process the full 252-frame cohort to force automatic
matches. This operational direction takes precedence over earlier
checkpoint text referring to zero strong review suggestions.

**Current direction — 2026-11-07:** Identify at least one useful
face observation per visible person, test existing-catalogue
known-person suggestions and unknown handling, and examine a small
number of additional local videos. Historical grouping sensitivity is
a diagnostic, **not an acceptance target**. The current experiment
**does not yet read PostgreSQL**: manually supplied reference photos
do not equal real catalogue identity matching. See the revised
[WI-0155 requirements](../delivery/work-items/WI-0155-video-face-processing-evaluation.md).

## Scope and limits

The normal prototype accepts **one private JSON manifest** naming **1–12 already
extracted JPEG/PNG frames** from one expressly selected clip, with relative
timestamps in strictly increasing order, maximum 10 minutes. Input frames
must be ordinary files immediately beside the manifest, at most 8 MiB each.
The program reuses the repository's governed, SHA-verified
`yunet-2023mar-fp32` and `sface-2021dec-fp32` models and existing
OpenCV alignment pipeline; no detector training or new external inference
provider is used.

By default it prints only aggregate counts and elapsed milliseconds, including
a **heuristic**, one-to-one count of potentially repeated faces across
consecutive sampled frames no more than five seconds apart. Adjacent frame
similarity threshold 0.80 is **not calibrated for identity** or video tracking.

In the **explicit opt-in** reference mode, provide a *separate*, private
reference manifest with 1–6 operator-approved local stills, each containing
**exactly one detected face**. Every reference gets an artificial alias
`R01`–`R99` (no real names). Frame and reference filenames must have no
directory components, and no byte-identical photos may occur across
references or between a reference and a video frame. The tool compares SFace cosine embeddings
in memory and writes a **brand-new, private JSON review report** to a
separate folder only when both `--references` and `--private-report`
are present. It refuses existing reports and paths inside the repository
or either input folder. Normal stdout remains aggregate-only.

Review fields are sampled frame number/time, detected face ordinal,
candidate alias (or null), cosine score, runner-up score, score gap and
`reviewStatus`. The report includes the exact detector/embedder model IDs
and SHA-256 digests for reproducibility. The aggregate elapsed time in
reference mode includes preparing the approved reference stills as well
as frame analysis. Possible values: `review-candidate`,
`single-reference-uncertain`, `ambiguous`,
`below-exploratory-threshold`. The deliberately *uncalibrated*
pilot-only thresholds are cosine >= 0.80 and gap >= 0.05.
Even `review-candidate` **does not mean identity assigned**. One-reference
results are always marked uncertain; weak scores suppress the alias.
None of these values constitutes a verified recognition result.

**The private report is biometric-derived personal information.** It must
remain in a local non-synced operator-controlled folder; never copy its
contents, aliases, scores, timestamps or private paths into the repository,
CI, chats, tickets or public PR reviews. No real pilot accuracy is claimed
before a human-reviewed local run.

The model CLI has **no PostgreSQL, worker, API, OneDrive or video-file
dependency**. It reads only the manifest, explicitly named still frames
and already-installed approved models. Checkpoint 4 has a separate
operator-invoked local FFmpeg sampler that explicitly reads one approved local clip. It does **not** create crops,
embeddings on disk, identity assignments, catalogue writes, or any
automatic video source or OneDrive hydration. Outputs exclude file
paths, face images, vectors, person IDs and identifiers. Repeated
observations are aggregated, not published.

This is **not** yet a production video face-recognition feature. A
tentative offline suggestion path now exists; threshold sensitivity
diagnostics, accuracy evaluation, representative still selection and any
production persistence require explicit further decisions.
An offline model test cannot establish photo/video identity accuracy.

## Windows checks without personal media

**Do not switch branches in the WI-0211 working directory.** Use a
separate detached worktree for reading and testing this experimental PR.
A detached worktree is intentional here: it does not create commits on
the long-lived integration branch. The first worktree was created for
PR #740; it can now be refreshed to the merged `wi-0155` head.

```powershell
$mainRepo = "C:\Kod\codex\Photo Identity Indexer"
$wi0155Root = "C:\Kod\codex\Photo Identity Indexer-WI0155"
git -C $mainRepo fetch origin
if ($LASTEXITCODE -ne 0) { throw "Fetch failed." }

if (-not (Test-Path -LiteralPath $wi0155Root)) {
    git -C $mainRepo worktree add --detach $wi0155Root origin/wi-0155
} else {
    $changes = @(git -C $wi0155Root status --porcelain)
    if ($changes.Count -gt 0) { throw "Review WI-0155 changes before updating." }
    git -C $wi0155Root switch --detach origin/wi-0155
}
if ($LASTEXITCODE -ne 0) { throw "Worktree setup failed." }
Set-Location $wi0155Root

dotnet test tests/PhotoIdentity.VideoFaceFeasibility.Tests/PhotoIdentity.VideoFaceFeasibility.Tests.csproj -c Release

dotnet run --project tools/PhotoIdentity.VideoFaceFeasibility -c Release -- --help

dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
```

The tests use synthetic byte fixtures, fake detection/embedding models and
temporary folders. They do not need installed ONNX models, local
PostgreSQL or private videos. Passing them does not prove real face detection
or recognition.

## Explicit optional local model pilot (not required for first PR)

**Only when deliberately approved by the maintainer**, create a private
directory *outside the repository*, containing a JSON file and up to
12 JPEG/PNG stills extracted from one already-local, approved clip.
The tool never extracts frames from a video by itself. Do not supply an
online-only original. Make sure the stills have the desired portrait
orientation and SDR colors before starting. For example:

```text
%LOCALAPPDATA%\PhotoIdentity\Temp\wi0155\clip-01\
  selected-frames.json
  frame-01.png
  frame-02.png
```

```json
{
  "schemaVersion": 1,
  "frames": [
    { "timestampMs": 1000, "file": "frame-01.png" },
    { "timestampMs": 3000, "file": "frame-02.png" }
  ]
}
```

After independently installing the governed models, run from the
repository root (replace the private path with your actual approved
manifest; never commit it):

```powershell
$privateManifest = Join-Path $env:LOCALAPPDATA "PhotoIdentity\Temp\wi0155\clip-01\selected-frames.json"

dotnet run --project tools/PhotoIdentity.VideoFaceFeasibility -c Release -- `
    --manifest $privateManifest `
    --root (Get-Location).Path
```

The summary is suitable for sharing **only if free of other identifying
information**. Keep the original video, selected frames and review notes
private. No real-media pilot is requested for this first PR. Do not run
against the maintained catalogue or modify Photo Identity settings.

## Review plan before recommending production work

Use a few deliberately selected frames and record privately:
the number of visible faces versus detections, obvious misses, repeated
detections and ambiguous faces; overall elapsed time; whether the
selected frame still offers a useful person view. The approved **read-only** private known-person reference comparison
is now available, but its accuracy remains uncalibrated.
No identity labels, inferred names or biometric evidence are to be
published in source control.

Keep the experimental tool on `wi-0155` while checkpoints accumulate.
A human decision will determine whether any subset should be ported
to production and merged into `main`.


## Optional separate known-person reference pilot — checkpoint 2

**Only after this PR has merged into `wi-0155`**, use the independent
WI-0155 worktree and approved local media. This is a deliberately
selected, limited private evaluation—not part of the standard Photo
Identity workflow. No PostgreSQL, OneDrive, internet, person catalogue
or original video hydration is used.

Set up *three different* local folders **outside the repository**:

```text
%LOCALAPPDATA%\PhotoIdentity\Temp\wi0155\pilot-01\
  frames\
    frames.json
    frame-01.png
    frame-02.png
  references\
    references.json
    person-A.png
    person-B.png
  reviews\
```

Frame manifest (same 1–12 ordered, timestamped JPEG/PNG files as
checkpoint 1):

```json
{
  "schemaVersion": 1,
  "frames": [
    { "timestampMs": 1000, "file": "frame-01.png" },
    { "timestampMs": 3000, "file": "frame-02.png" }
  ]
}
```

Reference manifest (one distinct, approved **single-face** still per
alias; these cannot be copies of any selected video frame):

```json
{
  "schemaVersion": 1,
  "references": [
    { "alias": "R01", "file": "person-A.png" },
    { "alias": "R02", "file": "person-B.png" }
  ]
}
```

Aliases are not names or canonical person IDs. Keep the mapping to
familiar people in private operator notes, not source control.

```powershell
# In the separate WI-0155 worktree, once the PR is merged:
$pilotRoot = Join-Path $env:LOCALAPPDATA 'PhotoIdentity\Temp\wi0155\pilot-01'
$frames = Join-Path $pilotRoot 'frames\frames.json'
$references = Join-Path $pilotRoot 'references\references.json'
$reviews = Join-Path $pilotRoot 'reviews'
New-Item -ItemType Directory -Path $reviews -Force | Out-Null

# Brand-new report name; existing reports are never overwritten.
$privateReport = Join-Path $reviews (
    'review-' + (Get-Date -Format 'yyyyMMdd-HHmmssfff') + '.json')

# Verify source snapshots before and after. No database required.
$photos = @(
    Get-ChildItem (Join-Path $pilotRoot 'frames') -File |
        Where-Object { $_.Extension -in '.png', '.jpg', '.jpeg' }
    Get-ChildItem (Join-Path $pilotRoot 'references') -File |
        Where-Object { $_.Extension -in '.png', '.jpg', '.jpeg' }
)
$before = @($photos | Get-FileHash -Algorithm SHA256)

dotnet run --project tools/PhotoIdentity.VideoFaceFeasibility -c Release -- `
    --manifest $frames `
    --references $references `
    --private-report $privateReport `
    --root (Get-Location).Path

if ($LASTEXITCODE -ne 0) { throw 'Private feasibility run failed.' }

$after = @($photos | Get-FileHash -Algorithm SHA256)
if ((@($before.Hash) -join ',') -ne (@($after.Hash) -join ',')) {
    throw 'One or more input stills changed.'
}
Write-Host 'Selected original stills unchanged.'

# Review the private JSON only locally; do not paste it into a PR/chat.
# For cleanup when you no longer need it:
# Remove-Item -LiteralPath $privateReport
```

For an initial human judgement, privately compare the sampled video
frames with visible faces, note obvious omissions and confusions, and
assess if the `Rxx` suggestions are useful, misleading or uncertain.
Time and review burden should be recorded privately for a few clips
rather than extrapolated to a whole archive. Do not label or save
canonical identities automatically.

### Post-merge test-only checks

These do not require personal media or installed models:

```powershell
dotnet test tests/PhotoIdentity.VideoFaceFeasibility.Tests/PhotoIdentity.VideoFaceFeasibility.Tests.csproj -c Release
dotnet run --project tools/PhotoIdentity.VideoFaceFeasibility -c Release -- --help
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
```


## Checkpoint 3 — approved pilot and opt-in sensitivity diagnosis

The maintainer's first private pilot of merged PR #741 used two
operator-selected frames containing the same person, also represented
in one reference photograph. The face detector returned a face in only
one frame; one embedding was computed; no known-person candidate passed
the existing experimental threshold. End-to-end processing took roughly
1.35 seconds, and human review a few seconds. File hashes were
unchanged. These observations are not statistically representative, and
the true reference's private ranking is still unknown.

Do not infer that lowering the similarity threshold will solve
recognition. First establish **whether the highest-ranked reference is
actually the known person**. Then evaluate difficult and unknown-person
frames; a more permissive threshold can increase false suggestions.
Likewise, a person appearing in a frame does not necessarily mean a
recognizable face is visible.

Checkpoint 3 adds `--diagnostics` **only** alongside
`--references` and `--private-report`. The ordinary reference
experiment keeps its original YuNet confidence 0.9, similarity cutoff
0.80, uncertainty rules and aggregate-only console output. The
opt-in diagnostic report adds:

- `diagnostics.frameDetections`: every selected frame ordinal and
  timestamp, count of detected faces, and lowest/highest detector
  confidence (null if none). This includes frames with zero detections.
- `diagnostics.rankedFaces`: for every detected face, the highest
  cosine-ranked *reference pseudonym even if the existing threshold
  suppressed it*, top score, runner-up score, score gap and the **hypothetical**
  review outcome at similarity cutoffs 0.363, 0.45, 0.60 and 0.80.
- `diagnostics.requestedDetectorConfidenceThreshold`: the explicitly
  selected YuNet confidence for that run. Accepted values: 0.9 (default),
  0.7 and 0.5. This affects both reference and video-frame detection;
  reference stills must continue to contain exactly one detected face.

A hypothetical `would-suggest-for-review` is **not an identity claim**.
The 0.363 cutoff is included as an exploratory comparison point because
[OpenCV's SFace tutorial](https://docs.opencv.org/4.11.0/d0/dd4/tutorial_dnn_face.html)
reports that value for its **LFW benchmark**. That benchmark does not
validate any cutoff for these video frames, reference photos, preprocessing
steps or personal collection. The value is **not** a recommended deployment
threshold. The comparison
does not change the main `findings` review statuses or the baseline
similarity threshold. Only private JSON files contain ranked aliases
and frame-level evidence.

### Refresh separate WI-0155 checkout after PR merge

Do not change the WI-0211 checkout.

```powershell
$mainRepo = "C:\Kod\codex\Photo Identity Indexer"
$wi0155Root = "C:\Kod\codex\Photo Identity Indexer-WI0155"

git -C $mainRepo fetch origin
if ($LASTEXITCODE -ne 0) { throw 'Fetch failed.' }
$changes = @(git -C $wi0155Root status --porcelain)
if ($changes.Count -gt 0) { throw 'Review WI-0155 uncommitted files first.' }
git -C $wi0155Root switch --detach origin/wi-0155
if ($LASTEXITCODE -ne 0) { throw 'WI-0155 checkout failed.' }
Set-Location $wi0155Root

dotnet test tests/PhotoIdentity.VideoFaceFeasibility.Tests/PhotoIdentity.VideoFaceFeasibility.Tests.csproj -c Release
if ($LASTEXITCODE -ne 0) { throw 'Focused tests failed.' }
dotnet run --project tools/PhotoIdentity.VideoFaceFeasibility -c Release -- --help
if ($LASTEXITCODE -ne 0) { throw 'CLI help failed.' }
dotnet run --project tools/PhotoIdentity.Docs -- validate
if ($LASTEXITCODE -ne 0) { throw 'Docs validation failed.' }
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
if ($LASTEXITCODE -ne 0) { throw 'Docs generation check failed.' }
```

### Explicit private rerun — 0.9 baseline, then 0.7

Reuse the approved `pilot-01` photographs and manifest files
**outside the repository**. Do not change or upload the source images.
Install the governed YuNet/SFace model binaries if needed (see above);
point `$modelDir` to the folder that already contains both approved
ONNX files. The following uses the same private input photos for both
runs so that only the configured detector confidence changes:

```powershell
Set-Location "C:\Kod\codex\Photo Identity Indexer-WI0155"

$pilotRoot = Join-Path $env:LOCALAPPDATA 'PhotoIdentity\Temp\wi0155\pilot-01'
$frames = Join-Path $pilotRoot 'frames\frames.json'
$references = Join-Path $pilotRoot 'references\references.json'
$reviews = Join-Path $pilotRoot 'reviews'
$modelDir = Join-Path $env:LOCALAPPDATA 'PhotoIdentity\Tools\WI0155\Models'
# If your models are installed elsewhere, adjust $modelDir above.
New-Item -ItemType Directory -Force -Path $reviews | Out-Null

$photos = @(
    Get-ChildItem (Join-Path $pilotRoot 'frames') -File |
        Where-Object { $_.Extension -in '.jpg', '.jpeg', '.png' }
    Get-ChildItem (Join-Path $pilotRoot 'references') -File |
        Where-Object { $_.Extension -in '.jpg', '.jpeg', '.png' }
)
$before = @($photos | Get-FileHash -Algorithm SHA256)
$reports = @()

foreach ($confidence in @('0.9', '0.7')) {
    $reportPath = Join-Path $reviews (
        'diagnostic-' + ($confidence -replace '\.', '') + '-' +
        (Get-Date -Format 'yyyyMMdd-HHmmssfff') + '.json')

    dotnet run --project tools/PhotoIdentity.VideoFaceFeasibility -c Release -- `
        --manifest $frames `
        --references $references `
        --private-report $reportPath `
        --model-dir $modelDir `
        --root (Get-Location).Path `
        --diagnostics `
        --detector-confidence $confidence

    if ($LASTEXITCODE -ne 0) {
        throw "Private diagnostics failed at detector confidence $confidence."
    }
    $reports += $reportPath
}

$after = @($photos | Get-FileHash -Algorithm SHA256)
if ((@($before.Hash) -join ',') -ne (@($after.Hash) -join ',')) {
    throw 'Selected input photos changed.'
}

# Safe aggregate-only comparison; do not paste private detailed reports.
foreach ($path in $reports) {
    $result = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    [pscustomobject]@{
        DetectorConfidence = $result.diagnostics.requestedDetectorConfidenceThreshold
        FramesExamined = $result.aggregate.framesExamined
        FramesWithDetections = $result.aggregate.framesWithFaces
        FacesDetected = $result.aggregate.facesDetected
        DurationMs = $result.aggregate.elapsedMilliseconds
    }
}
```

Optionally add a separate `0.5` run if 0.7 still misses the face;
the more permissive configuration is only a local diagnostic, not
permission to enable it in production. If a reference still now
contains more than one detected face, the experiment rejects it:
choose a clearer independent single-face reference rather than
silently selecting one.

To inspect the private evidence **only on your own machine**:

```powershell
$private = Get-Content -LiteralPath $reports[0] -Raw | ConvertFrom-Json
$private.diagnostics.frameDetections |
    Format-Table frameNumber, timestampMs, facesDetected, lowestConfidence, highestConfidence
$private.diagnostics.rankedFaces |
    Format-List frameNumber, faceNumber, topReferenceAlias, topSimilarity, scoreGap, thresholdChecks
```

Compare the ranked alias with your *private* reference mapping to
determine whether the correct person ranked first. Repeat, if desired,
with a few known-positive and unknown-person frames, tracking wrong
top-ranked references and increased false suggestions as well as
recovered detections. Do not log names, video paths, face-level scores,
the private alias mapping or detailed reports in GitHub or this chat.
Share only non-identifying aggregate test conclusions.

The diagnostic report is a new, sensitive biometric-derived JSON file.
It must stay in an operator-controlled **non-synced** folder. Delete it
when no longer needed. No database access, worker, OneDrive hydration
or original-video modification is involved.


## Checkpoint 4 — sample the full timeline to look for additional people

The maintainer confirmed a second person is visible elsewhere in the
video, but neither of the two original hand-selected stills contains
that person. This is a **sampling coverage failure**, not evidence
that the detector cannot see the second person's face. YuNet at 0.9
detected only frame 1 (confidence 0.9082); at 0.7 it also detected
frame 2 (confidence 0.8962). The correct known reference ranked
first for both frames, with cosine scores 0.5402 and 0.6571.
These two frames cannot establish whole-video person coverage.

Checkpoint 4 adds a separate, explicit private PowerShell sampling
script. It uses the maintainer's already approved local FFmpeg and
FFprobe binaries, one explicitly approved *already local* video,
and a selected window of at most 10 minutes. It creates 12, 24
or 36 evenly spaced frame stills in a new folder under
%LOCALAPPDATA%\PhotoIdentity\Temp\wi0155\coverage.
No OneDrive placeholders, linked or cloud-recall source files,
unattended jobs, network calls, original modifications or catalogue
writes are allowed. If a video exceeds ten minutes, choose a
specific window and repeat separately for other windows if necessary.

The model CLI retains its normal twelve-frame limit. Explicit
--coverage --diagnostics with --references and --private-report
permits 36 pre-extracted stills for one private experiment.
Only the private report contains optional cross-frame appearance
groups: G0001-style temporary aliases, first-seen frame/time and
observation counts, plus a separate per-face assignment. Each
same-frame detection is assigned to a different tentative group.
Grouping compares **in-memory embeddings** at an uncalibrated 0.80
repeat threshold. **Groups do not establish unique people**:
one person may split into several groups; distinct people may merge.
No vectors, crops or identities are written to disk.

### Run locally after merging the checkpoint-4 PR

Use PowerShell 7.6 in the separate WI-0155 worktree, not in WI-0211.
Run the normal synthetic model tests and documentation checks above.
Then set an explicitly approved, already-local clip and the verified
FFmpeg model paths. The following script makes a new private manifest
and runs YuNet at diagnostic confidence 0.7.

~~~powershell
Set-Location "C:\Kod\codex\Photo Identity Indexer-WI0155"

# Replace these with your verified already-local paths.
$video = "D:\Private\ApprovedLocalClip.mp4"
$ffmpegPath = (Get-Command ffmpeg.exe -ErrorAction Stop).Source
$modelDir = Join-Path $env:LOCALAPPDATA 'PhotoIdentity\Tools\WI0155\Models'
$pilotRoot = Join-Path $env:LOCALAPPDATA 'PhotoIdentity\Temp\wi0155\pilot-01'
$referenceManifest = Join-Path $pilotRoot 'references\references.json'
$reviewDir = Join-Path $pilotRoot 'reviews'

$sampleParams = @{
    VideoPath = $video
    FfmpegPath = $ffmpegPath
    ApprovePrivateLocalVideo = $true
    FrameCount = 36
}
$coverage = & .\tools\PhotoIdentity.VideoFaceFeasibility\New-VideoFaceCoverageFrames.ps1 @sampleParams

if (-not $coverage -or -not (Test-Path -LiteralPath $coverage.ManifestPath -PathType Leaf)) {
    throw 'No private coverage manifest created.'
}
if (-not (Test-Path -LiteralPath $referenceManifest -PathType Leaf)) {
    throw 'Previously approved reference manifest missing.'
}
New-Item -ItemType Directory -Path $reviewDir -Force | Out-Null

$stills = @(Get-ChildItem -LiteralPath $coverage.FramesDirectory -Filter '*.jpg' -File)
$before = @($stills | Get-FileHash -Algorithm SHA256)
$reportPath = Join-Path $reviewDir ('coverage-' + (Get-Date -Format 'yyyyMMdd-HHmmssfff') + '.json')

$argsForPilot = @(
    'run', '--project', 'tools/PhotoIdentity.VideoFaceFeasibility',
    '-c', 'Release', '--', '--manifest', $coverage.ManifestPath,
    '--references', $referenceManifest, '--private-report', $reportPath,
    '--model-dir', $modelDir, '--root', (Get-Location).Path,
    '--diagnostics', '--detector-confidence', '0.7', '--coverage'
)
& dotnet @argsForPilot
if ($LASTEXITCODE -ne 0) { throw 'Private timeline coverage pilot failed.' }

$after = @($stills | Get-FileHash -Algorithm SHA256)
if ((@($before.Hash) -join ',') -ne (@($after.Hash) -join ',')) {
    throw 'Input image hashes changed.'
}

$private = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
[pscustomobject]@{
    FramesExamined = $private.aggregate.framesExamined
    FramesWithDetectedFaces = $private.aggregate.framesWithFaces
    FaceObservations = $private.aggregate.facesDetected
    PossibleAppearanceGroups = $private.diagnostics.coverage.possibleAppearanceGroups
    ElapsedMilliseconds = $private.aggregate.elapsedMilliseconds
}
~~~

For videos longer than ten minutes, add WindowStartSeconds and
WindowLengthSeconds (no more than 600 seconds) to the sampleParams
hashtable. The manifest timestamps are relative to the *selected
window*. Reports from different windows are independent and
their temporary group aliases cannot be compared across runs.

### Inspect and verify coverage privately

~~~powershell
$private.diagnostics.coverage.groups |
    Format-Table groupAlias, firstFrameNumber, firstTimestampMs, observations
$private.diagnostics.coverage.assignments |
    Where-Object reviewStatus -eq 'needs-distinct-face-review' |
    Format-Table frameNumber, timestampMs, faceNumber, groupAlias, closestPreviousSimilarity
# Open the extracted JPG stills locally to check if the second person
# actually appears in any sample and whether their face is detected.
~~~

**Human pilot acceptance questions:** Did any selected still show the
second person? If yes, was their face detected at confidence 0.7?
Did grouping split one person or combine unrelated faces? If the
person was absent from all stills, this is a *sampling* miss,
not a detector or matcher failure. Even 36 samples can miss a
short appearance: manually inspect a targeted interval and add
more deliberately selected samples rather than claiming complete
coverage. Whole-video tracking is not implemented.

These are private biometric-derived reports. Do not post frame
timestamps, face-level similarities, temporary group aliases,
private file paths, reference mappings or any photographs to PRs
or chats. Share only non-identifying counts and review conclusions.
Delete private derivatives when no longer needed.


## Checkpoint 5 — private grouping sensitivity using existing 36-frame pilot

**Reuse the 36 private JPEG stills extracted in checkpoint 4.** No
need to rerun FFmpeg, access the original video or change model settings.

In the maintainer's first 36-frame local pilot, the second person
appeared in samples 32–36 and was detected. Across all samples,
21 face observations were found in 19/36 sampled frames; baseline
grouping produced 14 tentative groups. Several zero-detection frames
had blurred, extreme-angle or partially hidden faces, where an
embedding might not be useful. The outstanding issue is therefore
excessive **group fragmentation**, not raw detector sensitivity.

The private diagnostics.coverage output now includes:

- groupingComparisons: multi-observation grouping at cosine thresholds
  0.60, 0.70 and 0.80, with at most eight in-memory exemplars per
  temporary group. Each comparison has independently numbered
  aliases and its own tentative assignments, group count and
  single-observation-group count. The original coverage.groups and
  coverage.assignments are still the **unchanged first-exemplar/0.80
  baseline**.
- sameFramePairs: for frames with two or more detections, compares
  normalized bounding-box overlap and embedding similarity. A high
  IoU (>=0.40) or cosine (>=0.85) produces a private
  possible-duplicate-review hint. No detection is automatically
  removed or assigned an identity.

A low threshold or multi-exemplar maximum can falsely **merge different
people**, while a high threshold can split the same person. None of
these diagnostics determines how many unique people are present.
Same-frame detections are still kept separate in all comparisons,
even if they may be duplicates; the operator must inspect the images.

### Explicit local re-evaluation (after PR merge)

Refresh the separate WI-0155 worktree and pass the focused
tests and docs checks first. Then use PowerShell 7.6:

~~~powershell
Set-Location "C:\Kod\codex\Photo Identity Indexer-WI0155"

# EDIT: select the existing 36-frame private manifest, not the video.
$coverageManifest = "C:\Path\To\Private\coverage-XXXX\frames.json"

$pilotRoot = Join-Path $env:LOCALAPPDATA 'PhotoIdentity\Temp\wi0155\pilot-01'
$referenceManifest = Join-Path $pilotRoot 'references\references.json'
$reviews = Join-Path $pilotRoot 'reviews'

# EDIT if the approved models are in a different existing folder.
$modelDir = Join-Path $env:LOCALAPPDATA 'PhotoIdentity\Tools\WI0155\Models'

foreach ($file in @($coverageManifest, $referenceManifest)) {
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) {
        throw 'An approved private manifest is missing.'
    }
}

$stills = @(Get-ChildItem -LiteralPath (Split-Path $coverageManifest -Parent) -File |
    Where-Object { $_.Extension -in '.jpg', '.jpeg', '.png' })
$before = @($stills | Get-FileHash -Algorithm SHA256)
New-Item -ItemType Directory -Path $reviews -Force | Out-Null

$reportPath = Join-Path $reviews (
    'grouping-sensitivity-' + (Get-Date -Format 'yyyyMMdd-HHmmssfff') + '.json')

$runArgs = @(
    'run', '--project', 'tools/PhotoIdentity.VideoFaceFeasibility',
    '-c', 'Release', '--',
    '--manifest', $coverageManifest,
    '--references', $referenceManifest,
    '--private-report', $reportPath,
    '--model-dir', $modelDir,
    '--root', (Get-Location).Path,
    '--diagnostics', '--detector-confidence', '0.7', '--coverage'
)
& dotnet @runArgs
if ($LASTEXITCODE -ne 0) { throw 'Grouping sensitivity failed.' }

$after = @($stills | Get-FileHash -Algorithm SHA256)
if ((@($before.Hash) -join ',') -ne (@($after.Hash) -join ',')) {
    throw 'Approved input stills changed.'
}
$private = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json

# Non-identifying summary; not a number of unique people.
[pscustomobject]@{
    FramesExamined = $private.aggregate.framesExamined
    FacesDetected = $private.aggregate.facesDetected
    BaselineGroups = $private.diagnostics.coverage.possibleAppearanceGroups
    DurationMs = $private.aggregate.elapsedMilliseconds
}
$private.diagnostics.coverage.groupingComparisons |
    Select-Object strategy, repeatSimilarityThreshold,
        possibleAppearanceGroups, singleObservationGroups |
    Format-Table
~~~

### Review possible false splits and same-frame duplicate detections

~~~powershell
# Private detail: do not upload to GitHub or paste into chat.
foreach ($comparison in $private.diagnostics.coverage.groupingComparisons) {
    Write-Host "Threshold $($comparison.repeatSimilarityThreshold)"
    $comparison.assignments |
        Where-Object { $_.frameNumber -in @(32,33,34,35,36) } |
        Format-Table frameNumber, faceNumber, groupAlias,
            closestPreviousSimilarity, reviewStatus
}

$private.diagnostics.coverage.sameFramePairs |
    Where-Object { $_.frameNumber -in @(14,17) } |
    Format-Table frameNumber, firstFaceNumber, secondFaceNumber,
        boundingBoxIntersectionOverUnion, embeddingCosineSimilarity, reviewStatus
~~~

Privately review the existing JPEGs for frames 14, 17 and 32–36.
Did 32 join 33–36? Did any lower cutoff incorrectly merge the two
different people? Are the two detections in frames 14/17 overlapping
boxes on one face or two genuine face images? Report only **aggregate
comparison counts** and generalized human observations, never
group aliases, face-level scores, exact timestamps, private paths,
images or reports. A real human-reviewed negative control is required
before adopting any grouping threshold in production.


## Current multi-video, person-level plan — 2026-11-07

The accepted 36-frame pilot found both human-observed people, despite
14 fragmented baseline groups. A later private comparison yielded
12/11/9 tentative groups at multi-observation thresholds 0.80/0.70/0.60,
but fewer groups are not a goal in themselves. Many missed faces
were blurry, partially occluded or viewed at difficult angles:
detecting every low-quality face does not ensure identification.

**New success unit:** a person, not a frame or a heuristic group.
A single good, human-reviewable observation may suffice to suggest
one known person. More observations are useful when they discover
someone else, strengthen reliable ranking evidence or provide a
clearer frame for human review. **Continue sampling through the
video after a known person is found**, because another person may
appear later. Do not turn the highest similarity across many samples
into automatic identity confirmation: evaluate incorrect suggestions,
rank-2 competition and uncertainty as well.

### What is implemented versus planned

**Already available:** one expressly approved, fully local video
per run; a bounded set of 12/24/36 timestamped extracted frames;
YuNet/SFace and optional diagnostics at detector confidence 0.7;
1–6 separately approved single-face reference photographs;
private review reports and grouping experiments. Each additional
video has a separate frame manifest and private report.

**Not yet implemented:** read-only loading of the Photo Identity
catalogue, an exact-model eligible known-person gallery,
a production-equivalent identity suggestion policy for video,
person-level best-frame selection, an unknown-rejection-calibrated
decision, or aggregation of multiple videos in one command.
Do not invent a database CLI switch or treat the current
--references mode as catalogue-backed.

**Next engineering increment, still isolated on wi-0155:** explicitly
approved, bounded, least-privilege read-only catalogue adapter or
operator-controlled local snapshot. Use only eligible current
assigned-person exemplars, filter Unknown/Rejected/reversed/
superseded evidence and merged people according to Photo Identity
identity-matching rules, and require the exact SFace model ID,
SHA-256 hash and compatible alignment/preprocessing. Preserve
one stable evidence/version snapshot; fail closed on incompatible
models, stale evidence or incomplete reads. Do not trigger matcher
regeneration, auto-assignment or any PostgreSQL writes. Production
photo detection uses CenterFace while the video experiment uses
YuNet; that difference requires evaluation rather than an
assumption of equal recognition quality.

### Additional private videos

Keep the existing two-person video as the repeatable baseline.
Add at least **one**, preferably **two or three**, diverse explicitly
approved fully local clips: one with several people (ideally
overlapping appearances), one with varied angles/movement, and
an intentionally unknown-to-gallery person or equivalent negative
case. A visible person is not always represented by a usable face.

For each clip, repeat the checkpoint-4 FFmpeg extraction with
the existing *explicitly approved* local binary, then run the
checkpoint-4 private-reference diagnostic only if the approved
reference set is relevant. The existing tool is **single-video**,
so do not combine manifests or group aliases across clips.

~~~powershell
Set-Location "C:\Kod\codex\Photo Identity Indexer-WI0155"

# EDIT: expressly approved, already-local video and PINNED FFmpeg.
$video = "C:\Private\ApprovedExtraVideo.mp4"
$ffmpegPath = "C:\Private\ApprovedTools\ffmpeg.exe"

$params = @{
    VideoPath = $video
    FfmpegPath = $ffmpegPath
    ApprovePrivateLocalVideo = $true
    FrameCount = 36
}
$coverage = & .\tools\PhotoIdentity.VideoFaceFeasibility\New-VideoFaceCoverageFrames.ps1 @params

if (-not $coverage -or -not (Test-Path -LiteralPath $coverage.ManifestPath -PathType Leaf)) {
    throw 'Private frame sampler did not create a manifest.'
}

# Use the checkpoint-4 CLI example with this new private manifest,
# approved reference photos, and a NEW private report path.
# It has NO PostgreSQL catalogue mode yet.
~~~

For videos longer than ten minutes, specify a bounded window and
inspect subsequent windows separately. Do not claim complete person
coverage from one short sample if someone appears later.

### Private human-reviewed per-clip evidence

Keep these notes **locally outside Git, CI and synced folders**:

- Number of human-observed distinct people, people with at least
  one detected **usable** face, and people missing from sampled frames.
- For people known to an eligible reference gallery, correct
  rank-1 at least once, wrong/ambiguous/below-threshold suggestions;
  distinguish the current manually curated references from the
  not-yet-implemented database gallery.
- Unknown-to-gallery examples: whether faces remain unknown rather
  than receiving unsupported known-person labels.
- Whether an additional frame materially improved ranking evidence
  or was visibly better for review; count redundant review effort.
- Frames sampled, time spent, unusable/borderline/usable examples.
  A qualitative reviewer judgment is sufficient initially; it is
  not a precise numerical model-quality metric.

Only non-identifying totals and generalized review findings
should be copied into work-item notes or chat. Never publish
names, person IDs, file paths, individual scores, embeddings,
face images or private report contents.

### Isolation and final architecture gate

All prototype code, tests and docs continue on wi-0155 and
its child feature branches; all such PRs **target wi-0155, not main**.
Do not add implicit Photo Identity startup, UI, API, worker, migration,
or automatic original-hydration changes during this work item.

**Near WI-0155 completion only**, review the additional video evidence,
catalogue matching/unknown handling, privacy and operating costs,
and obtain an explicit maintainer decision between:

1. An integrated, separately approved minimal video-recognition
   slice in Photo Identity, with its own main-branch PR and
   tests/rollback/review protections; or
2. Keeping video recognition as a separately operated tool,
   module or service, using a deliberately governed, narrow
   read-only identity-data interface/snapshot and documented
   privacy, authentication, update and retention boundaries.

Neither choice is pre-decided. It is acceptable to complete
feasibility with a documented decision and a separate production
follow-up; do not interpret experimental PR merges as permission
to merge the entire branch into main.


## Checkpoint 6 — read-only catalogue evidence across several videos (experimental PR)

This new checkpoint supersedes the older statements above that catalogue
access and a multi-clip command are not implemented. It remains on **wi-0155**,
not main. The original manual-reference command remains available and unchanged.
No API, UI, worker, production matcher, identity assignment or schema migration
is involved.

The standalone command accepts a **private case manifest** containing 1–32
explicitly chosen canonical person UUIDs and **2–4** distinct already-local
video frame manifests (1–36 frames per clip). Each clip still has its own
maximum-ten-minute, timestamped coverage frame set. The entire cohort uses
**one** PostgreSQL REPEATABLE READ / READ ONLY evidence snapshot loaded into
memory before video inference begins. The transaction is rolled back before
inference and never performs writes. Only the currently eligible latest
assigned/confirmed faces from non-merged people and current nondeleted photo
revisions participate. Unknown, rejected, reversed, merged, superseded and
incompatible evidence is excluded. The selected SFace model ID, exact SHA-256,
128 dimensions and five-point crop alignment must match the approved model.
At most three independent source-photo exemplars are used **per person**.

Ranking is by person (best exemplar per person), not by reference-photo count;
the runner-up is a different person. A high cosine score, or highest score
among many sampled frames, is NOT proof of identity. There are no calibrated
video identification cutoffs. Diagnostics retain weak/ambiguous rankings
privately without forcing an unknown face into a known person. A per-clip
rank summary is **not** a person count, a track or a verified identification.

### Database role setup, separately performed by the operator

The command accepts **only loopback PostgreSQL** on localhost, 127.0.0.1 or
::1, and requires PHOTOIDENTITY_WI0155_READONLY_CONNECTION_STRING, a separate
connection string for a dedicated role. It rejects any role that can CREATE
a database/schema, write any public table, or elevate to superuser/role admin.
The transaction is also expressly READ ONLY. Do not reuse the application's
normal connection credentials.

A PostgreSQL administrator may use this example in a **separate maintenance
session**, after replacing the database name and selecting a private password.
No role-creation or GRANT command is performed by the experiment:

~~~sql
CREATE ROLE wi0155_reader LOGIN PASSWORD '<supply-private-unique-password>';
GRANT CONNECT ON DATABASE photoidentity TO wi0155_reader;
GRANT USAGE ON SCHEMA public TO wi0155_reader;
GRANT SELECT ON TABLE
    public.assets, public.asset_revisions, public.face_occurrences,
    public.face_crops, public.embeddings, public.people,
    public.person_labels, public.review_actions
TO wi0155_reader;
~~~

Never grant INSERT, UPDATE, DELETE, TRUNCATE, CREATE, role inheritance,
or superuser permissions to this role. Ensure no pre-existing PUBLIC/role
privilege grants defeat least privilege. Database ACL setup is a separate
maintainer/DBA action, not a change to Photo Identity.

### Private case manifest and trial

Use the approved existing FFmpeg frame sampler separately for the already
known baseline and at least one new, diverse, approved **fully local** clip.
Include a known positive and an intentionally *unknown to this selected
gallery* negative. Do not use online-only media, any source in OneDrive, or
unapproved videos. To find canonical people, use the application or a
local read-only SQL session; a read-only query against public.people can
display IDs and names privately. Never commit or share that mapping.

Store this file **outside every Git worktree** in its own local nonsynced
folder, separate from the frame folders. Replace both sample UUIDs and
absolute frame-manifest paths privately; the example UUIDs are placeholders:

~~~json
{
  "schemaVersion": 1,
  "people": [
    { "alias": "P01", "personId": "11111111-1111-4111-8111-111111111111" },
    { "alias": "P02", "personId": "22222222-2222-4222-8222-222222222222" }
  ],
  "clips": [
    { "alias": "V01", "manifest": "C:\\Private\\wi0155\\clip01\\selected-frames.json" },
    { "alias": "V02", "manifest": "C:\\Private\\wi0155\\clip02\\selected-frames.json" }
  ]
}
~~~

P01/P02 and V01/V02 are *operator-supplied aliases*, not people discovered
automatically. Every selected person must have at least one eligible exact-model
exemplar; missing person evidence fails the whole trial rather than silently
assuming a negative outcome. All reports are brand-new private JSON files, never
overwritten and never written into the repository or any input folder.
No names, person IDs, embeddings, original file paths or face images appear
in report output or standard output. Reported frame times, ranks, scores,
aliases and the snapshot token are still private biometric-derived evidence.

~~~powershell
# PowerShell 7.6 — use the separate WI-0155 worktree at merged wi-0155 HEAD.
Set-Location "C:\Kod\codex\Photo Identity Indexer-WI0155"
dotnet test tests/PhotoIdentity.VideoFaceFeasibility.Tests/PhotoIdentity.VideoFaceFeasibility.Tests.csproj -c Release
if ($LASTEXITCODE -ne 0) { throw "WI-0155 tests failed" }
dotnet run --project tools/PhotoIdentity.Docs -- validate
if ($LASTEXITCODE -ne 0) { throw "Docs validation failed" }
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
if ($LASTEXITCODE -ne 0) { throw "Generated docs out of date" }

# EDIT paths and local DB name/port. Keep cases and report in different folders.
$privateCases = "C:\Private\wi0155\catalogue-cases\cases.json"
$privateReport = "C:\Private\wi0155\reports\catalogue-trial-01.json"
if (Test-Path -LiteralPath $privateReport) {
    throw "Choose a new report filename"
}
$readOnlyPassword = Read-Host "Separate WI-0155 read-only DB password" -MaskInput
$env:PHOTOIDENTITY_WI0155_READONLY_CONNECTION_STRING = (
    "Host=127.0.0.1;Port=5432;Database=photoidentity;" +
    "Username=wi0155_reader;Password=$readOnlyPassword"
)
Remove-Variable readOnlyPassword

try {
    $argsForTrial = @("--catalogue-cases", $privateCases,
        "--private-report", $privateReport, "--root", (Get-Location).Path,
        "--detector-confidence", "0.7")
    dotnet run --project tools/PhotoIdentity.VideoFaceFeasibility -c Release -- @argsForTrial
    if ($LASTEXITCODE -ne 0) { throw "Private catalogue trial failed" }
}
finally {
    Remove-Item Env:PHOTOIDENTITY_WI0155_READONLY_CONNECTION_STRING -ErrorAction SilentlyContinue
}
~~~

Only share **aggregate, non-identifying** outcomes. Inspect the report
privately (the operator-held case file maps alias to person). For each clip:
count distinct people actually seen by a human; how many have at least one
usable detected face; which known people have correct rank-1 at least once;
wrong, weak, ambiguous and missing suggestions; false known-person
suggestions for deliberately excluded people; and redundant review observations.
Also note total runtime, missed sampling windows and usable/borderline/
unusable quality. No tool-derived group count equals the number of people.
The CLI does **not** automatically determine the visual quality or human
ground truth. Repeat the complete cohort when the catalogue changes, because
separate invocations do not share a database snapshot.

**Acceptance remains open** until those private real-video observations
are reviewed by the maintainer. The choice between small in-app integration
and a separate video-recognition component remains explicitly deferred.


## Checkpoint 7 — higher-resolution, denser local coverage (experimental)

The private coverage sampler now accepts `-FrameCount 12|24|36|120|180`.
It samples stratified midpoints across the explicitly approved window,
retains the source's aspect ratio, and downscales **only when the longest
side exceeds 3840 pixels**. This replaces the former 1600-pixel cap.
The coverage-mode .NET decoder also uses a 3840-pixel limit; the
non-coverage 12-frame baseline remains at 1600 pixels. No upscaling
or super-resolution is performed. Higher resolution can help detect
distant faces, but cannot recover information absent from the source.

The catalogue-backed evaluation and explicit `--coverage` mode now
accept up to **180** local frames per clip. The ordinary non-coverage
12-frame mode is unchanged. Each still retains the existing 8 MiB
file-size limit and each manifest the 16 KiB limit; oversize inputs
fail closed. A 180-frame run can require substantially more disk,
CPU and RAM. It processes frames sequentially and does not persist
embeddings, write to the catalogue or modify original video files.

For a before/after comparison, retain the old 36-frame manifests and
create a **new** private manifest for the same video:

~~~powershell
$coverage = & .\tools\PhotoIdentity.VideoFaceFeasibility\New-VideoFaceCoverageFrames.ps1 `
    -VideoPath $video `
    -FfmpegPath $ffmpegPath `
    -ApprovePrivateLocalVideo `
    -FrameCount 180

if (-not (Test-Path -LiteralPath $coverage.ManifestPath -PathType Leaf)) {
    throw 'High-resolution coverage extraction failed'
}
$coverage.ManifestPath
~~~

Update only the relevant `clips[].manifest` entry in the **private**
catalogue case file and run the checkpoint-6 read-only evaluation with
a new private report filename. Compare per-video face counts and
person-level review findings; a higher detection count alone does not
prove improved identification. Detector confidence 0.5/0.7/0.9 is
unchanged. Existing private manifests remain valid.

## Checkpoint 8 — source fidelity and temporary-frame lifecycle requirements (2026-11-08)

**Status: requirements and acceptance criteria only; not implemented.**
The operator verified the 180-frame V03 extraction and dimensions locally.
Visual quality improved slightly but remained insufficient for confidently
reviewing distant faces. The complete catalogue evaluation is **deferred**
until the fidelity changes are implemented; do not mark identification
acceptance as passed.

### Grounded V03 source evidence

Approved local FFprobe inspection of V03 reported HEVC, coded dimensions
**1920 × 1080**, pixel format **yuv420p10le** (10-bit 4:2:0), and
**-90° rotation** metadata. Its intended presentation is portrait.
The current 3840-pixel longest-side extraction cap therefore does **not**
explain the poor quality for this source: simply raising that cap cannot
recover detail not present in a 1920 × 1080 source. JPEG encoding, color
conversion, orientation handling, sampling position and the downstream
image decoder must be checked separately. Paused/zoomed playback is a
human visual reference, not proof that the source has higher pixel count.

### New requirements (proposed; scoped to WI-0155 feasibility)

- **WI0155-FID-01 — Preserve source spatial detail.** Extract at native
  decoded video resolution, preserving display orientation and aspect
  ratio without upscaling or unnecessary resampling. Record coded
  dimensions, display dimensions, rotation and extracted dimensions
  privately for reproducibility. A source smaller than the maximum
  permitted dimensions must not be scaled up. A larger source must
  have an explicit, documented bounded policy rather than silent loss.
- **WI0155-FID-02 — Avoid additional lossy intermediate encoding.**
  Provide a lossless (e.g. PNG) or equivalent lossless decoded-frame
  path for comparative experiments. Preserve source-derived color
  fidelity as far as the detector/decoder support it, document
  10-bit-to-8-bit conversion when unavoidable, and avoid implying that
  lossless extraction can restore HEVC-compressed source detail.
- **WI0155-FID-03 — End-to-end decode fidelity.** Verify that the .NET
  image decoder and inference pipeline do not re-downscale frames
  unexpectedly. Record the dimensions actually presented to detection,
  not only those of the extracted file. Distinguish frame sharpness
  from model input-size limits and face-pixel dimensions.
- **WI0155-SAMPLE-01 — Separate sampling from fidelity.** Preserve
  configurable 36/120/180-frame comparisons with equivalent source
  processing. Permit selecting useful timestamps or windows for
  short-lived appearances without treating more detections as
  identification success. Evaluate later whether adaptive or
  motion/quality-based selection is warranted.
- **WI0155-TEMP-01 — Bounded, local, disposable workspace.** Treat
  extracted frames as temporary working data in a dedicated local,
  nonsynced folder, with explicit byte/count/time budgets and
  predictable failure on exhaustion. Never implicitly hydrate an
  online-only OneDrive original or scan unapproved media.
- **WI0155-TEMP-02 — Safe cleanup and recovery.** Clean intermediates
  only after required analysis outputs have been safely persisted and
  verified; define retention for failed/interrupted runs and a
  deliberate retry/cleanup operation. Never delete the original video,
  approved user input, or selected retained outputs. Do not
  automatically delete the operator's existing private evidence.
- **WI0155-ART-01 — Selective retention.** Support explicit retention
  decisions for useful face crops, a representative/poster frame,
  clip boundaries or clips, captions and review metadata as those
  capabilities are introduced. Retained image assets may be
  downscaled/compressed **after** their intended use is established,
  with a reviewable quality policy. These downstream capabilities
  are not promised by the present experiment.
- **WI0155-ARCH-01 — Avoid redundant intermediates.** Evaluate
  streaming decoded frames directly into analysis or reusing a
  decoded frame for multiple approved tasks. Treat this as an
  architectural option, not a prerequisite for the immediate
  source-fidelity experiment.
- **WI0155-SAFE-01 — Preserve isolation.** Changes remain on wi-0155
  or child branches, read-only toward the existing PostgreSQL
  catalogue, with no production background scanning, automatic
  identity assignments, UI/API/worker integration or original
  modification.

### Acceptance evidence for the next increment

1. On the same approved V03 clip, capture source metadata and show
   that lossless extraction yields correctly oriented **1080 × 1920**
   display frames (or explain any metadata-specific deviation),
   without rescaling and without unintended color/orientation changes.
2. Compare one or more *identical timestamps* extracted using the
   previous JPEG path and the new lossless path. Report frame
   dimensions, file sizes, visual face usability and any measured
   detector-input dimensions. Do not claim a quality improvement
   from comparing different moments.
3. Run bounded 36- and 180-frame experiments only after the fidelity
   path is verified. Record disk peak, processing duration and
   detection counts; then review correct/incorrect/unknown catalogue
   rankings privately, using person-level human ground truth.
4. Demonstrate that the temporary workspace respects its configured
   limit and that success/failure/retry cleanup does not touch
   originals, retained outputs, other runs or operator-owned
   pre-existing private evidence. Cleanup lifecycle implementation
   may be a **separate** increment after fidelity verification.
5. Keep existing 12-frame baseline behavior, manifest compatibility,
   explicit approvals and privacy constraints intact; pass focused
   tests and docs validation.

**Implementation order:** (A) source-fidelity investigation and
lossless, native-resolution extraction/decoding; (B) rerun the
controlled V03 comparison; (C) bounded temporary workspace,
safe cleanup and selective retention; (D) decide whether streaming
or more intelligent sampling is justified by measured results.
Do not merge the entire WI-0155 feasibility branch into main as
part of this requirements update.

## Checkpoint 9 — opt-in native-lossless extraction development (2026-11-08)

**Implementation in isolated PR; operator acceptance is deferred.**
The coverage sampler now supports `-FrameEncoding NativeLosslessPng` while
preserving `LegacyJpeg` as its default. It extracts one frame at each of the
existing approved stratified timestamps, with FFmpeg autorotation and **no
scale filter**, encoding PNG instead of JPEG. It is deliberately limited to
source coded dimensions at most 3840 pixels per axis; it does not upscale.
FFmpeg decodes V03's HEVC 10-bit YUV source into **8-bit RGB PNG**; PNG is
lossless after that conversion, **not** a preservation of source 10-bit
precision or a recovery of detail lost in HEVC encoding. The existing
.NET coverage decoder uses a 3840-pixel bound and must be inspected during
local acceptance for effective detector input resolution.

The extractor now probes stream dimensions, codec, pixel format and
rotation; writes `extraction-metadata.json` **beside the private frame
manifest** (not inside the manifest schema); and enforces a configurable
cumulative workspace budget with `-MaximumWorkspaceMiB` (8–2048,
default 1024). The existing 8 MiB **per-frame** limit remains in force.
If a PNG exceeds that cap, or a run exceeds its workspace budget, the
sampler fails explicitly and **leaves partial files for deliberate local
cleanup**. Automatic retention, cleanup, streaming and shared downstream
artifact lifecycle are **not implemented** by this increment.

### Deferred local acceptance: run once the development increment is ready

In the WI-0155 worktree, with `$video3` and the approved `$ffmpegPath`
already set to **fully local** paths:

```powershell
dotnet test tests/PhotoIdentity.VideoFaceFeasibility.Tests/PhotoIdentity.VideoFaceFeasibility.Tests.csproj -c Release
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check

$native = & .\tools\PhotoIdentity.VideoFaceFeasibility\New-VideoFaceCoverageFrames.ps1 `
    -VideoPath $video3 `
    -FfmpegPath $ffmpegPath `
    -ApprovePrivateLocalVideo `
    -FrameCount 180 `
    -FrameEncoding NativeLosslessPng `
    -MaximumWorkspaceMiB 1024

$native.ManifestPath
$native.ExtractionMetadataPath
Get-Content -LiteralPath $native.ExtractionMetadataPath -Raw |
    ConvertFrom-Json |
    Select-Object frameEncoding, codedWidth, codedHeight,
        rotationDegrees, expectedDisplayWidth, expectedDisplayHeight,
        extractedFrameBytes, maximumWorkspaceMiB
```

Verify the actual PNG dimensions on several samples (for V03, expected
portrait **1080 × 1920**), compare the **same timestamps** to legacy
JPEG extraction, and check distant-face usability rather than merely
frame size. Then use a **new** private catalogue case file referring
to the PNG manifest and rerun the approved read-only evaluation;
do not overwrite baseline manifests/reports or publish biometric data.
Record peak disk use, runtime, and whether the detector actually sees
more usable faces. This is a fidelity experiment, not proof of identity
recognition. If the new frames are no better, investigate the detector's
effective input size and source face-pixel dimensions before increasing
sample counts further.

The next substantial increment should implement the separate
`TEMP-02` safe cleanup/recovery and `ART-01` selective retention
requirements, without broadening the present standalone prototype into
production.

## Checkpoint 10 — explicit temporary workspace lifecycle (development; 2026-11-08)

The WI-0155 sampler now creates `wi0155-run.json` as an **ownership
marker before frame extraction starts**, updating it from `extracting`
to `extracted` after the manifest and provenance metadata are written.
This supports identifying interrupted runs without adopting or deleting
pre-existing private experiment folders.

The new `Complete-VideoFaceCoverageRun.ps1` provides **inspection by
default** and **explicit** cleanup only for an immediate child of
`%LOCALAPPDATA%\PhotoIdentity\Temp\wi0155\coverage` with a valid
ownership marker. It rejects linked/offline/reparse-point entries,
unexpected files and completed manifests inconsistent with their frames.
It never recursively deletes a directory. Old unmarked runs are
intentionally **not eligible** for this automated cleanup.

A successful analysis run requires `-VerifiedReportPath` and
`-ApproveCleanup`; the report must be a nonempty regular local file
outside the coverage root. **This checks existence and placement, not
the semantic correctness or provenance of the report**: the operator
must review and approve its successful persistence first. An
interrupted/failed extraction may instead use `-AbandonRun
-ApproveCleanup`, but cannot retain frames or claim a successful
report. The cleanup operation is never automatic.

Optionally specify exact `-RetainFrames` names from a completed
manifest and a pre-created `-RetentionDirectory` **outside the
temporary root**. The tool copies selected originals without resizing,
uses run-ID-prefixed names, refuses overwrite, verifies SHA-256 equality
before deleting intermediates, and leaves the run in place if copying
or verification fails. Future poster/crop/clip/caption selection and
post-use image compression remain separate work; this increment
retains original frame files only.

```powershell
# Inspect a new, marked run; no deletion:
.\tools\PhotoIdentity.VideoFaceFeasibility\Complete-VideoFaceCoverageRun.ps1 `
    -RunDirectory $native.FramesDirectory

# Only AFTER separately verifying the private analysis report and
# confirming that the temporary frames are no longer needed:
$retention = Join-Path $env:LOCALAPPDATA 'PhotoIdentity\WI0155\retained'
New-Item -ItemType Directory -Path $retention -Force | Out-Null

.\tools\PhotoIdentity.VideoFaceFeasibility\Complete-VideoFaceCoverageRun.ps1 `
    -RunDirectory $native.FramesDirectory `
    -VerifiedReportPath $privateReport180 `
    -RetainFrames @('frame-001.png') `
    -RetentionDirectory $retention `
    -ApproveCleanup

# For a deliberately abandoned *new marked* failed run instead:
# .\tools\PhotoIdentity.VideoFaceFeasibility\Complete-VideoFaceCoverageRun.ps1 `
#     -RunDirectory $failedRunDirectory -AbandonRun -ApproveCleanup
```

**Acceptance remains deferred** until the source-fidelity and
temporary-lifecycle increments can be verified together on the
maintainer's Windows 11 / PowerShell 7.6 setup. Confirm inspection
is non-destructive, incomplete and unexpected-file runs fail safely,
retained bytes match their source hashes, no other run or original
changes, and successful completion removes only the approved run.
The broader temporary-data budget across *all* concurrent runs,
semantic report-to-run binding, streaming frames, automated retry
or crash recovery and selective compressed poster/clip/caption
persistence are **not implemented**.

## Checkpoint 11 — detector-input fidelity and isolated multiscale comparison (development; 2026-11-08)

**Implementation ready for CI and future operator acceptance; no real-media
accuracy assertion.** V03's HEVC 1920 × 1080 10-bit source is already
below the 3840-pixel extraction cap. Even with correctly oriented native
1080 × 1920 PNG input, the previously selected default YuNet **single
pass** processes the *entire* frame through the governed **640 × 640**
model input. Native PNG storage therefore cannot, by itself, increase
the face's effective model pixels. A distant face can be only a few
pixels high after this preprocessing.

The standalone WI-0155 CLI and read-only catalogue evaluation now allow
the experimental **opt-in**, already-existing governed YuNet
`MultiScale` pipeline. It runs a whole-frame pass plus spatially
overlapping tiles of **1024** (default) or **640** source pixels,
projected separately to the same 640 × 640 model tensor, and merges
overlapping detections via the existing detector implementation.
This may improve detection of distant faces but increases inference
work and may introduce different duplicate/false detections. It **does
not** change models, similarity thresholds, alignment or any
production setting. `single-pass` remains the default.

The CLI options are `--detector-pipeline single-pass|multi-scale`
and (for multi-scale only) `--tile-size 640|1024`. The private
reference CLI requires existing explicit diagnostics authorization;
the catalogue runner already requires a private report and a
read-only PostgreSQL snapshot. The private diagnostic report records
the detector pipeline, fixed model tensor size, tile size (if used),
**decoded pre-model frame dimensions**, and smallest detected face
width/height in source pixels. Face-pixel counts refer to **detections**,
not manually annotated missed faces; they do not prove that the
model saw original-resolution face detail.

If the extractor's private `extraction-metadata.json` indicates
`NativeLosslessPng`, the experiment validates consistent source
display dimensions and refuses images that the .NET decoder shrinks
or rotates incorrectly relative to the recorded extraction metadata.
Operator-supplied manifests **without** metadata retain compatibility.
These checks are active only in coverage mode; old 12-frame normal
mode stays unchanged. No original or frame file is modified.

### Deferred combined local procedure

After the code is merged into `wi-0155`, and **after** the
maintainer approves a fully local V03 and has verified the PNG
dimensions / no source hydration / local models:

```powershell
# $newCasesPath: private 2–4-video catalogue case file with V03 pointing
# to the SAME approved native PNG manifest in BOTH runs.
# $privateReportSingle and $privateReportTiled: different, new private
# report paths outside Git and temporary extraction folders.
# $wiRoot / $modelDir: existing WI-0155 approved installation paths.

dotnet run --project tools/PhotoIdentity.VideoFaceFeasibility -c Release -- `
    --catalogue-cases $newCasesPath --private-report $privateReportSingle `
    --root $wiRoot --model-dir $modelDir --detector-confidence 0.5 `
    --detector-pipeline single-pass
if ($LASTEXITCODE -ne 0) { throw 'Single-pass private evaluation failed.' }

dotnet run --project tools/PhotoIdentity.VideoFaceFeasibility -c Release -- `
    --catalogue-cases $newCasesPath --private-report $privateReportTiled `
    --root $wiRoot --model-dir $modelDir --detector-confidence 0.5 `
    --detector-pipeline multi-scale --tile-size 1024
if ($LASTEXITCODE -ne 0) { throw 'Tiled private evaluation failed.' }
```

If the first tiled run shows an insufficient change and its runtime /
peak resource use is acceptable, a **third new** report can compare
`--tile-size 640` on the same approved cases; this is more expensive
and is **not** a required first pass. Check each private report's
`clips[].review.diagnostics.detectorProfile` and
`frameDetections[]` for pre-model decoded size, model input size,
tile size, and small face bounding-box sizes. Review correct/unknown/
incorrect person candidate rankings against private ground truth,
not just detection totals. Do not paste scores, identities, file
paths, timestamps or raw biometric findings into PRs or chats.

Complete focused unit tests, docs validation/check and the explicit
temporary workspace inspection, selective retention and cleanup
procedure from checkpoint 10 **after** evaluation and confirmed
private report persistence. Use the same original selected video,
sampling timestamps, decoded PNGs and catalogue cohort for both
detector modes to isolate this new variable.

**Outstanding acceptance:** Real V03 image/model orientation and
dimension checks; recorded precision/quality of small faces; cost and
human-confirmed utility of 1024/640 tiles; actual safe cleanup and
retention; verified private report-to-run association remains
operator-attested, not enforced cryptographically. No production
integration is approved.

## Checkpoint 12 — masked tiled-catalogue failure in combined local verification (2026-11-09)

**Observed operator evidence:** The single-pass read-only evaluation
completed successfully for three selected videos / 252 frames:
74 frames with faces, 101 face detections and 101 embeddings;
46,222.713 ms aggregate analysis time, private report successfully
persisted. The same-cases 1024-pixel multi-scale evaluation terminated
with the runner's previous generic `private read-only catalogue
evaluation failed` message. **The underlying cause is unknown**;
do not mark multiscale acceptance passed or assume the 32-face-per-frame
limit was hit.

**Diagnostic correction:** The catalogue CLI now emits a fixed,
non-identifying `stage` and `category` when it fails. Categories
cover the explicit 32-face safety limit, memory, file I/O/access,
invalid JSON/input/state, cancellation and other runtime failures.
Stage tags can isolate input selection, model integrity, read-only
catalogue snapshot, frame read/decode/fidelity, face detection/limit,
alignment, embedding, candidate ranking, frame integrity and report
persistence. The original exception type, stack and failure behavior
are left intact internally. **No exception message, frame number,
timestamp, clip/person alias, image path, score, embedding, database
connection or other private evidence appears on stdout/stderr.**
The tool does not add database writes, automatic retry or a new
error report.

### Focused retry after this diagnostic PR is merged

**Do not re-extract V03 or rerun the successful single-pass pass.**
Keep the existing approved PNG workspace, private case file and
single-pass private report. Re-run **only** tiled detection using a new
report path, with the same approved least-privilege PostgreSQL login:

```powershell
$retryReport = Join-Path $reportsDir (
    'tiled1024-retry-' + (Get-Date -Format 'yyyyMMdd-HHmmssfff') + '.json'
)

dotnet run --project tools/PhotoIdentity.VideoFaceFeasibility -c Release -- `
    --catalogue-cases $newCasesPath `
    --private-report $retryReport `
    --root $wiRoot --model-dir $modelDir `
    --detector-confidence 0.5 `
    --detector-pipeline multi-scale --tile-size 1024
```

The pre-existing `PHOTOIDENTITY_WI0155_READONLY_CONNECTION_STRING`
variable must already point to the approved local SELECT-only account;
never use application write credentials. Only share the **sanitized
stage/category**, exit status and anonymous aggregate outcome.
Do not share exception details, person rankings or report contents.
A failed run is *not* evidence for detector quality; preserve frames
until failure classification and actual successful comparison.

If the category is `frame-face-limit`, investigate unexpected
candidate volume and false positives first; do **not** silently raise
or remove the bounded 32-face safety gate. If the stage is
`face-detection`, examine the existing governed YuNet tiling and
source-to-tensor path through focused synthetic tests before retrying
full private clips. Other stages require different fixes.
No root-cause claim can be made until the retry identifies the stage.

## Checkpoint 13 — controlled multiscale candidate overflow (2026-11-09)

**Observed:** After PR #772 the maintainer's 1024-pixel tiled
catalogue trial failed at **stage `face-count-limit`,
category `frame-face-limit`**, exit code 1 after 35.6 s.
That proves at least one frame returned more than 32 post-NMS
candidate faces; it does **not** prove that they were real people
or identify the affected frame/video. Successful single-pass
results remain 3 clips / 252 frames / 74 frames with faces /
101 face observations / 101 embeddings in 46.2 s.

**New opt-in experimental behavior:** The catalogue runner now
accepts `--face-overflow-policy retain-top-32` **only with**
`--detector-pipeline multi-scale`. Default `fail` retains
the existing fail-closed 32-face limit for all experiments.
An opted-in frame with more than 32 post-merge detections keeps
only the 32 highest YuNet-confidence candidates, with
deterministic position/index tie-breaking, *before* alignment,
embedding and catalogue ranking. This does **not** raise the
number of faces processed per frame, alter YuNet thresholds
or its existing NMS, or affect the app's normal recognition.

This is an evaluation workaround, **not evidence of
identification quality**. High-confidence false positives can
displace real smaller faces, especially distant people.
The private per-frame diagnostic report records
`rawCandidatesDetected`, `candidatesDiscarded`,
and `facesDetected` (retained candidates), with safe
cross-video aggregate counts `framesWithCandidateOverflow`
and `candidatesDiscarded` in CLI stdout. These are candidate
counts, **not** numbers of distinct people, confirmed faces,
or known identities. Human review must explicitly assess
whether this ranking discards useful people.

### Deferred local trial, using existing private PNGs and case file

Do **not** rerun the successful single-pass evaluation or re-extract V03.
After focused synthetic CI succeeds and the PR merges into
`wi-0155`, retry the existing 1024-pixel tiled evaluation
with a **new** report name, the existing approved
`$newCasesPath` / `$modelDir` / `$wiRoot`, and the
approved read-only PostgreSQL credential:

```powershell
$boundedReport = Join-Path $reportsDir (
    'tiled1024-bounded-' + (Get-Date -Format 'yyyyMMdd-HHmmssfff') + '.json'
)

dotnet run --project tools/PhotoIdentity.VideoFaceFeasibility -c Release -- `
    --catalogue-cases $newCasesPath `
    --private-report $boundedReport `
    --root $wiRoot --model-dir $modelDir `
    --detector-confidence 0.5 `
    --detector-pipeline multi-scale --tile-size 1024 `
    --face-overflow-policy retain-top-32
if ($LASTEXITCODE -ne 0) { throw 'Bounded multiscale evaluation failed.' }
```

Keep the report outside repository/source frames, use a
fresh name, and do not print private ranking or frame metadata
to chat/PRs. Review the raw/retained/discarded counts within
`clips[].review.diagnostics.frameDetections`, using the
operator's private ground truth to compare known people,
unknown people, false detections, and whether dropped candidates
might contain someone useful. If this fails again, the safe
stage/category from checkpoint 12 still applies. Preserve the
temporary PNGs until the evaluation and explicit retention /
cleanup procedure have been verified.

**Not accepted yet:** No actual positive/negative human
classification or performance result is available for tiled
detection. This increment does not establish face accuracy,
does not change single-pass behavior, does not bypass the 32
alignment/embedding cap, and does not alter production,
originals, OneDrive state, or the database.

## Checkpoint 14 — private visual review instead of enormous JSON (2026-11-09)

**Operator evidence after merged PR #775:** Focused tests PASS; bounded
multi-scale 1024 PASS with 4,043 raw face candidates, 3,986 retained,
57 discarded across 17 overflow frames and 207 seconds elapsed.
**Human usefulness and identification correctness remain unreviewed.**
The complete private report is approximately 150,000 text lines; asking
the operator to assess each candidate manually would be unworkable.

**New isolated offline review tool (proposed, no production integration):**
`tools/PhotoIdentity.VideoFaceFeasibility/New-VideoFaceReviewGallery.ps1`
accepts the **existing private catalogue report** and the **existing
private case file**. It does not run inference, connect to PostgreSQL,
read original videos, write identity state, modify extracted images or
change recognition thresholds. It creates a **new local-only** folder
under `%LOCALAPPDATA%\PhotoIdentity\WI0155\review-galleries` with:

- `review.html` — collapsible per-video summaries, per-person ranking
  coverage (including people never top-ranked), a few best-ranked
  frame observations per person, uncertain suggestions and highest
  candidate-overflow frames; uses **small, newly created JPEG review
  thumbnails** and links back to the exact approved original frames.
- `review-worksheet.csv` — a small shortlist of the same observations
  with blank `HumanDecision` and `ReviewerNotes` columns for private
  reviewer input.
- `thumbnails\` — local, explicitly derived *review copies* (not model
  inputs or claimed recognition artifacts). The script creates these
  only for shortlisted frames; it does not duplicate the full frame
  collection or overwrite source material.

**Important limitations:** The existing diagnostic report does not
retain per-face bounding boxes or face crops. Therefore gallery cards
show a **full video frame** and its *face ordinal*, not a marked or
cropped specific face. An ordinal cannot be visually matched to a face
without further implementation. Highest-ranked person aliases and
cosine scores are exploratory, not verified identity assignments.
The `Uncertain suggestions` section is *not* proof of unknown people.
Top-32 confidence selection may discard genuinely useful low-confidence
faces, and the review gallery cannot display discarded boxes.
Human inspection remains required before claiming face discovery or
identification gains. The HTML/CSV/preview images are private
biometric-related material: never commit, upload or share them.

### One-time Windows PowerShell 7.6 review commands (no repeated inference)

After the focused PR merges into `wi-0155`, refresh the independent
worktree. **Use the already successful** bounded 1024 tiled private
report and exactly the case file used to create it:

```powershell
Set-Location 'C:\Kod\codex\Photo Identity Indexer-WI0155'
# Point to EXISTING private local paths; no database connection is needed.
# $boundedReport = '<previous tiled1024-bounded-...json>'
# $newCasesPath = '<previous cases-native180-...json>'

.\tools\PhotoIdentity.VideoFaceFeasibility\Test-VideoFaceReviewGallery.ps1
if (-not $?) { throw 'Synthetic gallery verification failed.' }

$gallery = .\tools\PhotoIdentity.VideoFaceFeasibility\New-VideoFaceReviewGallery.ps1 `
    -PrivateReportPath $boundedReport `
    -PrivateCasesPath $newCasesPath `
    -FramesPerPerson 2 `
    -UncertainFramesPerClip 3 `
    -OverflowFramesPerClip 4 `
    -OpenGallery

$gallery.GalleryPath
$gallery.WorksheetPath
```

Review one or two **useful observations per actually visible person**
rather than every detection. Prioritize wrong matches, missing known
people, unknown people, duplicates and frames where useful evidence
might have been discarded. Copy **only non-identifying aggregate
conclusions** into the work item. Keep extracted V03 PNGs and the
successful original report intact until the review completes.
The review tool's `-OpenGallery` option simply opens the generated
local HTML in the default browser; it does not start a web server.

A synthetic smoke test with local generated PNGs covers bounded frame
shortlisting, thumbnail creation, private CSV/HTML creation, unchanged
source hashes and refusal of out-of-manifest frame references.
CI does **not** necessarily execute this Windows-only PowerShell test;
run it deliberately on Windows before private review.

## Checkpoint 15 — reconstruct face boxes and edit reviews in local HTML (development; 2026-11-09)

**Operator acceptance of PR #778:** synthetic gallery test, HTML generation,
thumbnails/full-frame links and CSV worksheet all **PASS**; **human usefulness
remains not reviewed**. The operator requested face-specific bounding boxes,
crops and editing review decisions directly in the gallery rather than
manually editing a spreadsheet.

### Review decision vocabulary

Each shortlisted observation now has a fixed editable
`HumanDecision` dropdown: `Correct person`, `Wrong person`,
`Unknown person`, `Not a face`, `Cannot tell`, or
`Review not applicable` (for frame-wide overflow observations).
`ReviewerNotes` captures short reasons such as sharpness/visibility,
incorrect candidate, duplicate observation or source-person ambiguity.
**Unknown** means a visible real face not matching the selected known
catalogue; **Cannot tell** means visual evidence is insufficient. A
ranked catalogue alias is not an identity assignment.

The generated HTML supports **direct editing** of these fields. Use
**Save review CSV** to explicitly choose a private local file in a
browser supporting the File System Access API, or download a CSV in
another browser; use **Import saved CSV** to resume. There is **no
silent autosave**: closing the page without saving may lose edits.
The operator must choose a nonsynced private destination (browser
Downloads may be synced). This performs no network calls and never
writes any review action to PostgreSQL.

### Retrospective face geometry — *targeted detection only*

The previous large private catalogue JSON did not persist the
normalized face boxes required to locate a face ordinal in a frame.
Instead of rerunning embeddings, full catalogue evaluation or V03
extraction, the standalone `VideoFaceReviewGeometryRunner` uses
only the small shortlist generated by the gallery. It reads its
`review-selection.json`, the approved private catalogue report and
case manifests, verifies the governed YuNet model SHA-256, and
re-detects only shortlisted local frames using the report's
multiscale tile size and detector confidence. It applies the same
opt-in `retain-top-32` selection and refuses outputs when the
recomputed frame candidate/overflow counts or confidence extremes
disagree with the original diagnostics. The output is a **new
private local JSON file** containing reconstructed normalized boxes
and stable 1-based face ordinals; stdout contains anonymous counts
only. It does not create embeddings, read PostgreSQL, identify people,
contact OneDrive or modify source images. This is still detector
inference, just targeted to the review shortlist; additional processing
time is expected. Because the old report never saved geometry,
a count/score match is evidence of reproducibility but is **not
cryptographic proof** of an identical ordering; manually check any
ambiguous multi-face examples.

The new `-GeometryPath` option on the gallery validates these boxes,
then draws **yellow bounding boxes on local reduced-size frame
previews** and saves **native-pixel face crops with 25% margin**
for the exact retained face ordinal. Crops are private local
review copies and do not change the recognition pipeline or source
quality. On overflow cards it draws the **retained** candidates, not
the discarded ones: dropped candidates were never assigned embeddings
and their boxes are absent from the old report. Correctness still
requires human review.

### Deferred combined local verification — no full inference repeat

After the proposed isolated PR merges into `wi-0155`, use the same
existing `$boundedReport`, `$newCasesPath`, `$wiRoot` and
`$modelDir` from the successful 4,043-candidate run:

```powershell
Set-Location $wiRoot

# Synthetic images only; no private data, ONNX or DB required.
.\tools\PhotoIdentity.VideoFaceFeasibility\Test-VideoFaceReviewGallery.ps1
if (-not $?) { throw 'Gallery synthetic test failed.' }

# New shortlist with explicit selected-frame manifest, no inference.
$shortlist = .\tools\PhotoIdentity.VideoFaceFeasibility\New-VideoFaceReviewGallery.ps1 `
    -PrivateReportPath $boundedReport `
    -PrivateCasesPath $newCasesPath `
    -FramesPerPerson 2 -UncertainFramesPerClip 3 -OverflowFramesPerClip 4

$geometryPath = Join-Path $shortlist.ReviewFolder 'detector-geometry.json'
dotnet run --project tools/PhotoIdentity.VideoFaceFeasibility -c Release -- `
    --review-geometry-selection $shortlist.SelectionPath `
    --catalogue-cases $newCasesPath `
    --private-catalogue-report $boundedReport `
    --review-geometry-output $geometryPath `
    --root $wiRoot --model-dir $modelDir
if ($LASTEXITCODE -ne 0) {
    throw 'Selected-frame private geometry reconstruction failed.'
}

# Generates bounded box overlays and face crops, plus editable HTML.
$review = .\tools\PhotoIdentity.VideoFaceFeasibility\New-VideoFaceReviewGallery.ps1 `
    -PrivateReportPath $boundedReport `
    -PrivateCasesPath $newCasesPath `
    -FramesPerPerson 2 -UncertainFramesPerClip 3 -OverflowFramesPerClip 4 `
    -GeometryPath $geometryPath -OpenGallery

$review.Crops
$review.GalleryPath
```

**PowerShell note:** The above commands use a PowerShell backtick at
the end of each continued line. Run them in PowerShell 7.6; do not
replace these with Bash-style backslashes. The gallery is a standalone local HTML
page using a copied static `review-ui.js` script. Browser-specific
`file://` script/security policies may affect the save button;
this is part of local operator acceptance. If the browser does not
support direct saving, the explicit CSV download/import path is the
supported fallback.

**Review acceptance:** At least one candidate has a visible bounding
rectangle and matching face crop; saved edits can be imported and
preserved; one or more original frames still match their hashes; no
database credentials are needed; private output is kept outside Git.
The actual usefulness and accuracy of catalogue suggestions remain
**unverified** until the operator marks correct/wrong/unknown/not-a-face
and cannot-tell observations. No automatic identity assignment or
production integration is approved.

## Checkpoint 16 — eliminate duplicate reviews and clarify uncertain/overflow (2026-11-09)

**Operator verification for merged PR #780:** C# and PowerShell synthetic
tests PASS; detector-only geometry reconstruction PASS; box overlays
PASS; face crops PASS; direct HTML decisions/notes PASS; CSV save/import
PASS. Human usefulness is **partially useful**. The operator has
reviewed the person-ranked examples but has not yet reviewed
`Uncertain suggestions` or `Overflow / omissions`. They reported
that some face observations appear in **both** the top-ranked and
uncertain lists.

**Cause:** The gallery originally picked person-ranked evidence and
below-threshold/ambiguous evidence independently. The same retained
face (same `clip`, `frameNumber`, `faceNumber`) could therefore
be selected and graded twice. A single frame showing *different*
face ordinals may appear in more than one section, correctly, because
those are independent detections.

**Proposed fix:** The isolated gallery now suppresses exact
face-observation duplicates across `Top-ranked by person` and
`Uncertain suggestions`, always retaining the top-ranked
instance and choosing the next qualifying uncertain example
if possible. A different face ordinal in the same frame remains
eligible. The gallery's **completion indicator counts only
face-specific observations**, not optional frame-level overflow
warnings. Neither the detector output, private report, inference,
face ordinals, bounding boxes, top-32 ranking nor app/DB is changed.

### How to classify the remaining sections

For `Uncertain suggestions`, examine the **specific face crop**:
- `Not a face`: false-positive detector candidate.
- `Unknown person`: a clearly visible real face not among the
  selected known catalogue people. This is not a verified new
  identity assignment.
- `Cannot tell`: image quality, occlusion or angle prevents
  a reliable human judgment.
- `Known person - no reliable match`: the reviewer recognizes
  a **selected known catalogue person**, but the match was
  below-threshold or ambiguous. In private `ReviewerNotes`,
  record the selected person pseudonym (e.g. `P02`) and why
  matching was unreliable. This category is an observed *possible
  false negative*, not automatic correction of the matcher.
- `Correct person` or `Wrong person`: for cases where a
  reliable catalogue person suggestion actually exists, not
  simply the top-ranking alias in a below-threshold case.

For `Overflow / omissions`, **no retained face is being
evaluated**: it is a frame-level warning that raw post-NMS
candidates exceeded the limit of 32. The gallery can draw
retained boxes/crops only; the discarded candidates were not
persisted in the historical report. Human reviewers may leave
these entries untouched or mark `Review not applicable` and
write a note such as `57 candidates discarded in 17 frames
overall; unavailable for face-level judgment`. If a full
frame visibly shows an unboxed human face, add `possible
missed face` in notes, **without asserting** it was one of
the discarded candidate boxes. No false-positive rate or
number of useful discarded people can be inferred from this
section alone.

### Preserve already completed private reviews

**Before changing galleries, save the current HTML decisions to a
private CSV using `Save review CSV`**, outside OneDrive and Git.
The existing CSV columns and row identity (`Clip`, `Section`,
`Frame`, `Face`, `Candidate`) remain unchanged. After the
follow-up PR merges, regenerate a new shortlist and (if needed)
its matching detector geometry, then use the gallery's
`Import saved CSV`. The import restores matching old rows;
new uncertain selections may be blank and need review. Because
a shortlist may change, **do not assume all previous rows
are imported**: check the reported number of matched rows
and inspect the regenerated gallery. Keep the old CSV; never
delete or overwrite it automatically.

The focused Windows PowerShell synthetic test now exercises
both a previously duplicated below-threshold top-ranked face
and a separate uncertain face in the same frame. Review
outcomes and notes remain private, do not update the
canonical identity index, and are not proof of automatic
identification quality.

## Checkpoint 17 — PR #784 acceptance and qualitative V03 limits (2026-11-10)

**Operator local verification:** Windows synthetic tests **PASS**;
deduplication **PASS**; re-created face geometry and visual gallery
**PASS**; existing CSV decision import **PASS**; uncertain suggestions
reviewed **PASS**. HTML human-review usefulness: **useful**.

**Private-cohort observations:** V01 is stated to include P02 and
P03, V02 P01 and P02, V03 P01, P04 and P05. The operator notes
an incorrect P04 identification/ranking for V03. Summary of
per-clip top-ranked candidate observations:

| Clip | Top-ranked counts (P01 / P02 / P03 / P04 / P05) | Best cosine per alias (P01 / P02 / P03 / P04 / P05) | Strong suggestions |
|---|---|---|---:|
| V01 | 1 / 18 / 5 / 4 / 8 | 0.1352 / 0.4962 / 0.5565 / 0.2618 / 0.3878 | 0 |
| V02 | 16 / 11 / 0 / 4 / 7 | 0.4897 / 0.5123 / — / 0.3081 / 0.3791 | 0 |
| V03 | 44 / 115 / 40 / 198 / 3515 | 0.4895 / 0.3682 / 0.3322 / 0.4088 / 0.4903 | 0 |

**Interpretation guardrails:**
- These are nearest-neighbour *top-ranked observations*, not
  counts of recognized people, verified matches or unique faces.
  No clip has any strong review suggestion; all reported maximum
  similarities are far below the existing exploratory 0.80
  review-candidate cutoff. Do not present the leading aliases
  as automatically identified.
- V03's 3,515 P05 top-rank observations do not imply 3,515
  distinct P05 faces or independent observations of P05.
- The operator knows three people appear repeatedly in V03's
  180-frame video, filmed from a one-storey elevated window
  approximately 10 m away. People do not face the lens and
  complete frontal faces are absent; image quality is limited.
  **One of the three people never appears as a face detection
  in the *HTML review shortlist***. This does not yet prove
  all 180 sampled frames lack a valid detector box.
- The operator suspects some of the previously discarded
  57 candidates are real useful faces. The current report
  persisted only retained boxes, so this is unconfirmed:
  counts/relative cosine scores cannot adjudicate discarded
  quality.

**Do not redo** the successful full 252-frame catalogue
run or delete the approved extracted frames, private report,
geometry or reviewed CSV. The next development proposal
should be a distinct opt-in **person-coverage audit**
with a few operator-chosen representative frames, explicitly
including the person absent from the shortlist; separately,
capture a bounded private comparison of *raw post-NMS* and
top-32-retained box coordinates at overflow frames. Keep
review-by-person ground truth local, no identity data in
logs/PRs, no database writes, and do not change production
recognition/thresholds based on these observations alone.

For full requirements and the next evaluation gate, see
`docs/delivery/work-items/WI-0155-video-face-processing-evaluation.md`,
experimental checkpoint 17.

## Checkpoint 18 — actionable three-tier evaluation and voluntary report sharing (2026-11-10)

**Clarification:** The operator does not require a high-confidence
automatic identification for V03. Their intended **video review
flow matches image review**: (1) auto-assign only with satisfactory
similarity **and** margin; (2) otherwise show **2–3 ranked
existing-person suggestions** that a reviewer may confirm/correct;
(3) manually identify or create new people when needed, so reviewed
catalogue evidence can improve future recognition. The tentative
expected distribution is **less than 25% auto, 50–60%
semi-automatic, about 20% manual**; these are not measurements
or per-video quotas, and should not influence model thresholds.
When persons recur across many video frames, require a manageable
number of distinct human review tasks, not one per raw detection.

**Current private data limitation:** `rankedFaces` records the
top alias and cosine score, runner-up score, and gap but **not**
the second alias or third-ranked alias/score. Consequently
the existing local reports cannot establish whether correct
known people reach the top **two or three** suggestions.
A top-1 count or zero strong auto-suggestion count does not
answer that question, and strong auto confidence is not the
appropriate success requirement for oblique V03. A separate,
approved follow-up will need to retain private top-3 ranked
candidates and group repeated faces for review rather than
changing acceptance thresholds.

**Operator-authorized data sharing:** The maintainer has
volunteered to provide their existing local reports to improve
understanding of the human review. Prefer the already **saved,
reviewed CSV** and the matching **bounded private catalogue
JSON**; the prior single-pass report or a few explicitly
approved local face crops can be supplied if they materially
clarify observations. Never upload without the maintainer's
specific action. These files may include private clip/person
aliases, frame identifiers, metadata and biometric-related
scores; do not copy their contents into GitHub, CI logs, PRs,
or public documentation. No PostgreSQL password, database dump,
original video, or entire extraction folder is required. The
previous general `never upload` precaution remains applicable
to unsolicited/automatic publication, not to a user choosing
to attach their own private reports for analysis.

**Next steps:** Await the selected operator files; compare
human decisions with the actual available rankings and report
schema, identify which additional top-3 candidate information
is required, and then propose a narrow, isolated experimental
change. Reuse the same approved local frames, immutable
report and review CSV. Do not rerun 252-frame inference
or merge any recognition behavior into main yet.

The detailed formal acceptance criteria are recorded in
`docs/delivery/work-items/WI-0155-video-face-processing-evaluation.md`,
checkpoint 18.

## Checkpoint 19 — group-based video review: scope and data findings (2026-11-10)

The maintainer confirmed PR #787 merged and clarified the
**intended finished user experience**:
- Present **a small set of person groups per video** rather than
  every detection. A V03 review ideally shows three groups for
  its three people; more groups may be created by viewpoint
  variation, difficult faces or false positives.
- Each provisional group shows a few best, **visually diverse
  face crops** plus **2 or 3 ranked identity suggestions**.
  The reviewer can accept a suggestion, choose another,
  create a new person via the normal person workflow,
  dismiss false positives, or merge/split incorrect groups.
  Groups must not be defined simply by the model's
  top-ranked known-person alias.
- The final identity result is **person present in video**.
  Individual frame and timestamp positions can be internal
  transient processing aids, but the reviewer need not know
  when in the video the person appears.
- Video-derived crops and embeddings are **not permanent
  reference data** and must not train/improve future
  identifications. Ephemeral embeddings for *within-video*
  clustering and ranking remain permitted, with explicit
  cleanup, retention and no canonical DB writes.

### Observations from the three voluntarily supplied reports

The user supplied the existing bounded tiled-1024 catalogue JSON,
single-pass catalogue JSON and the completed review CSV for
private evaluation. Their contents must not be committed,
uploaded to CI, logged or reprinted in the repository. Safe
aggregate outcomes:

| Clip | Single-pass retained candidates | Tiled-1024 retained candidates |
|---|---:|---:|
| V01 | 30 | 36 |
| V02 | 36 | 38 |
| V03 | 35 | 3,912 |

Both runs compared **only 5 manually selected known people**
using **15 total still-photo exemplars**. The actual
production catalogue has 100+ people according to the
operator, so these results **do not evaluate** realistic
full-catalogue top-3 suggestions. Current
`VideoFaceCatalogueCasesLoader.MaximumPeople = 32`;
this existing experimental limit prevents a simple
100+ person trial. Matchers also rank per person, with
at most three reference exemplars per person. The
`rankedFaces` diagnostics omit runner-up and third
**identity labels**: only top-ranked alias and runner-up
score are available.

The human-review CSV has 36 face-specific review observations
and four optional overflow entries. It contains examples of
correct suggestions, wrong suggestions, known people without
a reliable match, poor-angle faces and non-face candidates.
This is **biased reviewer-selected evidence**; it is not
a representative model accuracy estimate. In V03 the
tiled detector generated very many detections, despite
only three recurring real people being visible. Reject the
idea that maximizing model candidate counts is the same
as finding additional unique people.

### Design, performance and safety checkpoints

1. Use a read-only frozen catalogue snapshot of **all
   eligible** reference people, with governed resource
   budgets. Do **not** interpret the existing 5/32 limits
   as the required finished maximum; separately plan
   and test a safe, bounded enlargement. Exclude people
   with no eligible reference vectors from automatic/
   semi-automatic matching, but allow manual identification.
2. Select representative high-quality crops, cluster
   conservatively within each video, prefer extra
   reviewable fragments to unsafe merges, and allow
   explicit manual merge/split/dismiss. A group can be
   unidentified/unknown; do not require a selected
   catalogue identity to form a group.
3. Evaluate full-catalogue ranked top-1/top-2/top-3
   **per human-reviewed person group**, not per-candidate
   hit rate. Auto-assign only under the existing
   governed similarity **and** runner-up margin rules;
   otherwise present suggestions or manual controls.
4. Attach person to video with a unique reviewed
   **video/person presence** record and correction path.
   Do not expose/require appearance timestamps. Do not
   persist video-derived embeddings as canonical
   person reference exemplars, even after the reviewer
   confirms an identity.
5. Preserve the existing reviewed CSV and reports
   outside Git; no repeated full-model run, production
   schema changes or write access in this documentation
   checkpoint. Future grouping/full-catalogue development
   remains in a separate isolated WI-0155 feature branch.

The main work item describes acceptance contracts
`WI0155-GROUP-01` through `WI0155-GROUP-06`.
This checkpoint only establishes requirements and records
anonymous aggregates. It does not implement grouping
or 100+ identity matching.

## Checkpoint 20 — experimental provisional group preview (PR pending; 2026-11-10)

This checkpoint **does not** change any application behavior.
It adds an explicitly opt-in private *group* diagnostic to
the existing isolated catalogue runner, and a new offline
PowerShell group review gallery. Do not run the new full
252-frame experiment unless the maintainer wants the
grouping evidence and the earlier tests are green.

### Focused verification (PowerShell 7.6, after this PR merges)

Start in the already approved isolated WI-0155 worktree
after fetching `origin/wi-0155`. Do not discard local
uncommitted work. Existing approved model files,
private `$newCasesPath` and validated least-privilege
PostgreSQL read-only account are reused:

```powershell
$wiRoot = "C:\Kod\codex\Photo Identity Indexer-WI0155"
Set-Location $wiRoot

dotnet test tests/PhotoIdentity.VideoFaceFeasibility.Tests/PhotoIdentity.VideoFaceFeasibility.Tests.csproj -c Release
if ($LASTEXITCODE -ne 0) { throw "C# group tests failed" }

.\tools\PhotoIdentity.VideoFaceFeasibility\Test-VideoFaceGroupReview.ps1
if (-not $?) { throw "Synthetic gallery tests failed" }
```

These tests require no real media, ONNX models or database.
The synthetic group review is temporary; it checks
source-image hashes, crop export, HTML controls,
CSV worksheet and refusal of invalid boxes.

### Optional V03 real grouping evaluation

Use the same existing approved `$newCasesPath` (the
two-to-four-clip file with V01/V02/V03) and `$modelDir`
you used for PR #780. The old catalogue report is
insufficient to reconstruct groups because it does not
persist the per-face SFace vectors needed to compare
faces across frames. A new inference evaluation is
therefore required, **but not any new video extraction**.

```powershell
if (-not (Test-Path -LiteralPath $newCasesPath -PathType Leaf)) {
    throw "Private case file missing."
}
if (-not (Test-Path -LiteralPath $modelDir -PathType Container)) {
    throw "Approved model directory missing."
}
if (-not $env:PHOTOIDENTITY_WI0155_READONLY_CONNECTION_STRING) {
    throw "The preapproved PostgreSQL SELECT-only connection is not set."
}

$reportsDir = Join-Path $env:LOCALAPPDATA "PhotoIdentity\WI0155\reports"
New-Item -ItemType Directory -Path $reportsDir -Force | Out-Null
$newGroupReport = Join-Path $reportsDir (
    "groups-tiled1024-" + (Get-Date -Format "yyyyMMdd-HHmmssfff") + ".json"
)

$argsForGroupRun = @(
    "run", "--project", "tools/PhotoIdentity.VideoFaceFeasibility",
    "-c", "Release", "--",
    "--catalogue-cases", $newCasesPath,
    "--private-report", $newGroupReport,
    "--root", $wiRoot,
    "--model-dir", $modelDir,
    "--detector-confidence", "0.5",
    "--detector-pipeline", "multi-scale",
    "--tile-size", "1024",
    "--face-overflow-policy", "retain-top-32",
    "--provisional-groups"
)
& dotnet @argsForGroupRun
if ($LASTEXITCODE -ne 0) {
    throw "Isolated private provisional-group run failed."
}

$groupReport = Get-Content -LiteralPath $newGroupReport -Raw | ConvertFrom-Json
$groupReport.clips | ForEach-Object {
    $g = $_.review.diagnostics.provisionalGroups
    [pscustomobject]@{
        Video = $_.alias
        Frames = $_.review.aggregate.framesExamined
        Candidates = $g.observationsConsidered
        ProvisionalGroups = @($g.groups).Count
        Assigned = $g.observationsAssigned
        Unassigned = $g.observationsUnassigned
    }
} | Format-Table -AutoSize
```

A capped count of 64 and large `Unassigned` is
**not a successful result**; it means the cap has been
reached and potentially useful candidates were not
assigned. The resulting aliases `G001` and so on
refer to tentative groups, NOT actual persons.
V03 is challenging and may produce many extra groups;
quantify this before choosing thresholds or another
grouping strategy.

### Build the private group review HTML (no inference)

```powershell
$gallery = .\tools\PhotoIdentity.VideoFaceFeasibility\New-VideoFaceGroupReview.ps1 -PrivateReportPath $newGroupReport -PrivateCasesPath $newCasesPath -ClipAlias V03 -OpenGallery
$gallery | Format-List
```

The gallery selects up to three local crops per
provisional group and shows **no appearance timeline**.
Use `Keep group`, `Merge group` with another G alias,
`Split required`, `Dismiss false detection` or
`Cannot tell` and the private notes field; click
`Save group-review CSV` to keep decisions locally.
The buttons record decisions **only in the private
CSV**, not in the database and not in grouped inference.
The next step must implement validated and reversible
review actions before any production integration.
No person identities or video frames are copied into
permanent reference embeddings.

Local acceptance should report:
**C# tests**, **PowerShell synthetic PASS/FAIL**,
**group count per video**, **V03 unassigned count**,
number of actual real people represented by at least
one group, false-positive groups, necessary merges/splits,
group usefulness and any unusual crop quality.
Keep the new JSON/CSV and all original frames
private and intact; do not include them in the PR.

The previous 5-person scope remains unchanged in this
prototype. Full 100+ person top-2/top-3 catalogue ranking,
automatic identity assignment, executed group merges,
and video/person DB associations are explicitly **not**
part of this checkpoint. More requirements and open
issues are in work-item checkpoint 20.

## Checkpoint 21 — useful V03 coverage, but background groups dominate (2026-11-11)

**Verified by maintainer (PR #789):** C# tests PASS,
Windows synthetic PowerShell gallery PASS,
real grouping PASS, HTML crops PASS, CSV save/import
PASS. All three people in V03 appear in at least
one group. Grouping usefulness **partially useful**:
**most groups are unrelated background objects**.
Exact group counts/unassigned totals were not reported.
Do not classify 64 groups or any observation count
as 64 persons, nor claim detection precision is good.

**Follow-up triage workflow (new optional gallery UI):**
- Generate group HTML from the **same prior private
  grouped report and case file**; no new inference
  is needed merely to test revised HTML review controls.
- Click **Dismiss obvious background** on any
  obviously non-face group. The card is hidden when
  **Hide dismissed** is checked; uncheck to inspect
  or correct a dismissal. The **Show groups with
  3+ observations only** checkbox is a reversible
  *navigation filter*, NOT a face classifier.
  Real faces may have only one observation,
  while background features may recur many times.
- The group export always includes **all groups,
  including hidden/dismissed**. Save CSV explicitly
  and verify reimport. The input CSV columns remain
  identical to PR #789, permitting import into a
  newly generated gallery **only when group IDs and
  per-group observation counts match**.
- The new grouper retains up to **five diverse
  representative crops**, instead of three.
  Older grouped reports containing one to three
  crops remain valid and immediately usable with
  the revised HTML gallery. A new model evaluation
  is needed only to obtain **new** fourth/fifth
  samples from the original unpersisted embeddings.

### Verify locally (PowerShell 7.6; after follow-up PR merge)

```powershell
$wiRoot = "C:\Kod\codex\Photo Identity Indexer-WI0155"
Set-Location $wiRoot

dotnet test tests/PhotoIdentity.VideoFaceFeasibility.Tests/PhotoIdentity.VideoFaceFeasibility.Tests.csproj -c Release
if ($LASTEXITCODE -ne 0) { throw "C# tests failed." }

.\tools\PhotoIdentity.VideoFaceFeasibility\Test-VideoFaceGroupReview.ps1
if (-not $?) { throw "Group gallery synthetic tests failed." }

# Use the successful PR #789 private file and existing cases.
$gallery = .\tools\PhotoIdentity.VideoFaceFeasibility\New-VideoFaceGroupReview.ps1 `
  -PrivateReportPath $newGroupReport `
  -PrivateCasesPath $newCasesPath `
  -ClipAlias V03 -OpenGallery
$gallery | Format-List
```

Check one-click dismissal, hide/show correction,
the optional repeated-observation view filter,
**CSV preservation of hidden groups**, and
save/import. A fresh V01–V03 ONNX run is **not**
needed for this triage acceptance. The previous
3-crop gallery remains valid. To independently
inspect the 5-crop selection on real faces,
an additional approved opt-in run would be
required; **do not rerun it just to prove the
interface controls work**.

The next improvement to actual false-positive
filtering must be measured against labelled
true/false reviewed groups and safeguard detection
of the three actual V03 people. Do not equate
repeated detections, cosine score or group size
with verified face evidence.

## Checkpoint 22 — audited V03 grouping baseline and private metrics (2026-11-11)

**PR #790 local operator feedback:** one-click dismiss,
hide/reveal and review CSV save/import all **PASS**.
The voluntarily attached V03 group CSV has **64
complete group decisions**: 55 dismissed (326 assigned
observations), six proposed merges (six assigned),
and three kept (three assigned). The six merges
terminate at kept groups without cycles. Proposed
outcome: **three groups**, but no merge is executed
by the HTML and no production database is updated.
Every retained/merged group had exactly **one**
face observation; several dismissed background groups
were detected repeatedly. This confirms substantial
false-positive workload and genuine-face fragmentation
despite the previously reported presence of all
three real people in V03's group gallery.

### After the measurement PR merges: local quantitative audit

Use the *operator-saved completed group-review CSV*,
not the initial blank worksheet. The standalone
analyzer reads private inputs, but emits **only
aggregate numbers**; no per-group identifiers,
notes, names, paths, frames or embeddings. It
never connects to PostgreSQL, re-runs ONNX or
edits frames/reports.

```powershell
$wiRoot = "C:\Kod\codex\Photo Identity Indexer-WI0155"
Set-Location $wiRoot

.\tools\PhotoIdentity.VideoFaceFeasibility\Test-VideoFaceGroupReviewMeasurement.ps1
if (-not $?) { throw "Group metric synthetic test failed." }

# Set to the actual saved private review CSV:
$reviewCsv = "C:\Path\To\wi0155-private-group-review-....csv"

$metrics = .\tools\PhotoIdentity.VideoFaceFeasibility\Measure-VideoFaceGroupReview.ps1 `
    -ReviewedCsvPath $reviewCsv -ClipAlias V03
$metrics | Format-List
```

For the submitted CSV the expected counts are:
`TotalGroups=64`, `GroupsReviewed=64`,
`KeptGroups=3`, `MergeGroups=6`,
`DismissedGroups=55`,
`ProposedFinalGroups=3`,
`AssignedGroupObservations=335`,
`KeptOrMergeObservations=9`,
`DismissedGroupObservations=326`.
`ObservationsUnassigned` and `GroupCapacityReached`
remain **null** unless the matching private
grouped JSON report is provided.

For the exact same evaluation, optionally pass
`-PrivateGroupReportPath $newGroupReport`
as well as `-ReviewedCsvPath $reviewCsv` to
reconcile every group ID/observation count and
return the actual unassigned candidate total.
Use the saved PR #789 group report that produced
the review, **not** a new report with different
embeddings/group selection.

This quantitative baseline will distinguish
review workload, true-person coverage and
false-positive candidate grouping in subsequent
independent WI-0155 experiments. A full rerun,
new extraction or production DB access is not
needed to measure the existing report.

## Checkpoint 23 — revised merge target: adaptive all-person coverage (2026-11-11)

This checkpoint records the **new pre-merge evaluation goal**, not
a claim that the implementation has passed it. The authoritative gate
is in the [WI-0155 work item](../delivery/work-items/WI-0155-video-face-processing-evaluation.md).
Keep experimental changes on `wi-0155` or branches targeting it.

### Reuse photo discovery evidence without copying its thresholds

Review the existing photo paths in
`src/PhotoIdentity.Core/Clustering/ProvisionalFaceDbscanClusterer.cs`,
`ProvisionalFaceClusterKnownPersonAdvisory.cs`, and
`docs/architecture/provisional-face-clustering.md`. Evaluate
noise/uncertain grouping, exact-model distances, disagreement and
explicit not-same evidence against the **same reviewed V03 inputs**.
M25's independent still-photo votes do not correspond to adjacent
correlated video samples; photo density parameters should not be
adopted without a labelled video comparison. Separate face-candidate
quality from clustering. In the V03 baseline, **55/64 groups were
dismissed as background**, while **all nine nondismissed groups
were singletons**. A minimum group-size or repeated-detection
filter could discard all accepted true-face evidence.

### Adaptive frame sampling requirements

The current `New-VideoFaceCoverageFrames.ps1` samples one approved
local window using a **manually selected fixed** 12, 24, 36, 120 or
180 evenly spaced frames. Its ten-minute window, 180-frame loader,
8 MiB per-frame bound and owned-workspace lifecycle are current
safety contracts, **not an adaptive quality policy**. WI-0190's
`VideoFrameSamplingPlanner` is a source-free general frame-evidence
planner capped at 24 samples and needs *externally provided* scene
boundaries; do not mistake it for an adaptive video-face sampler.

Implement a versioned private sampling plan with an initial sweep
of the whole approved video, measured or explicitly annotated
motion/scene/entrance/exit signals and selective bounded refinement
of sparse or uncertain areas. Frame counts must vary across videos,
and the report should show duration, segments examined, selected
timestamp distribution, largest uncovered temporal gap, total frames,
processing time, budget exhaustion and skipped/deferred intervals.
Do not stop sampling later intervals because a known person has been
found. Long videos may be processed by separately approved contiguous
windows under bounded resource budgets; never silently imply the
unvisited portion was checked. Preserve original hashes, media
exclusions, local-only/OneDrive safeguards, provenance and cleanup
semantics. Use existing extracted images in regression tests first
where possible, but a genuinely new adaptive sampling test will
need approved source clips and extraction.

### Expanded private comparison and evidence reporting

Keep the already reviewed V03 group CSV + matching grouped JSON
together for the same-run baseline. The existing measurement tool
can reconcile `ObservationsUnassigned` if that JSON is supplied.
For each new approved clip, record human-observed *identifiable*
people, face candidates with an actual visible face, representation
in at least one review group, missing people, false/non-face groups,
split and incorrect-merge groups, distinct review actions needed,
total/temporal coverage and processing time. Include new clips with
motion, strong lighting/colour variations, foreground and background
people, arrivals/departures, known and intentionally unknown people
and no-face negatives.

For full-catalogue matching, never present the current selected
five-people/15-exemplar reports as evidence of correct recognition
against the 100+ person archive. Existing
`VideoFaceCatalogueCasesLoader.MaximumPeople=32` and
`VideoFaceCataloguePersonMatcher.Rank` returning only top alias,
top score and runner-up *score* must change before acceptance.
First prove full-catalogue retrieval on human-verified true-face
crops, independently of group quality; then integrate ranking into
groups. Rank every eligible current person, return the actual top 3
identities/scores per group, and record human-reviewed top-1/2/3
coverage and wrong/unknown outcomes. Review evidence privately;
only aggregate measures may enter GitHub.

### Post-merge scope

A successful evaluation plus explicit maintainer sign-off can
support a controlled merge of the isolated capability. Production
video/person presence writeback, review UI, executable person
association, three-tier canonical auto/suggest/manual workflow,
background scheduling, broad archive scanning and cross-video
tracks are deliberately deferred to later work items. No video
embedding or crop is enrolled into still-photo reference evidence.

No new CLI flags or scripts are implied by this checkpoint;
previous local procedures remain valid only for their **fixed**
frame and **selected-catalogue** evaluations.

## Checkpoint 24 — full-eligible-catalogue top-three code (PR #793, pending local acceptance)

**Development only; not proof that the correct identity is retrieved on real footage.**
The isolated experiment now supports either (a) historical named/pseudonymized
1–32 selected-person cases or (b) explicitly requested **all-eligible**
catalogue cases against a read-only PostgreSQL snapshot of **all current,
unmerged canonical people**, up to an explicit 4,096-person safety ceiling.
For each canonical person, only current eligible photo-origin exact SFace
exemplars participate, at most three distinct-content photo samples.
A person without any eligible exact-model exemplar cannot be recognized by
this experiment and is counted in the canonical-versus-eligible provenance.
Never equate `eligiblePeople` with the full canonical roster.

### Input and private-map contract

For an all-eligible case manifest, omit the `people` property and use
`catalogueScope: "all-eligible"` at the top level, along with two to eight
operator-selected clip manifests in approved local directories. Example
structure (replace paths with actual **private absolute** paths; never
commit populated case manifests):

```json
{
  "schemaVersion": 1,
  "catalogueScope": "all-eligible",
  "clips": [
    {"alias": "V01", "manifest": "C:\\Private\\WI0155\\V01\\frames.json"},
    {"alias": "V02", "manifest": "C:\\Private\\WI0155\\V02\\frames.json"}
  ]
}
```

The command requires `--private-people-map` **only for all-eligible
mode**, in addition to existing `--catalogue-cases` and
`--private-report`. This map is a create-new private JSON containing
`P0001`-style aliases, canonical person UUIDs and optional display names
from the same snapshot. Keep it **outside the repository, the case directory
and all clip folders**. The separate evaluation report contains aliases,
never canonical UUIDs or display names. Neither report nor alias map may
be overwritten, and stdout still contains aggregates only. Handle the
map as personally identifying information: never paste it into PRs, CI
logs or public issue comments.

The evaluator's `rankedFaces[].topCandidates` privately contains up to
three distinct **people**, with scores even below exploratory suggestion
thresholds; this supports measuring recognition opportunities that the
rank-one threshold would otherwise hide. With `--provisional-groups`,
`groupSuggestions` adds up to three person candidates per group based
on its retained representative vectors, using the average of the two
strongest representative-to-person scores when more than one representative
exists. This is a ranking heuristic, NOT a calibrated probability, identity
assignment, confidence guarantee or proof that the candidate is a real face.
One authentic singleton remains rankable. Provisional group false-positive
suppression and full-video adaptive sampling are **still pending**.

### Example PowerShell 7.6 command (run only during explicit local acceptance)

```powershell
$repo = 'C:\Kod\codex\Photo Identity Indexer-WI0155'
$cases = 'C:\Private\WI0155\all-eligible-cases.json'
$report = 'C:\Private\WI0155\all-eligible-evaluation.json'
$map = 'C:\Private\WI0155\all-eligible-people-map.json'
$models = 'C:\Private\ApprovedModels'  # Replace with your verified model directory

Set-Location $repo
dotnet run --project tools/PhotoIdentity.VideoFaceFeasibility -- `
    --catalogue-cases $cases `
    --private-report $report `
    --private-people-map $map `
    --model-dir $models `
    --provisional-groups
```

The existing `PHOTOIDENTITY_WI0155_READONLY_CONNECTION_STRING` must
identify a **separate loopback PostgreSQL SELECT-only account**; the
reader verifies least privilege and a READ ONLY repeatable-read
transaction. Inputs must be approved fully-local frames and approved
exact model files. To compare group-level top-three recall, the operator
must map aliases to ground truth only in the private environment.
A current code-only PR can pass synthetic tests without establishing
real full-archive accuracy. Run the local snapshot once after this
feature has passed CI and before declaring `CATALOGUE-TOP3-01`
accepted; additional detector/grouping/sampler development may proceed
independently before that local acceptance.

## Checkpoint 25 — PR #794: offline top-three group inspection and reviewed accuracy

This checkpoint follows the **merged PR #793** full-catalogue run and the
maintainer's 2026-11-11 V03 review: 241 canonical people, 240 eligible people,
**596** current still-photo exemplars; 180 V03 frames; 3,912 retained
detector candidates, **407 assigned** to 64 groups and **3,505 unassigned**.
The matching 64-row review CSV identified **55 background/non-face groups**
containing **398** assigned detections; three kept and six proposed merges
are nine single-observation genuine-face groups, corresponding to three
identifiable people. Only one genuine group/person received the correct
top-three candidate. V01/V02 human accuracy is still not reviewed.
This is a *failed precision and recognition acceptance result*, not a
database/CLI or JSON-structure regression.

PR #794 provides **no new detector, face-crop, grouping or identity
algorithm**, only more reliable human diagnostic access. It does not use
PostgreSQL, approved ONNX models, FFmpeg, OneDrive or source media to
calculate reviewed accuracy. The group gallery now displays the actual
up-to-three **private pseudonyms** and scores from a PR #793 report;
a score is not a face-validity or identity probability. Old rankless
reports remain inspectable. The revised offline measure validates
that a CSV, its group JSON and private person alias map describe the
same experiment/snapshot, and produces **aggregate-only** top-one/top-three
human-review accuracy and group-level score/quality signal comparisons.

### Windows PowerShell 7.6: no-inference verification after merge

Use your existing separate `wi-0155` worktree and previously generated
**matching** V03 full-catalogue JSON, case JSON, private person map and saved
group CSV. Do **not** rerun 180-frame inference just for this step.

```powershell
$wiRoot = 'C:\Kod\codex\Photo Identity Indexer-WI0155'
Set-Location $wiRoot
git fetch origin
git switch wi-0155
git pull --ff-only origin wi-0155

# Synthetic fixtures: no personal media, SFace, DB or network.
.\tools\PhotoIdentity.VideoFaceFeasibility\Test-VideoFaceGroupReview.ps1
if (-not $?) { throw 'Ranked gallery synthetic test failed' }
.\tools\PhotoIdentity.VideoFaceFeasibility\Test-VideoFaceGroupRankAccuracy.ps1
if (-not $?) { throw 'Private rank audit synthetic test failed' }

# Replace these four values with your EXISTING matching local files.
$casesPath = 'C:\Private\WI0155\cases\cases.json'
$reportPath = 'C:\Private\WI0155\reports\evaluation.json'
$mapPath = 'C:\Private\WI0155\reports\people-map.json'
$csvPath = 'C:\Private\WI0155\reports\saved-group-review.csv'

$gallery = .\tools\PhotoIdentity.VideoFaceFeasibility\New-VideoFaceGroupReview.ps1 -PrivateReportPath $reportPath -PrivateCasesPath $casesPath -ClipAlias V03 -OpenGallery
$gallery.Groups
$gallery.UnassignedObservations

# Safe even before identity labels are populated: missing recall is null.
$comparison = .\tools\PhotoIdentity.VideoFaceFeasibility\Measure-VideoFaceGroupRankAccuracy.ps1 -ReviewedCsvPath $csvPath -PrivateGroupReportPath $reportPath -PrivatePeopleMapPath $mapPath -ClipAlias V03
$comparison | Format-List
```

If you want measured human top-three accuracy, **import the saved CSV**
into the new group gallery. For the nine reviewed genuine-face groups,
fill `SelectedPerson` with the **correct private P0001/P0002... alias**
from the separate people map (repeat the same alias across groups
representing the same person), or `UNKNOWN` only for a genuine face
with no eligible catalogue identity. Do not type real names/UUIDs.
Keep false-positive groups dismissed and without a person alias.
Save a **new** CSV under your private local folder, set `$csvPath`
to that new file and rerun the aggregate measurement (not inference).
Unlabelled genuine groups remain unmeasured; the tool deliberately
reports **null**, not zero, when no known-person labels exist.

Report back only `InputEvidenceValidated`, `KnownPeople...` counts,
`CorrectTop1Groups`, `CorrectTop3Groups`, false-group exploratory
lead counts, group/class medians and the assigned/unassigned totals.
Keep the private JSON, person map, individual rankings and any
annotated group CSV **outside Git, CI, chats and public issue comments**.
A review CSV must come from **the same exact inference report**:
the 2026-11-10 335-assigned baseline must not be paired with
2026-11-11 407-assigned diagnostics.

The purpose of the class-level aggregate medians is to decide whether
the reviewed true-face and background groups have any separable
confidence, geometry-quality or similarity signals before attempting
a non-destructive face-validation rule. Do not tune fixed cutoffs from
the nine genuine singleton groups or infer that the **3,505 unassigned**
detections were background. Even if these diagnostics look promising,
a later targeted local inference comparison must still prove true-face
retention, reduced review effort and useful person-level top three.
Full-duration adaptive video sampling remains a separate gate.

## Checkpoint 26 — zero-inference review-priority comparison (PR #795)

PR #794's **local private human-label verification** is complete. The
snapshot included 241 canonical people, 240 eligible people and 596
eligible SFace still-photo exemplars. All nine genuine V03 singleton
groups were privately labelled: correct top-one in one group, correct
top-three in one group and correct person represented in one of three
distinct known people. The other two known people were absent from
top-three suggestions. The V03 detector generated 3,912 candidate
observations, of which only 407 entered 64 groups and 3,505 remained
unassigned. The reviewer dismissed 55 groups as background.

The median detector confidence, quality proxy and top catalogue cosine
were respectively **0.5954 / 0.1080 / 0.3208** for dismissed non-faces,
versus **0.6531 / 0.1949 / 0.4137** for retained true-face groups.
Although these medians differ, there are only nine positive groups
and no evidence that any single threshold can distinguish every true
face. In particular, the exploratory catalogue 0.80 cosine threshold
is above all reviewed group scores, including real people. These
scores are **not** calibrated face probabilities.

### Compare existing reviewed V03 groups without model inference

**Do not rerun** the 180-frame catalogue inference, video extraction or
PostgreSQL snapshot for this step. Use the same private, fully-local
JSON/CSV/map from the successfully accepted PR #794 run. All three
input paths must be outside Git and from the same saved cohort.

```powershell
$wiRoot = 'C:\Kod\codex\Photo Identity Indexer-WI0155'
Set-Location $wiRoot
git fetch origin
git switch wi-0155
git pull --ff-only origin wi-0155

# Zero-personal-media deterministic regression.
.\tools\PhotoIdentity.VideoFaceFeasibility\Test-VideoFaceReviewPriority.ps1
if (-not $?) { throw 'Review priority synthetic comparison failed' }

# Replace these placeholders with the EXISTING matching private files.
$csv = 'C:\Private\WI0155\review\annotated-v03.csv'
$report = 'C:\Private\WI0155\reports\full-catalogue-report.json'
$map = 'C:\Private\WI0155\reports\people-map.json'

$comparison = @(.\tools\PhotoIdentity.VideoFaceFeasibility\Compare-VideoFaceReviewPriority.ps1 `
    -ReviewedCsvPath $csv `
    -PrivateGroupReportPath $report `
    -PrivatePeopleMapPath $map `
    -ClipAlias V03)

$comparison |
    Select-Object Policy,ReviewBudgetGroups,TrueFaceGroupsShown,
        TrueFaceGroupsNotShown,BackgroundGroupsShown,
        DistinctKnownPeopleShown,DistinctKnownPeopleWithCorrectTop3Shown |
    Format-Table -AutoSize
```

The command produces **only aggregate scenario metrics**; it never
displays group aliases, person pseudonyms, names, source paths,
individual similarities, image data or embeddings. The comparator
requires a completely reviewed keep/merge/dismiss worksheet;
unresolved or cross-snapshot evidence is rejected rather than
silently counted as background. If human identity labels are
incomplete, known-person counts are `null`, not invented zeroes.

Six fixed experimental priorities are compared at review budgets of
8, 16, 32, 48 and 64 groups (plus the available full group count).
`Time-spread + quality` round-robins between four temporal buckets
based on representative frame positions. This is a **review order**,
not a face-rejection rule or true adaptive sampling.

**Interpretation:** a smaller review budget is useful only if
it reduces background-group review work while retaining all
three known visible people and the reviewed singleton groups.
The upper bound is still the **64 already assigned groups**:
3,505 unassigned candidate observations have no group labels
and cannot be credited as background or included in a full-video
person-recall denominator. Even a zero-miss scenario here cannot
prove a safe global face filter. A future deliberately controlled
local inference test is needed to validate any new group selection
or grouping-cap strategy against those unassigned observations,
other videos and the missing two people.


## Checkpoint 27 — deterministic, staged adaptive frame planner (experimental)

A **planning-only** component, `VideoFaceAdaptiveSamplingPlanner`, advances
SAMPLING-01 without relying on private V03 analysis or waiting for the
counterfactual PR #795 review. It is deliberately **not wired to the
production application, the video extractor or the catalogue runner**.
Existing fixed-frame manifests and all prior grouping/identity baselines
remain untouched.

- Stage one selects deterministic timestamps throughout the entire current
  window, with consecutive sample gaps at most eight seconds. It does not
  stop when a known person appears. A window is at most ten minutes and
  requests no more than 180 frames.
- For a longer video, `HasMoreWindows` remains true and the caller must
  explicitly plan the next window starting at `WindowEndMs`. Finishing
  the first window does not imply complete video coverage.
- A separately supplied, bounded second-stage cue list can request more
  sampling near already-planned frames showing possible new/poorly
  observed faces, entrance/exit, uncertain detections, scene changes or
  motion. Refinement is deterministic, prioritizes diverse cue anchors,
  never removes the coarse pass, never exceeds 180 frames per window,
  and reports `RefinementBudgetReached` if there is more proposed work.
- Cues are constrained to frames already in the plan, not an arbitrary
  guessed timestamp. No actual scene detector, face-event classifier,
  frame extraction or continuation executor exists yet. There is
  **no guarantee of detecting short appearances between samples**;
  subsequent local evaluations must quantify such misses.
- The plan contains timestamps only. No real source paths, photo/video
  bytes, embeddings, people, model access, PostgreSQL, identity writes,
  OneDrive hydration or automatic processing are involved.

### Synthetic verification now; private validation later

```powershell
Set-Location 'C:\Kod\codex\Photo Identity Indexer-WI0155'
git fetch origin
git switch wi-0155
git pull --ff-only origin wi-0155

dotnet test tests/PhotoIdentity.VideoFaceFeasibility.Tests/PhotoIdentity.VideoFaceFeasibility.Tests.csproj -c Release --filter 'FullyQualifiedName~VideoFaceAdaptiveSamplingPlannerTests'
if ($LASTEXITCODE -ne 0) { throw 'Adaptive sampling planner tests failed.' }

dotnet run --project tools/PhotoIdentity.Docs -- validate
if ($LASTEXITCODE -ne 0) { throw 'Docs validation failed.' }
```

These tests cover full-window temporal bounds, explicit multi-window
continuation, deterministic priority under permuted cue input,
preservation of all coarse timestamps, the frame cap and rejection of
invalid/unobserved cues. **No private files are needed** for these tests.
Do not run adaptive video-face inference with these timestamps yet:
the CLI has no adaptive extraction/continuation integration.

**Acceptance still open:** wire the planner to an explicitly approved
local source extractor behind a separate opt-in switch, analyze
observed content (including possible short appearances), compare
duration/frame/runtime/review costs to V01–V03 and several diverse
new clips, preserve all human-identifiable people and private
identity boundaries, and explicitly handle incomplete long-video
coverage. Keep the 64-group overflow problem and top-three recall
quality failure separate; this planner does not fix either.


## Checkpoint 28 — opt-in adaptive timestamp extraction (experimental development)

This step connects checkpoint 27's **source-free** C# adaptive planner to
the **existing, explicitly approved local** `New-VideoFaceCoverageFrames.ps1`
extractor. It is a new `-AdaptiveSampling` switch; without it, the
historical fixed 12/24/36/120/180 midpoint timestamps and existing
manifest format are unchanged. There is still **no automatic content
analysis, person tracking, database write, production API/worker integration,
OneDrive hydration or scheduled archive scan**.

- The extractor first requires `-ApprovePrivateLocalVideo`, rejects
  offline/reparse/on-demand files, and uses only the exact supplied local
  FFmpeg/FFprobe installation. FFprobe supplies the approved clip duration.
- In adaptive mode, it asks the isolated .NET
  `--adaptive-sampling-plan` command to plan the **next complete window**
  (up to ten minutes). The planner requests 1–180 temporal frames,
  initially no more than eight seconds apart; a clip at most one hour
  may require multiple separately approved windows.
- `-WindowStartSeconds` selects a window explicitly. Returned
  `HasMoreWindows` and `NextWindowStartSeconds` tell the operator
  whether the remainder is **not yet processed**. No automatic
  continuation or success claim for unvisited windows is made.
- Optional `-AdaptiveCuesPath` supplies observed movement, scene
  change, uncertain-face, possible entrance/exit or poorly observed
  face cues from previously sampled frames. The source-free CLI
  accepts only strict schema-version-1 JSON, bounded to 256 entries
  and 16 KiB. Every cue **must** anchor to a timestamp in the
  planner's initial window pass. It cannot contain person IDs,
  name fields, invented frame positions or identity labels.
  A second extraction at these refined timestamps currently creates
  a **new** owned run and re-extracts coarse frames; do not assume
  same-run reuse or automatic event detection.
- The same existing `frames.json` schema is written with **window-relative**
  timestamps for the unchanged 180-frame recognition loader.
  Metadata now also records sampling mode, coarse count,
  refinement-budget exhaustion and long-video continuation.
- The 8 MiB per frame, at-most-180 frames per run, 8–2048 MiB
  user-selected workspace budget, source-dimension limit and
  existing opt-in `Complete-VideoFaceCoverageRun.ps1` ownership/cleanup
  behavior are preserved. Partial failures remain for deliberate review;
  no automatic cleanup is introduced.

### Consolidated local review (later; not performed by this PR)

In PowerShell 7.6 within the isolated worktree:

~~~powershell
Set-Location 'C:\Kod\codex\Photo Identity Indexer-WI0155'
git fetch origin
git switch wi-0155
git pull --ff-only origin wi-0155

dotnet test tests/PhotoIdentity.VideoFaceFeasibility.Tests/PhotoIdentity.VideoFaceFeasibility.Tests.csproj -c Release --filter 'FullyQualifiedName~VideoFaceAdaptiveSampling'
if ($LASTEXITCODE -ne 0) { throw 'Adaptive sampler tests failed.' }
dotnet run --project tools/PhotoIdentity.Docs -- validate
if ($LASTEXITCODE -ne 0) { throw 'Documentation validation failed.' }

# Reuse the previously authorized already-local source and approved FFmpeg.
# Set these paths LOCALLY; no actual path or private output belongs in Git.
# $video = 'D:\Private\approved-local-video.mp4'
# $ffmpeg = 'C:\ApprovedFFmpeg\ffmpeg.exe'
$first = .\tools\PhotoIdentity.VideoFaceFeasibility\New-VideoFaceCoverageFrames.ps1 `
    -VideoPath $video -FfmpegPath $ffmpeg `
    -ApprovePrivateLocalVideo -AdaptiveSampling `
    -FrameEncoding NativeLosslessPng -MaximumWorkspaceMiB 1024

# Inspect only aggregates; the new run still requires human face review.
$first | Select-Object FrameCount,CoarseFrameCount,HasMoreWindows,
    RefinementBudgetReached,WindowLengthSeconds

# If HasMoreWindows is true, the NEXT window is still unexamined:
# $second = .\tools\PhotoIdentity.VideoFaceFeasibility\New-VideoFaceCoverageFrames.ps1 `
#     -VideoPath $video -FfmpegPath $ffmpeg `
#     -ApprovePrivateLocalVideo -AdaptiveSampling `
#     -FrameEncoding NativeLosslessPng `
#     -WindowStartSeconds $first.NextWindowStartSeconds
~~~

A private cue JSON file, **only after** reviewing its matching initial
window's timestamped frames, has this shape (the timestamp must equal
one of that window's actual initial planned **absolute** timestamps):

~~~json
{
  "schemaVersion": 1,
  "cues": [
    {
      "observedFrameTimestampMs": 100,
      "kind": "PossibleNewOrPoorlyObservedFace"
    }
  ]
}
~~~

The `100` above is merely a schema example and may not be a valid cue
for the selected window. The accepted kinds are `Motion`, `SceneChange`,
`UncertainFace`, `PossibleEntranceOrExit` and
`PossibleNewOrPoorlyObservedFace`. Cues are local private evidence.
No person labels, source images, frame-specific scores, reference
mapping, precise private timestamps or cue files should be uploaded
or pasted into public discussions.

**Real acceptance remains pending:** Verify approved source hashes,
orientation/colour, manifest loading, per-window time coverage,
detector runtime/crop quality, any short appearances missed between
samples, workspace byte budget and explicit cleanup. Compare adaptive
runs against the same source fixed-frame V01–V03 baselines and several
diverse additional videos. Without content cues, this increment is
duration-adaptive **coarse coverage only**; it has not solved V03's
55 false groups, 3,505 unassigned observations or its poor full-catalogue
top-three rank accuracy.


## Checkpoint 29 — derive bounded refinement cues from previous detector evidence (experimental)

An additional **optional offline stage** turns the *existing* per-frame
detector diagnostics of one completed **adaptive coarse-only** window
into a strict, small, private cue JSON compatible with checkpoint 28.
This is a separate `--derive-adaptive-cues` command; it neither reads
the source video nor runs models or opens PostgreSQL. The command
reads only the per-frame detection counts, minimum detector confidence,
smallest face width, and discarded-candidate count from the previously
saved one-clip diagnostics report. It does **not** interpret any person
alias, similarity, top-three ranking, embedding or reviewed identity.

### Exploratory selection rule

- A change in detected face count between consecutive coarse frames
  triggers `PossibleEntranceOrExit` for **both** sides. Face visibility
  at the first frame may also merit a boundary cue. Detector jitter
  and background false positives can cause these signals; **no actual
  entrance, distinct person or correct face is implied**.
- A detector candidate below 64 decoded pixels in width gives a
  `PossibleNewOrPoorlyObservedFace` cue. Otherwise discarded candidates
  or minimum detection confidence below 0.70 give `UncertainFace`.
  These are exploratory *review allocation* thresholds, **not**
  validated probabilities or rules for rejecting detections.
- A clip with no detected faces yields no cue, and a face that appears
  entirely between two coarse samples may still be missed. Neither
  condition proves that the original video contains no people.
- A continuously stable visible detection does not consume all
  refinement slots: the first coarse sample is the only such boundary
  cue. Candidate/face identities do not affect selection.
- Input is accepted only from a **complete, marked, already extracted**
  window using the original coarse planner timestamps. The extractor
  records the absolute `samplingWindowStartMs` in its private metadata,
  while the original frame manifest and private detector report use
  window-relative timestamps. The command verifies them against each
  other and the deterministic coarse plan, requires the actual local
  frame files to remain safely available, and rejects partial, wrong
  window, modified or refined plans. This is structural provenance,
  **not a cryptographic binding** of the report to particular image
  bytes; the maintainer must still preserve evidence/source hashes.
- The new cue output is created without overwriting an existing file,
  outside the repository and temporary frame workspace. Only
  `schemaVersion`, `observedFrameTimestampMs` and the cue kind are
  emitted; no people, aliases, vectors, coordinates or confidence
  values appear in the output or CLI logs. The original diagnostics
  report remains private and unchanged.

### Deferred consolidated PowerShell 7.6 acceptance

Update `wi-0155` and run the synthetic tests and documentation
validator first:

~~~powershell
Set-Location 'C:\Kod\codex\Photo Identity Indexer-WI0155'
git fetch origin
git switch wi-0155
git pull --ff-only origin wi-0155
dotnet test tests/PhotoIdentity.VideoFaceFeasibility.Tests/PhotoIdentity.VideoFaceFeasibility.Tests.csproj -c Release --filter 'FullyQualifiedName~VideoFaceAdaptiveCueDerivationTests|FullyQualifiedName~VideoFaceAdaptiveSampling'
if ($LASTEXITCODE -ne 0) { throw 'Adaptive cue tests failed' }
dotnet run --project tools/PhotoIdentity.Docs -- validate
if ($LASTEXITCODE -ne 0) { throw 'WI-0155 docs validation failed' }
~~~

After selecting a **specific already-local, approved** video, approved
FFmpeg/models, and previously approved local reference manifest, run
the following private sequence. Do not paste the paths, individual
timestamps, person labels or diagnostics contents into GitHub.

~~~powershell
# Requires previously set LOCAL $video, $ffmpeg, $referenceManifest,
# $modelDirectory, plus a separate private $reportFolder outside the
# repository and extracted frame folder. Use NEW output names each time.
$coarse = .\tools\PhotoIdentity.VideoFaceFeasibility\New-VideoFaceCoverageFrames.ps1 `
    -VideoPath $video -FfmpegPath $ffmpeg -ApprovePrivateLocalVideo `
    -AdaptiveSampling -FrameEncoding NativeLosslessPng
if ($coarse.CoarseFrameCount -ne $coarse.FrameCount) {
    throw 'Expected an unrefined coarse-only run'
}

$report = Join-Path $reportFolder ('wi0155-coarse-' + [guid]::NewGuid().ToString('N') + '.json')
dotnet run --project tools/PhotoIdentity.VideoFaceFeasibility -c Release -- `
    --manifest $coarse.ManifestPath --root (Get-Location).Path `
    --model-dir $modelDirectory --references $referenceManifest `
    --private-report $report --diagnostics --coverage
if ($LASTEXITCODE -ne 0) { throw 'Coarse offline diagnostics failed' }

$cues = Join-Path $reportFolder ('wi0155-cues-' + [guid]::NewGuid().ToString('N') + '.json')
dotnet run --project tools/PhotoIdentity.VideoFaceFeasibility -c Release -- `
    --derive-adaptive-cues --manifest $coarse.ManifestPath `
    --private-diagnostics $report --private-cues-output $cues `
    --root (Get-Location).Path
if ($LASTEXITCODE -ne 0) { throw 'Private cue derivation failed' }

# Second extraction of the same approved window. It makes a NEW owned run
# and currently duplicates the coarse frames rather than reusing them.
$refined = .\tools\PhotoIdentity.VideoFaceFeasibility\New-VideoFaceCoverageFrames.ps1 `
    -VideoPath $video -FfmpegPath $ffmpeg -ApprovePrivateLocalVideo `
    -AdaptiveSampling -AdaptiveCuesPath $cues `
    -WindowStartSeconds $coarse.WindowStartSeconds `
    -FrameEncoding NativeLosslessPng

$coarse | Select-Object FrameCount,CoarseFrameCount,HasMoreWindows
$refined | Select-Object FrameCount,CoarseFrameCount,RefinementBudgetReached,HasMoreWindows
~~~

The **original coarse run and private report must remain intact** until
verification and any desired refined comparison are complete. Do not
auto-delete either extraction. Use the existing explicit run-completion
tool from checkpoint 10 only after separately verifying reports,
retained evidence and hashes.

This step enables a first two-pass **detector-responsive** workflow,
but is not self-running, not a scene-change or motion estimator, and
is not proof of missing-person recall. New videos and all windows
need separate human review. Existing V03 failures—55 dismissed
background groups out of 64, 3,505 unassigned observations and only
one of three distinct known people correctly ranked top-three—are
unchanged. Do not integrate `wi-0155` into `main` on this evidence.


## Checkpoint 30 — bounded visibility into 64-group overflow (experimental; 2026-11-12)

An opt-in `--unassigned-audit` flag has been added to the **existing,
read-only full-catalogue experiment**, and is permitted **only with**
`--provisional-groups`. The legacy 64-group algorithm, score thresholds,
ranking, face detection, detector overflow policy, 180-frame manifest
and report structure remain unchanged unless the new flag is used.

### What is collected

When the grouper reaches 64 groups and a candidate cannot be assigned,
it still increments `observationsUnassigned`. If the explicit audit is
enabled, it *also* considers that candidate's **existing private
source-frame crop metadata** for a fixed-memory sample:

- The current clip's frame timestamp range is divided into 12
  deterministic strata. Each retains at most eight candidate crops
  with the strongest current **review-quality proxy**, at most two
  from one frame, giving at most **96 sampled observations** per clip.
- Neither the other unassigned candidates nor their counts are
  discarded. The private `provisionalGroups.unassignedAudit` shows
  `totalUnassignedObservations`, `sampledObservations`,
  `unrepresentedObservations`, `strataRepresented` and the bounded
  `sampledCrops`. These are candidates, **not person groups** or
  assigned face observations.
- Sampling the overflow does not retain extra embedding vectors,
  use catalogue person rankings, change any existing group or
  identity suggestion, reject any supposed background face, write
  to PostgreSQL, or scan additional media. The new per-candidate
  frame numbers, bounding boxes, timestamps and quality values are
  **private diagnostic evidence** and never appear in stdout.
  The CLI prints only the sample total and represented-strata count.
- The cap is **not removed**: a sample of 96 cannot prove that
  the remaining thousands of unassigned detections contain no
  useful people. In particular, a high-quality background false
  positive can displace a small authentic face. The audit allows
  a bounded *human look* at overflow rather than hiding all of it.
- The existing gallery still focuses on 64 provisional groups.
  It does **not yet** provide a dedicated overflow-crop annotation UI,
  automatically promote sampled overflow to face groups, or implement
  the still-photo DBSCAN comparison. Those belong to later increments.

### Deferred consolidated Windows 11 / PowerShell 7.6 verification

After this PR is merged into `wi-0155`, the following needs **no
private media or PostgreSQL** and can be batched with previous synthetic
verification:

~~~powershell
Set-Location 'C:\Kod\codex\Photo Identity Indexer-WI0155'
git fetch origin
git switch wi-0155
git pull --ff-only origin wi-0155

dotnet test tests/PhotoIdentity.VideoFaceFeasibility.Tests/PhotoIdentity.VideoFaceFeasibility.Tests.csproj -c Release --filter 'FullyQualifiedName~VideoFaceUnassignedAuditTests|FullyQualifiedName~VideoFaceProvisionalGroupingTests'
if ($LASTEXITCODE -ne 0) { throw 'WI-0155 audit tests failed' }

dotnet run --project tools/PhotoIdentity.Docs -- validate
if ($LASTEXITCODE -ne 0) { throw 'WI-0155 docs validation failed' }
~~~

For the later **real V03 comparison**, use the previously approved
fully-local V03 extracted manifest and existing read-only case
definitions. **Do not replace** baseline JSON/CSV or user-reviewed
group labels. Use **new private filenames** for report and person map,
and the same approved face model settings, detector pipeline,
model hashes, catalogue snapshot selection and other parameters as
the baseline:

~~~powershell
# Set these locally from your existing approved environment/cases.
# $cases, $models, $newPrivateReport, $newPrivatePeopleMap
dotnet run --project tools/PhotoIdentity.VideoFaceFeasibility -c Release -- `
    --catalogue-cases $cases --private-report $newPrivateReport `
    --private-people-map $newPrivatePeopleMap --model-dir $models `
    --provisional-groups --unassigned-audit
if ($LASTEXITCODE -ne 0) { throw 'Private overflow audit failed' }

# stdout contains only aggregate totals, not faces, timestamps or people.
# Inspect the new report ONLY locally; it contains private face crop geometry.
~~~

For selected-catalogue cases, omit `--private-people-map`; if the
baseline used `--detector-pipeline multi-scale` or
`--face-overflow-policy retain-top-32`, also repeat those exact
existing flags. A fresh read-only snapshot may differ from the
historical baseline: compare source hashes, eligible catalogue
snapshot and real group counts before attributing differences
to this audit. **No extra inference run is necessary before the
maintainer is ready for the consolidated local review.**

**Human acceptance is still open.** Record whether useful genuine
singletons or any additional identifiable people are visible among
the bounded overflow crop samples, whether the sampling proxy
overrepresents background false positives, and how many original
groups still need dismissal. Do not claim `FACE-QUALITY-01`,
`GROUP-QUALITY-01` or full person coverage based on the audit alone.


## Checkpoint 31 — reference-free adaptive detector diagnostics, verified V03 audit (2026-11-13)

**Maintainer verification received after merged PR #799:**

- Updated experimental branch `wi-0155` at merge commit `a43db2bb`.
  Focused release C# test assembly: **84 passed, 0 failed**; docs
  `validate` and `generate --check` passed.
- The separately reviewed V03 six-policy group-priority comparison
  (PR #795) completed with the same approved private group evidence.
  At eight reviewed groups, quality-first reveals six of nine genuine
  groups but only two of three people; catalogue-similarity-first
  reveals all three people but only five of nine genuine groups and
  zero correct top-three person suggestions in that review budget.
  At 32 groups, arrival and catalogue-similarity each show nine of
  nine genuine groups and 23 background groups. **No safe candidate
  suppression or person-recall rule has been established.**
- Full eligible catalogue opt-in overflow audit (PR #799): three
  cases, 252 frames, 241 canonical/240 eligible people, 3,986
  detections and embeddings, 17 frames with overflow and 57 detector
  candidates discarded. V03 exactly reproduces the existing baseline:
  **180 frames, 3,912 candidates, 64 groups, 407 assigned, 3,505
  unassigned**. The new sampler retained **96 private examples across
  all 12 strata**, with 3,409 unrepresented; original group count,
  assigned/unassigned counts and exact group details all matched.
  **PR #799 real-run behavior accepted, not grouping quality.**
- First expressly approved V03 adaptive coarse-only extraction: **22
  frames** over a **166.68-second** video window, largest observed
  inter-frame sample gap **7,928 ms**, no remaining window. The
  maintainer has not yet supplied original-video hash comparison,
  crop orientation/colour review, detector cues, or a refined
  extraction result. These remain **pending**.
- Prior script for `--diagnostics --coverage` required manually
  selected `references.json` and consequently could not execute
  the intended detector-only adaptive pass without the obsolete
  selected-reference experiment. This was an **interface gap**;
  historical references are not intended as the reference source
  for current WI-0155 recognition. Reuse the full eligible people
  catalogue (read-only) for final identity evaluation.

### New explicitly requested detector-only bridge

The new `--detector-only` CLI examines only operator-approved,
already-extracted **local frames** using the verified YuNet model.
It does **not** load SFace, still-image reference manifests, person
identities, PostgreSQL, OneDrive or an original video. Its private
create-new report contains detector counts, candidate overflow and
minimal per-frame diagnostics in the `aggregate.framesExamined` /
`diagnostics.frameDetections` shape required by the existing
`--derive-adaptive-cues` command. Source frames are preflighted,
fidelity checked and checked for modifications during inference.
It preserves the existing governed 32-candidate overflow behavior;
opt-in multiscale retention is available explicitly.
There is no ranking, tracking, person assignment, face rejection
or source modification. The two-pass adaptive workflow is still
**operator initiated**, not a background workflow.

### Consolidated PowerShell 7.6 continuation — replace earlier Step 5C

Use the **existing** approved coarse adaptive extraction in
`$coarse.ManifestPath`. You do NOT need `$referenceManifest`.
Reuse your verified `$wiRoot`, `$modelDir`, `$video` and `$ffmpeg`.
All generated reports and cues must be in a **new private folder
outside Git and the frame workspace**. Run after merging this PR
into `wi-0155` and completing its CI checks.

~~~powershell
Set-Location 'C:\Kod\codex\Photo Identity Indexer-WI0155'
git fetch origin
git switch wi-0155
git pull --ff-only origin wi-0155

dotnet test tests/PhotoIdentity.VideoFaceFeasibility.Tests/PhotoIdentity.VideoFaceFeasibility.Tests.csproj -c Release
if ($LASTEXITCODE -ne 0) { throw 'WI-0155 detector-only regression tests failed' }

# $coarse must still be the preserved successful coarse-only run object
# with its actual frames/metadata and completed ownership marker.
# $modelDir must be your already-verified YuNet model directory.
$privateDir = Join-Path $env:LOCALAPPDATA (
    'PhotoIdentity\WI0155\adaptive-detector-' +
    (Get-Date -Format 'yyyyMMdd-HHmmssfff')
)
New-Item -ItemType Directory -Path $privateDir -Force | Out-Null
$coarseReport = Join-Path $privateDir 'coarse-detector-only.json'
$cuePath = Join-Path $privateDir 'derived-adaptive-cues.json'

dotnet run --project tools/PhotoIdentity.VideoFaceFeasibility -c Release -- `
    --detector-only --manifest $coarse.ManifestPath `
    --private-report $coarseReport --root (Get-Location).Path `
    --model-dir $modelDir --detector-confidence 0.5 `
    --detector-pipeline multi-scale --tile-size 1024 `
    --face-overflow-policy retain-top-32
if ($LASTEXITCODE -ne 0) { throw 'Approved YuNet coarse diagnostics failed' }

dotnet run --project tools/PhotoIdentity.VideoFaceFeasibility -c Release -- `
    --derive-adaptive-cues --manifest $coarse.ManifestPath `
    --private-diagnostics $coarseReport `
    --private-cues-output $cuePath --root (Get-Location).Path
if ($LASTEXITCODE -ne 0) { throw 'Private adaptive cue derivation failed' }

$refined = .\tools\PhotoIdentity.VideoFaceFeasibility\New-VideoFaceCoverageFrames.ps1 `
    -VideoPath $video -FfmpegPath $ffmpeg `
    -ApprovePrivateLocalVideo -AdaptiveSampling `
    -AdaptiveCuesPath $cuePath `
    -WindowStartSeconds $coarse.WindowStartSeconds `
    -FrameEncoding NativeLosslessPng -MaximumWorkspaceMiB 1024

[pscustomobject]@{
    CoarseFrames = $coarse.FrameCount
    RefinedFrames = $refined.FrameCount
    AddedFrames = $refined.FrameCount - $coarse.FrameCount
    CueCount = @((Get-Content -LiteralPath $cuePath -Raw |
        ConvertFrom-Json).cues).Count
    RefinementBudgetHit = $refined.RefinementBudgetReached
    HasMoreWindows = $refined.HasMoreWindows
} | Format-List
~~~

The multiscale settings above match the historical V03 exploratory
detector configuration; they are *not* a recommended or calibrated
face-detection cutoff. Running this isolated detector command does
not require the environment variable
`PHOTOIDENTITY_WI0155_READONLY_CONNECTION_STRING`. For later
**identity recognition** on additional sampled frames, use the
**existing full-eligible-catalogue read-only experiment** and create
a separate, operator-approved case/manifest/report. Do not enrol
video frame crops as photo exemplars.

**Pending acceptance:** Compare original video's SHA-256 before/after;
inspect orientation, colour and a human-visible face sampling
difference on coarse versus refined frames; confirm bounded counts,
privacy and source integrity. Refinement cannot claim person coverage
or make an unknown person known without human-reviewed evidence from
several diverse approved videos. Do not integrate `wi-0155` into
`main` based only on structural evidence.


## Checkpoint 32 — PR #800 real V03 adaptive refinement verified (2026-11-13)

**Maintainer local verification after PR #800 merged into `wi-0155`:**

- Automatic tests: **PASS** (the maintainer did not report individual test totals).
- The approved, already-local V03 video yielded **22 adaptive coarse
  frames** over **166.68 seconds**, maximum coarse sample gap **7,928 ms**,
  with **no second window**. The same coarse frames were preserved.
- New reference-free YuNet `--detector-only` mode examined all 22
  frames, with detector candidates on all 22; **486 raw candidates**,
  **473 retained**, **13 discarded** under explicit multiscale
  `retain-top-32`, elapsed **12,110.831 ms**. Private diagnostics
  were written and the existing `--derive-adaptive-cues` command
  successfully produced **22 cues**, without a manually curated
  `references.json` or PostgreSQL catalogue connection.
- Explicit refined extraction completed: **180 total frames**
  (**22 coarse + 158 new**), over the same 166.68-second window.
  `RefinementBudgetReached=True` and `HasMoreWindows=False`.
  **180/180-frame bound PASS**. Private extracted frame bytes:
  **569,296,333** (~543 MiB), within the configured 1,024 MiB
  workspace limit. All 22 original coarse **timestamps remained**
  and their **SHA-256 hashes were unchanged**.
- The original video **SHA-256 matched before and after the refined
  extraction**. Note: this verification measures the refined step
  only unless a separately retained hash from *before* initial
  coarse extraction is compared.
- Human inspection: no apparent differences in colour, orientation,
  or image quality; all sampled frames looked visually correct.
  Three people appear in frame for virtually the entire video,
  so coarse temporal coverage alone likely would already capture
  each. **One person was seen with their entire face visible
  only at a brief moment while leaving the frame; those
  unobstructed-face frames were in the *refined* set, not the coarse
  set.** This establishes at least one useful sampling difference,
  not a proven increase in correctly recognized people or ranking
  quality.

**Outcome:** real Windows-host end-to-end **SAMPLING-01 mechanics PASS
for this V03 clip** (approved local extraction, reference-free
detection and cue generation, bounded refinement, source integrity,
private outputs, human visual inspection). **General quality and
efficiency acceptance remains OPEN.** The 22/22 cue saturation
exhausted the 180-frame window budget and consumed 569 MB despite
heavy background false-positive detections. Additional frames were
human-useful at one important moment, but neither recognition
improvement nor the necessity of all 158 added samples is established.
Do **not** infer a suitable cutoff or auto-dismissal rule.

**Suggested next measurement:** Compare human-reviewed, full-face
observations and correct known-person top-three retrieval for the
coarse and refined windows, keeping catalogue reference evidence
solely in the existing read-only PostgreSQL photo catalogue.
Evaluate other approved videos with transient appearances before
tuning cue priority/number of refinement samples. Retain private
artifacts outside Git and do not integrate `wi-0155` into `main`
without separate production acceptance.


## Checkpoint 33 — PR #802: human-review bridge for the 96 unassigned candidates (2026-11-13)

The prior read-only PR #799 V03 run preserved **3,505 unassigned
observations** after the 64-group ceiling and sampled **96 candidate
crops in 12 temporal strata**, but until this PR the sampler's metadata
had no human-review interface. Do not assume those unmatched detections
are genuine people or that all are background. The 96 are
**quality-prioritized, temporally stratified examples**, not an
unbiased random sample. Their observed face/background percentages
CANNOT be multiplied by 3,505.

The new, separate offline PowerShell gallery renders the **existing**
samples as a small face crop and a bounded source-context thumbnail
with a rectangle. It accepts the *same* approved private full-catalogue
report and cases JSON produced for PR #799; it checks report and manifest
consistency, per-frame and temporal sample bounds, geometry and fully
local inputs. It never runs YuNet/SFace, opens videos/OneDrive,
loads the people database, modifies source images, or creates identity
assignments. Only review copies and an initially empty local CSV are
created under LOCALAPPDATA.

The controls let the maintainer review each sample as **Real face,
Not a face, Cannot tell** or leave it unreviewed. For a clearly
recognized real face, a private P-alias can optionally be added; if
the person is confidently not in the eligible catalogue, use
`UNKNOWN`. Do not use real names. Review CSV save/import is
operator-initiated and local only; the measurement script checks
all sample IDs, frame and face ordinals and classifications against
the original report before reporting aggregate counts.

### Local verification (after PR #802 merges to wi-0155)

From your already updated PowerShell 7.6 WI-0155 worktree:

~~~powershell
Set-Location 'C:\Kod\codex\Photo Identity Indexer-WI0155'
dotnet run --project tools/PhotoIdentity.Docs -- validate
if ($LASTEXITCODE -ne 0) { throw 'Docs invalid' }
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
if ($LASTEXITCODE -ne 0) { throw 'Generated docs stale' }

.\tools\PhotoIdentity.VideoFaceFeasibility\Test-VideoFaceUnassignedReview.ps1
if (-not $?) { throw 'Synthetic private audit review failed' }

# Reuse the approved V03-containing PR #799 private files.
# No models, credentials, people map or video are required.
# $auditReport = actual full-eligible report produced with --unassigned-audit
# $casesPath   = its exact matching existing private catalogue-cases JSON
foreach ($file in @($auditReport, $casesPath)) {
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) {
        throw 'Approved private WI-0155 audit input is unavailable'
    }
}

$review = .\tools\PhotoIdentity.VideoFaceFeasibility\New-VideoFaceUnassignedReview.ps1 `
    -PrivateReportPath $auditReport -PrivateCasesPath $casesPath `
    -ClipAlias V03 -OpenGallery
$review | Select-Object SampledCrops,TotalUnassignedObservations,
    UnrepresentedObservations,TemporalStrataRepresented | Format-List

# Label the 96 samples in the private browser gallery, save the reviewed
# CSV locally, and use its actual absolute file path here.
$reviewCsv = 'C:\replace-with-actual-private-reviewed-audit.csv'
$stats = .\tools\PhotoIdentity.VideoFaceFeasibility\Measure-VideoFaceUnassignedReview.ps1 `
    -ReviewedCsvPath $reviewCsv -PrivateGroupReportPath $auditReport -ClipAlias V03
$stats | Format-List
~~~

Expected V03 gallery accounting from the existing PR #799 report:
`SampledCrops=96`,
`TotalUnassignedObservations=3505`,
`UnrepresentedObservations=3409`,
`TemporalStrataRepresented=12`. After review, report only the
measurement command's **aggregate** output. Never share the HTML,
crops, original private JSON, CSV contents, names or P-aliases.

The quality-retention counters at detector confidence 0.70 and crop
quality proxy 0.50 are **retrospective comparisons of labelled
sample observations only**; neither is a validated face threshold.
Even if most sampled items are background, the unrepresented
3,409 observations remain unknown. This increment improves visibility
and provides a labelled cohort for evaluating better detector
validation and DBSCAN/other grouping policies; it does NOT lower the
false-group burden or establish a safe suppression policy.

**Local acceptance pending** until the operator has verified the
gallery, manual labels, matching CSV import/export, source-frame
integrity and aggregate report against actual V03 evidence.


## Checkpoint 34 — real unassigned-face labels; M25 DBSCAN shadow comparator (2026-11-13)

**Confirmed operator evidence after PR #802 (full 96-sample V03 audit):**
96/96 reviewed; **94 genuinely human face crops, 2 background crops,
0 uncertain whether a face exists**. The original 46 “Cannot tell”
labels meant **real faces whose owners could not be identified**,
NOT uncertain face validity. The maintainer therefore preserved an
original identity-conservative CSV and separately corrected the
face-validity CSV (94/2/0). Both measurement runs validated against
the original private audit report; the corrected aggregate included
**82/94 real faces with detector confidence >= 0.70** (12 below),
**0/94 with crop-quality proxy >= 0.50**, no known-person aliases
and no unknown-person aliases. Zero aliases is **missing identity
annotation**, not evidence that the faces were unknown to the catalogue.
An actual face must not be described as “unknown identity” solely
because it was unclear.

The 96 samples are **not random**: the PR #799 sampler favors its
crop-quality proxy within 12 temporal strata. Consequently, these
observed percentages are NOT detector precision and **cannot be
extrapolated** to the other 3,409 unassigned observations.
Nevertheless, the corrected evidence proves that valid face
observations exist beyond the chronological 64-group ceiling. The
sample offers no validated confidence/quality rejection threshold.

### New experimental comparison: `--shadow-dbscan`

M25's `ProvisionalFaceDbscanClusterer` implements bounded exact
cosine-distance DBSCAN with explicit noise and “not the same person”
constraints. This PR reuses that computation ONLY as a **separate,
offline, opt-in shadow comparison** while the existing all-eligible
video experiment already has the SFace vectors in memory. It does
not copy M25's three-independent-photo known-person voting or its
still-image policy values. The exploratory video policy has
cosine distance **0.18** (similarity ~0.82), min-neighbors **2**,
min-cluster-size **2**, chosen for a controlled structural
comparison against the original group cosine threshold—not
calibrated for people recognition or safe face rejection.

- Captures at most 180 frames x 32 candidates = **5,760** volatile
  embeddings per clip. Every detector-retained observation enters
  the shadow comparison; no prefilter based on confidence/size.
  The production M25 sparse-neighbor edge cap **2 million** remains
  fail-closed.
- All simultaneous detections from one frame get pairwise **not-same**
  constraints (bounded <=100,000). The replay checks that no resulting
  cluster contains two faces from the same frame; it fails rather
  than silently merging them.
- **Single-observation faces are allowed as noise**; noise is counted
  and NEVER treated as discarded/background/unknown persons.
  The number of formed clusters, their members, noise, co-presence
  violations, and the number of clusters not represented in the
  bounded 64-group review shortlist are all explicitly accounted.
  Noise also has a bounded, temporally stratified private crop audit
  (up to 96 samples); any unsampled noise remains explicitly counted.
  This is biased review evidence and is never a face-rejection rule.
- Private JSON may include up to **64 shadow group records** with
  source-frame crop geometry and up to three *catalogue-derived*
  candidate aliases/scores per group for **human review only**.
  The extra groups and their members are counted even when the
  shortlist cannot display them. Neither SFace embeddings,
  canonical IDs, names, original paths, nor M25's suggestion
  voting are serialized.
- Baseline `provisionalGroups`, `unassignedAudit`, original individual
  top-three ranks and existing private review workflows **remain
  unchanged**. The shadow results are separately stored in
  `clips[].review.diagnostics.shadowDbscan` ONLY with explicit
  `--shadow-dbscan`. No archive writes, video identity enrollments,
  worker scheduling, or main-branch changes are authorized.

### Optional read-only local acceptance (after PR merge)

This run computes the new comparison from local approved V01–V03
frame manifests and one read-only eligible production people
snapshot. It **does repeat** the old ONNX experiment, because the
prior private JSON appropriately contains no SFace vectors to
replay. The existing approved `$casesPath`, `$modelDir` and
`PHOTOIDENTITY_WI0155_READONLY_CONNECTION_STRING` must be available.
Do not run without a restored explicit, SELECT-only DB connection
and approved fully-local frames. Use the existing original V01–V03
case manifest (not V03-only; two or more cases required).

~~~powershell
Set-Location 'C:\Kod\codex\Photo Identity Indexer-WI0155'
dotnet test tests/PhotoIdentity.VideoFaceFeasibility.Tests/PhotoIdentity.VideoFaceFeasibility.Tests.csproj -c Release
if ($LASTEXITCODE -ne 0) { throw 'Focused experimental tests failed' }

if (-not $env:PHOTOIDENTITY_WI0155_READONLY_CONNECTION_STRING) {
    throw 'Existing approved SELECT-only WI-0155 PostgreSQL connection required'
}
foreach ($path in @($casesPath, $modelDir)) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw 'Missing previously approved private cases or verified models'
    }
}
$shadowRoot = Join-Path $env:LOCALAPPDATA (
    'PhotoIdentity\WI0155\shadow-dbscan-' +
    (Get-Date -Format 'yyyyMMdd-HHmmssfff')
)
New-Item -ItemType Directory -Path $shadowRoot -Force | Out-Null
$shadowReport = Join-Path $shadowRoot 'full-catalogue-shadow.json'
$shadowMap = Join-Path $shadowRoot 'private-people-map.json'

dotnet run --project tools/PhotoIdentity.VideoFaceFeasibility -c Release -- `
    --catalogue-cases $casesPath --private-report $shadowReport `
    --private-people-map $shadowMap `
    --root (Get-Location).Path --model-dir $modelDir `
    --detector-confidence 0.5 --detector-pipeline multi-scale `
    --tile-size 1024 --face-overflow-policy retain-top-32 `
    --provisional-groups --unassigned-audit --shadow-dbscan
if ($LASTEXITCODE -ne 0) { throw 'Private shadow DBSCAN experiment failed' }

$private = Get-Content -LiteralPath $shadowReport -Raw | ConvertFrom-Json
$private.clips | ForEach-Object {
    $group = $_.review.diagnostics.provisionalGroups
    $shadow = $_.review.diagnostics.shadowDbscan
    [pscustomobject]@{
        BaselineGroups = @($group.groups).Count
        BaselineAssigned = $group.observationsAssigned
        BaselineUnassigned = $group.observationsUnassigned
        ShadowClusters = $shadow.clusterCount
        ShadowClusterMembers = $shadow.clusterMembers
        ShadowNoise = $shadow.noiseObservations
        ShadowNoiseSamples = @($shadow.noiseAudit.sampledCrops).Count
        ShadowGroupsNotShown = $shadow.groupsNotRepresentedForReview
        ShadowHiddenGroupMembers = $shadow.membersInUnrepresentedGroups
        SameFrameCollisions = $shadow.sameFrameCollisions
        AllAccounted = ($shadow.clusterMembers + $shadow.noiseObservations) -eq
            $shadow.observationsEvaluated
    }
} | Format-Table -AutoSize
~~~

**Acceptance:** baseline V03 group counts remain 64/407/3505;
all shadow observations are accounted for, same-frame collisions
remain zero, private outputs stay outside Git, source files are
unchanged, and the shadow run reports explicit noise/omissions.
No requirement is accepted solely because shadow group count is
smaller. Review representative crops and person coverage with
known human labels before deciding to substitute an algorithm.
This increment does not yet solve the false-background burden or
the 1/3 known-person top-three failure.
