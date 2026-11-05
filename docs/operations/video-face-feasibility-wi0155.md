# WI-0155 — isolated offline video-face feasibility checkpoint

This experimental tool lives on the long-running `wi-0155` integration branch,
not on `main`. Feature PRs target `wi-0155` until the maintainer chooses a
small production slice or closes the feasibility experiment. **PR #740 is
merged into `wi-0155`; the current checkpoint adds an optional reference
comparison and an explicit private review report.**

## Scope and limits

The prototype accepts **one private JSON manifest** naming **1–12 already
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
directory components, and two byte-identical photos cannot be used as a
reference and a video frame. The tool compares SFace cosine embeddings
in memory and writes a **brand-new, private JSON review report** to a
separate folder only when both `--references` and `--private-report`
are present. It refuses existing reports and paths inside the repository
or either input folder. Normal stdout remains aggregate-only.

Review fields are sampled frame number/time, detected face ordinal,
candidate alias (or null), cosine score, runner-up score, score gap and
`reviewStatus`. Possible values: `review-candidate`,
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

The project has **no PostgreSQL, worker, API, OneDrive or video-file
dependency**. It reads only the manifest, explicitly named still frames
and already-installed approved models. It does **not** create crops,
embeddings on disk, identity assignments, catalogue writes, or any
automatic video source or OneDrive hydration. Outputs exclude file
paths, face images, vectors, person IDs and identifiers. Repeated
observations are aggregated, not published.

This is **not** yet a video face-recognition feature. Known-person
suggestions, representative stills, sensitivity comparisons and any
production persistence must be decided after a private human pilot.
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
selected frame still offers a useful person view. Only later consider an
approved, **read-only** private known-person reference comparison.
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
