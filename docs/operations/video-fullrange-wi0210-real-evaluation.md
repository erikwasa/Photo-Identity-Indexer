# WI-0210 — guarded real-video full-range proxy evaluation

This runbook verifies **one deliberately selected, already-local,
current, nonexcluded Rec.709 `yuvj420p` revision** in a cohesive
single real-video evaluation. It uses only the unregistered
`wi0210-rec709-fullrange-720p-evaluation-v1` recipe. It is NOT a
production worker, source hydrator, enqueue command or published
playable proxy. A successful run still needs **human visual review**
before the maintainer can approve the separate production integration.

## Previously accepted checkpoints

- Archive survey: 12 isolated Rec.709 `yuvj420p` pixel-only candidates.
- Maintained Windows host: 12/12 locally present, size matching.
- Private shortlist: 12/12 and no originals opened or hydration.
- One explicitly authorized real original: exact-class gate and
  registered SHA-256 **verified**; stable source metadata,
  no OneDrive hydration, jobs or derivatives.
- Synthetic actual-tool full→limited Rec.709 roundtrip: passed.

The new workflow removes the need to rerun separate local
source-proof, FFprobe and synthetic commands. Instead it rechecks
the original's SHA while staging and again after encoding.

## Safety and execution contract

The CLI command is `video compatibility real-evaluate`.
Its **default is dry-run**: no database connection, source
read, external tool, stage or derivative. To run real media,
the operator must supply **all three separate switches**:
`--apply --read-local-bytes --retain-review-proxy`.

When expressly opted in, it requires a real current revision from
the exact pixel-only class, checks local read-only PostgreSQL
with source-copy exclusions and schema currentness, rechecks
stopped/paused video controls and photo priority throughout,
and uses the existing Windows `FILE_FLAG_OPEN_NO_RECALL`
stager with registered SHA-256 checking. FFprobe and FFmpeg
run **only on the private staged copy**, under digest-pinned
executables, fixed file-only arguments, Windows single-process Job
Object memory/CPU/process budgets and measured I/O/scratch limits.
Actual `color_range=pc`, BT.709 primaries/transfer/matrix,
pixel format, codec, audio, dimensions and rotation are
independently checked before the conversion. Result is
independently validated as a 720p-or-smaller, faststart,
H.264/AAC, BT.709 **limited-range** MP4. The source is rehashed
after conversion and before declaring success.

The review copy is retained *only* with explicit opt-in as
`review-proxy.mp4` in a uniquely marked **private OS-temp child**.
It contains real private video content: do not upload,
share, index, copy to OneDrive, or leave it indefinitely.
There is no database migration, enqueue, durable receipt,
worker enablement, published derivative, HDR admission,
poster/frame change or policy widening. This is an
**evaluation**, not evidence of a kernel disk/network sandbox.

## Local verification — only AFTER the PR merges into main

Do not test a PR branch. CI covers docs validation/generation,
compilation, parser/gate/output tests and disposable PostgreSQL.
On the maintained Windows host, run the following in PowerShell
after the PR is merged:

### 1. Update main and locate approved pinned tools

~~~powershell
cd "C:\Kod\codex\Photo Identity Indexer"
git switch main
git pull --ff-only origin main

$db = "photoidentity_main"
$envName = "PHOTOIDENTITY_POSTGRES_CONNECTION_STRING"

$pinnedRoot = Join-Path $env:LOCALAPPDATA "PhotoIdentity\Tools\FFmpeg"
$pins = @(
    Get-ChildItem -LiteralPath $pinnedRoot -Directory |
        Where-Object {
            $exe = Join-Path $_.FullName "ffmpeg.exe"
            $probe = Join-Path $_.FullName "ffprobe.exe"
            (Test-Path -LiteralPath $exe -PathType Leaf) -and
            (Test-Path -LiteralPath $probe -PathType Leaf) -and
            ((Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash -eq $_.Name)
        }
)
if ($pins.Count -ne 1) {
    throw "Select exactly one previously approved digest-pinned FFmpeg directory."
}
$ffmpeg = Join-Path $pins[0].FullName "ffmpeg.exe"
$ffprobe = Join-Path $pins[0].FullName "ffprobe.exe"
$ffmpegSha = (Get-FileHash -LiteralPath $ffmpeg -Algorithm SHA256).Hash.ToLowerInvariant()
$ffprobeSha = (Get-FileHash -LiteralPath $ffprobe -Algorithm SHA256).Hash.ToLowerInvariant()
~~~

If multiple previously approved pin directories are installed,
choose the intended approved one explicitly rather than deleting
or changing any existing tool. Do not use PATH-discovered FFmpeg.

### 2. Privately select one current local revision

~~~powershell
$raw = & dotnet run --project src/PhotoIdentity.Cli --configuration Release -- video compatibility local-candidates --postgres-connection-env $envName --database $db --show-revision-ids
if ($LASTEXITCODE -ne 0) { throw "Private shortlist failed." }
$selection = ($raw -join "`n") | ConvertFrom-Json -ErrorAction Stop
if (-not $selection.readOnly -or
    $selection.sourceContentOpened -or
    $selection.oneDriveHydrationRequested -or
    $selection.locallyMatchedCount -ne @($selection.candidates).Count) {
    throw "Private candidate safety contract failed."
}
$selection | Select-Object candidateCount, locallyMatchedCount
$selection.candidates | Format-Table revisionId, sourceBytes, durationSeconds, codedOrientation, hasAudio -AutoSize

$revision = [Guid](Read-Host "Choose one local candidate UUID privately")
if (@($selection.candidates | Where-Object {
    [Guid]$_.revisionId -eq $revision
}).Count -ne 1) {
    throw "This revision is not in the current local shortlist."
}
~~~

Keep revision IDs and source names out of chat, PRs and public logs.

### 3. Create a dedicated private scratch workspace

~~~powershell
$scratch = Join-Path ([IO.Path]::GetTempPath()) (
    "wi0210-real-evaluation-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $scratch -ErrorAction Stop | Out-Null
Set-Content -LiteralPath (Join-Path $scratch ".photoidentity-real-video-evaluation") -NoNewline -Encoding utf8 -Value "wi0210-private-real-video-evaluation-v1"
~~~

The root must contain **only** that marker before the trial.
It must be on a local fixed-volume temp directory, not OneDrive
or the source archive. It is not cleared automatically on success,
because the private review proxy must be visually checked.

### 4. Validate dry-run (no original read)

~~~powershell
$argsToEvaluate = @(
    "run", "--project", "src/PhotoIdentity.Cli", "--configuration", "Release", "--",
    "video", "compatibility", "real-evaluate",
    "--postgres-connection-env", $envName, "--database", $db,
    "--revision-id", $revision.ToString(),
    "--scratch-root", $scratch,
    "--ffmpeg", $ffmpeg, "--ffmpeg-sha256", $ffmpegSha,
    "--ffprobe", $ffprobe, "--ffprobe-sha256", $ffprobeSha
)
$rawDry = & dotnet @argsToEvaluate
if ($LASTEXITCODE -ne 0) { throw "Evaluation dry-run failed." }
$dry = ($rawDry -join "`n") | ConvertFrom-Json -ErrorAction Stop
if ($dry.status -ne "not-run" -or -not $dry.dryRun -or
    $dry.originalBytesOpened -or $dry.reviewProxyRetained -or
    $dry.oneDriveHydrationRequested -or $dry.jobsEnqueued) {
    throw "Dry-run safety assertion failed."
}
$dry | Select-Object status, dryRun, originalBytesOpened, reviewProxyRetained
~~~

### 5. Explicitly authorize ONE original evaluation

**Only if you decide to approve reading this chosen real
already-local original and retaining one private review proxy**,
run the following. This is a genuine byte read and real encode,
not another metadata-only check.

~~~powershell
$trialOutput = & dotnet @argsToEvaluate --apply --read-local-bytes --retain-review-proxy
$trialExit = $LASTEXITCODE
$trial = ($trialOutput -join "`n") | ConvertFrom-Json -ErrorAction Stop

$trial | Select-Object status, inputFullRangeProven,
    outputLimitedRangeValidated, sourceIntegrityRechecked,
    reviewProxyRetained, originalBytesOpened, width, height,
    durationSeconds, hasAudio, oneDriveHydrationRequested,
    databaseWriteRequested, jobsEnqueued, derivativesPublished,
    productionProfileChanged, visualColourApproved

if ($trialExit -ne 0) {
    throw "Real source evaluation refused at $($trial.failure.stage): $($trial.failure.reason)"
}
if ($trial.status -ne "validated" -or
    -not $trial.inputFullRangeProven -or
    -not $trial.outputLimitedRangeValidated -or
    -not $trial.sourceIntegrityRechecked -or
    -not $trial.reviewProxyRetained -or
    -not $trial.originalBytesOpened -or
    $trial.oneDriveHydrationRequested -or $trial.databaseWriteRequested -or
    $trial.jobsEnqueued -or $trial.derivativesPublished -or
    $trial.productionProfileChanged -or $trial.visualColourApproved) {
    throw "Real evaluation did not meet its safety/format contract."
}
~~~

This is an **independent input-probe and output-probe** validation,
not yet a claim of acceptable *subjective* video appearance.
Fail-closed `trial-failed` at `input-probe` may mean the
original does not explicitly signal full range/Rec.709 or
rotation as expected; do not silently widen the filter. Share
only sanitized `stage` and `reason`, never source IDs,
paths or probe JSON.

### 6. Visually review the private proxy, then delete it

~~~powershell
$review = Join-Path $scratch "review-proxy.mp4"
if (-not (Test-Path -LiteralPath $review -PathType Leaf)) {
    throw "Review proxy is missing."
}

# Open the private file using the usual Windows-associated player.
Start-Process -FilePath $review
~~~

Inspect brightness/black levels, highlights/contrast, visible
colour shifts, aspect/orientation, audio (if applicable), and
smooth play/pause/seek. The app's production path is not yet
enabled for this new profile; browser publication/playback is
**a later product-integration acceptance**. Do not claim
it passed here.

After you close the local player, explicitly clean up
the dedicated evaluation directory. Only remove the exact
directory you just created for this review:

~~~powershell
if (-not $scratch.StartsWith(
    [IO.Path]::GetFullPath([IO.Path]::GetTempPath()),
    [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to remove scratch outside OS temp."
}
if ((Get-Content -LiteralPath (
    Join-Path $scratch ".photoidentity-real-video-evaluation") -Raw) -ne
    "wi0210-private-real-video-evaluation-v1") {
    throw "Unrecognized scratch marker."
}
Remove-Item -LiteralPath $scratch -Recurse -Force -ErrorAction Stop
~~~

Do not share the review proxy or its path. Report back only the
sanitized command fields and **your visual observations**:
pass/fail for brightness/contrast, colour, orientation, audio
and play/seek. If you did not approve `--apply`, the dry-run
alone is a safety preview, not an accepted real-media evaluation.

## Next work after accepted real-video evaluation

Only after independent probe validation and your subjective
visual review pass do we plan **one substantial product
integration PR**: register a versioned Rec.709 `yuvj420p`
playable-proxy profile and exact worker/gate, preserve all HDR,
poster, frame and other formats, prove end-to-end derived
playback and count actually unlocked videos.

If the chosen sample fails its range or image proof, stop
production admission for this subtype and investigate or
select another explicitly approved local sample. Do not
automatically process the other 11 candidates.
