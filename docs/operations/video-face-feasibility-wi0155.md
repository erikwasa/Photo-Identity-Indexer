# WI-0155 — isolated offline video-face feasibility checkpoint

This experimental tool remains on the long-running WI-0155 integration
branch (wi-0155), **not main**. All development PRs target wi-0155
until a specific **near-completion maintainer decision** to either integrate
video recognition into Photo Identity or keep it as a separate component.
No video recognition is added to the normal production API, worker,
UI or media processing flow merely by merging a prototype PR.
PRs #740, #741, #745, #748 and #751 are merged into wi-0155.

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
