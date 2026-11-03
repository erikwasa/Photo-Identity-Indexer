# WI-0210 — guarded full-range Rec.709 synthetic roundtrip

**Checkpoint 4, proposed.** This is an explicitly selected, synthetic-only
evaluation, not production video processing and not proof of real archive
colour quality. The first archive-driven candidate class is 12 Rec.709
`yuvj420p` metadata-only revisions out of 58 current registered videos.
All 58 catalogue availability observations are unknown; do not interpret
the candidate count as a count of locally playable source files.

## Scope and expected evidence

`video synthetic-proxy-pilot --full-range-evaluation` is **dry-run by
default**. With `--apply`, it reuses the existing marked,
otherwise-empty OS temp child, approved hash-pinned FFmpeg/FFprobe and
Windows Job Object. It generates only two fixed 3-second test sources:
1280×720 with audio and 720×1280 without audio. The synthetic H.264
encoder explicitly sets Rec.709 primaries, transfer, matrix and
`color_range=pc`. A separate bounded, pinned FFprobe invocation **first
proves** that the actual generated stream is `yuvj420p`, `pc`,
Rec.709 and of the expected size and orientation.

Only then does the unregistered
`wi0210-rec709-fullrange-720p-evaluation-v1` recipe run. It derives
the existing pinned 720p H.264/AAC argument array, changing just the
fixed BT.709 full-to-limited scale conversion and output
`-color_range tv` tag. The usual independent MP4 faststart/stream/
bounded colour-and-orientation validator runs again and additionally
requires explicit output `color_range=tv`. Both generated source
and output are deleted when the scoped supervisor workspaces close.

This **does not** open OneDrive placeholders or originals, read
PostgreSQL, alter any assets, enqueue processing, publish a proxy,
serve a browser response or enable the full-range profile in app
settings/worker SQL. User-supplied `--source`, filenames, freeform
filters, and `--profile` together with
`--full-range-evaluation` are rejected.

## Maintained Windows verification — start AFTER PR merge

Use your existing approved, digest-pinned FFmpeg installation.
The script below only accepts a **single** installed pair in the
previously approved `%LOCALAPPDATA%\PhotoIdentity\Tools\FFmpeg\<digest>`
layout, where the directory name equals the actual FFmpeg SHA-256.
This is a drift check, **not independent publisher authentication**;
do not use a different binary merely to make the command pass.

~~~powershell
cd "C:\Kod\codex\Photo Identity Indexer"
git switch main
git pull --ff-only origin main

$toolRoot = Join-Path $env:LOCALAPPDATA "PhotoIdentity\Tools\FFmpeg"
if (-not (Test-Path -LiteralPath $toolRoot -PathType Container)) {
    throw "Previously approved pinned FFmpeg directory not found."
}
$approved = @(
    foreach ($dir in Get-ChildItem -LiteralPath $toolRoot -Directory) {
        $exe = Join-Path $dir.FullName "ffmpeg.exe"
        $probe = Join-Path $dir.FullName "ffprobe.exe"
        if (-not (Test-Path -LiteralPath $exe -PathType Leaf) -or
            -not (Test-Path -LiteralPath $probe -PathType Leaf)) { continue }
        $hash = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($dir.Name.ToLowerInvariant() -eq $hash) {
            [pscustomobject]@{ Ffmpeg = $exe; Ffprobe = $probe; FfmpegSha = $hash }
        }
    }
)
if ($approved.Count -ne 1) {
    throw "Expected exactly one previously approved digest-pinned FFmpeg pair. Select the approved pair explicitly instead."
}
$ffmpeg = $approved[0].Ffmpeg
$ffprobe = $approved[0].Ffprobe
$ffmpegSha = $approved[0].FfmpegSha
$ffprobeSha = (Get-FileHash -LiteralPath $ffprobe -Algorithm SHA256).Hash.ToLowerInvariant()

$root = Join-Path ([IO.Path]::GetTempPath()) ("photoidentity-wi0210-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $root -ErrorAction Stop | Out-Null
$marker = Join-Path $root ".photoidentity-synthetic-video-lab"
[IO.File]::WriteAllText($marker, "wi0188-synthetic-video-lab-v1")

$cli = @(
    "run", "--project", "src/PhotoIdentity.Cli", "-c", "Release", "--",
    "video", "synthetic-proxy-pilot",
    "--scratch-root", $root,
    "--ffmpeg", $ffmpeg,
    "--ffmpeg-sha256", $ffmpegSha,
    "--ffprobe", $ffprobe,
    "--ffprobe-sha256", $ffprobeSha,
    "--full-range-evaluation"
)

# 1: Explicit dry-run, no media generated.
$dry = & dotnet @cli
if ($LASTEXITCODE -ne 0) { throw "WI-0210 dry-run failed." }
$dryReport = ($dry -join "`n") | ConvertFrom-Json -ErrorAction Stop
if ($dryReport.status -ne "not-run" -or -not $dryReport.dryRun) {
    throw "Unexpected dry-run report."
}

# 2: Explicit generation and evaluation, no archive access.
$executed = & dotnet @cli --apply
if ($LASTEXITCODE -ne 0) { throw "WI-0210 synthetic evaluation failed." }
$report = ($executed -join "`n") | ConvertFrom-Json -ErrorAction Stop
if ($report.status -ne "validated" -or $report.dryRun -or
    @($report.results).Count -ne 2 -or
    $report.originalsOpened -or $report.schemaUsed -or
    $report.videoWorkersActivated -or $report.derivativesRetained) {
    throw "Synthetic output did not satisfy the scoped acceptance."
}
$report | Select-Object status, profile, dryRun, originalsOpened,
    schemaUsed, videoWorkersActivated, derivativesRetained
$report.results | Format-Table orientation, width, height, hasAudio,
    durationSeconds, encodedBytes, encodeMilliseconds

# 3: No generated clips remain. Only the exact ownership marker is left.
$entries = @(Get-ChildItem -LiteralPath $root -Force)
if ($entries.Count -ne 1 -or $entries[0].FullName -ne $marker) {
    throw "Unanticipated synthetic scratch residue; inspect without automatic deletion."
}
Remove-Item -LiteralPath $marker
Remove-Item -LiteralPath $root
~~~

A successful `--apply` reports `status=validated`, two results
(`landscape` with audio and `portrait` without audio), positive
encoded sizes and elapsed times, and four explicit
`false` flags: `originalsOpened`, `schemaUsed`,
`videoWorkersActivated`, `derivativesRetained`.
The source and output colour-range validation happens inside the tool
and is mandatory to return `validated`; raw FFprobe/FFmpeg metadata
is not printed. The root is a private temporary test fixture, not a
general-purpose cache or archive folder.

## Regression/remaining acceptance

CI owns documentation regeneration and validation; **no separate local
docs commands are required**. An optional focused test after merge is:

~~~powershell
dotnet test tests/PhotoIdentity.Integration.Tests -c Release --filter "FullyQualifiedName~VideoRec709FullRangeEvaluationProfileTests|FullyQualifiedName~VideoSyntheticProxyPilotTests|FullyQualifiedName~VideoProxyCandidateProfileTests|FullyQualifiedName~VideoProxyEncodingProfilesTests"
~~~

This full-range synthetic acceptance establishes an FFmpeg/FFprobe
*format and safety* smoke test for the maintained tools, not the
visual colour/contrast of a real camera video. **Next** identify one
already-local source among the 12 Rec.709 `yuvj420p` candidates,
without automatic OneDrive hydration, and independently verify
`color_range=pc`, Rec.709 tags, rotation, unchanged source hash and
real output colour quality. Only then consider a new versioned
production profile and explicit operator opt-in; HLG, unknown transfer
and unknown rotation remain deferred.
