# WI-0155 — isolated offline video-face feasibility checkpoint

This experimental tool lives on the long-running `wi-0155` integration branch,
not on `main`. Feature PRs target `wi-0155` until the maintainer chooses a
small production slice or closes the feasibility experiment. **PRs #740, #741 and #745 are merged into `wi-0155`; checkpoint 4 adds an opt-in timeline-coverage experiment.**

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
