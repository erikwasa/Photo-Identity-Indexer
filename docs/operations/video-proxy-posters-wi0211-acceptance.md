# WI-0211 — proxy-derived poster backfill (Windows)

**Do not perform real-media backfill before this PR merges, CI passes, and
the operator expressly approves a bounded local run.** The safe preview below
is database-only and never touches original or proxy media bytes. The apply
path reads **only already retained, SHA-validated MP4 proxies**, not originals,
and produces a new versioned JPEG under the approved derivative root.
The browser is passive: opening Videos or refreshing status never invokes it.

## Preconditions

- Run from merged main; use the established PostgreSQL connection and approved
  launcher. Avoid running archive advances, proxy cleanup, and source exclusion
  operations during the acceptance session.
- Identify the actual approved video proxy cache root configured by
  PhotoIdentity__VideoProcessing__CacheRoot (or its app-local default).
- Identify the same video poster root configured by PhotoIdentity__VideoPosterRoot
  (or its app-local default). Both directories must already exist, be disjoint,
  and must not overlap original source roots.
- Use the approved digest-pinned FFmpeg and FFprobe binaries. Compare their
  hashes with the *previously approved* values; calculating a new SHA does not
  authorize an unapproved executable.
- Use the existing PostgreSQL connection environment variable and catalogue
  database. Do not print the connection string.

## 1. Update code and prepare the parameters

~~~powershell
cd "C:\Kod\codex\Photo Identity Indexer"
git switch main
git pull --ff-only origin main

# Replace these only if your launcher has different approved roots.
$cacheRoot = Join-Path $env:LOCALAPPDATA "PhotoIdentity\video-worker-cache"
$posterRoot = Join-Path $env:LOCALAPPDATA "PhotoIdentity\video-derivatives"

$ffmpeg = (Read-Host "Approved absolute ffmpeg.exe path").Trim('"')
$ffprobe = (Read-Host "Approved absolute ffprobe.exe path").Trim('"')
$approvedFfmpegSha = (Read-Host "Previously approved FFmpeg SHA-256").Trim().ToLowerInvariant()
$approvedFfprobeSha = (Read-Host "Previously approved FFprobe SHA-256").Trim().ToLowerInvariant()

if (-not (Test-Path -LiteralPath $cacheRoot -PathType Container) -or
    -not (Test-Path -LiteralPath $posterRoot -PathType Container)) {
    throw "Use the existing configured cache and poster roots."
}
if (-not (Test-Path -LiteralPath $ffmpeg -PathType Leaf) -or
    -not (Test-Path -LiteralPath $ffprobe -PathType Leaf)) {
    throw "Use already approved FFmpeg/FFprobe executables."
}
if ((Get-FileHash -LiteralPath $ffmpeg -Algorithm SHA256).Hash.ToLowerInvariant() -ne $approvedFfmpegSha -or
    (Get-FileHash -LiteralPath $ffprobe -Algorithm SHA256).Hash.ToLowerInvariant() -ne $approvedFfprobeSha) {
    throw "Tool digest mismatch. Do not continue."
}
$connectionVariable = "PHOTOIDENTITY_POSTGRES_CONNECTION_STRING"
if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($connectionVariable))) {
    throw "Use the established PostgreSQL connection environment."
}
$database = (Read-Host "Existing PostgreSQL catalogue database name").Trim()
if ($database -notmatch '^[a-z][a-z0-9_]{0,62}$') { throw "Invalid catalogue database name." }

$posterArgs = @(
    "run", "--project", "src/PhotoIdentity.Cli", "-c", "Release", "--",
    "video", "posters-from-proxies", "--enable-video",
    "--cache-root", $cacheRoot,
    "--derivatives", $posterRoot,
    "--ffmpeg", $ffmpeg, "--ffmpeg-sha256", $approvedFfmpegSha,
    "--ffprobe", $ffprobe, "--ffprobe-sha256", $approvedFfprobeSha,
    "--postgres-connection-env", $connectionVariable,
    "--database", $database,
    "--max-videos", "1",
    "--timeout-seconds", "60"
)
~~~

If a launcher override is active, replace the two root variables with its
*actual* configured paths before running. Do not use --apply to discover
candidate counts.

## 2. Safe metadata-only preview

~~~powershell
dotnet @posterArgs
if ($LASTEXITCODE -ne 0) { throw "Proxy poster preview failed." }
~~~

Expected sanitized output resembles:
WI-0211 dry run: candidates=N; applied=0; originalsRead=0; ffmpegRuns=0
N is 0 or 1 in this one-video invocation. No media bytes or private filenames
are read, and no posters or jobs are created.

## 3. Explicit one-proxy local acceptance

**Only with separate operator approval.** Pause the Archive first. The next
command intentionally reads the previously published proxy, stages a verified
temporary private copy (up to 256 MiB) and publishes one versioned JPEG.

~~~powershell
# Only after you deliberately authorize one local proxy-poster operation:
dotnet @posterArgs --apply
if ($LASTEXITCODE -ne 0) { throw "Proxy poster backfill failed." }
~~~

Successful aggregate output resembles:
WI-0211 proxy poster backfill: created=1; skipped=0; failed=0; originalsRead=0; hydration=0

## 4. Visual acceptance

1. Refresh /videos in the Windows browser. The registered poster count
   should increase by one if a previously missing still was created.
2. Inspect recognizable subject, portrait orientation, undistorted aspect ratio,
   SDR colors, and whether the selected frame is blank/dark.
3. Open the expected clip. Verify play, pause, seek, and native controls.
4. Repeat the safe preview; it must not select the same published poster again.
   Another missing candidate may still be offered.
5. Confirm no OneDrive hydration, original-video reads, proxy replacement,
   unexpected photo work, or background processing.

Report only aggregate counts and PASS/FAIL; do not paste private media,
source paths, revision UUIDs, personal metadata, screenshots or connection
credentials into CI/PRs/chat. This validates one bounded representative,
not an all-archive migration. An unsuccessful JPEG does not revoke the
existing playable proxy.
