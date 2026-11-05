# WI-0211 — operator-controlled proxy-derived video posters (Windows)

**Maintainer-accepted on 2026-11-05 for the bounded one-proxy vertical
slice and full-gallery layout correction.** Five focused local tests
passed, one previously published proxy generated one correct poster,
native playback and seek worked, and the maintainer confirmed that
the gallery now displays complete posters following PR #742.

Registered poster count increased **1 → 2**; playable without stills
decreased **15 → 14**; playable video count stayed **16**. No unexpected
OneDrive hydration or processing was observed. The 14 remaining
missing posters require separate opt-in future backfill; no automatic
batch was authorized. **Do not repeat --apply solely for this work
item's documentation closeout.**

The CLI works from previously published, hash-verified local MP4
proxies, not original video bytes. The browser never invokes the CLI.

## 1. Read the existing launcher and approved tools

Run from the repository root in PowerShell 7. The launcher uses a
settings object containing four approved executable settings, the proxy
cache root and the existing poster derivative root. Never supply ad hoc
tool hashes or create replacement roots to bypass validation.

~~~powershell
cd "C:\Kod\codex\Photo Identity Indexer"
git switch main
git pull --ff-only origin main
$ErrorActionPreference = "Stop"

$candidates = @(
    $env:PHOTOIDENTITY_LAUNCHER_CONFIG
    (Join-Path (Get-Location).Path "PhotoIdentity.launcher.json")
    (Join-Path (Get-Location).Path "launch.json")
    (Join-Path $env:LOCALAPPDATA "PhotoIdentity\launcher.json")
    (Join-Path $env:LOCALAPPDATA "PhotoIdentity\launch.json")
)
$launcherPath = $candidates | Where-Object {
    $_ -and (Test-Path -LiteralPath (
        [Environment]::ExpandEnvironmentVariables($_)
    ) -PathType Leaf)
} | Select-Object -First 1
if (-not $launcherPath) { throw "Approved launcher config missing." }
$launcherPath = (Resolve-Path -LiteralPath (
    [Environment]::ExpandEnvironmentVariables($launcherPath)
)).Path
$launcher = Get-Content -LiteralPath $launcherPath -Raw | ConvertFrom-Json
if (-not $launcher.settings) { throw "Launcher settings missing." }

.\Start-PhotoIdentity.ps1 -ConfigurationPath $launcherPath -ValidateConfigurationOnly
$approved = .\Get-PhotoIdentityApprovedVideoTools.ps1 -ConfigurationPath $launcherPath
$ffmpeg = $approved.FfmpegPath
$ffmpegSha = $approved.FfmpegSha256
$ffprobe = $approved.FfprobePath
$ffprobeSha = $approved.FfprobeSha256

$settings = $launcher.settings
function Get-ConfiguredRoot {
    param([string]$Name, [string]$Default)
    $property = $settings.PSObject.Properties[$Name]
    $value = if ($property -and
        -not [string]::IsNullOrWhiteSpace([string]$property.Value)) {
        [string]$property.Value
    } else { $Default }
    return [IO.Path]::GetFullPath(
        [Environment]::ExpandEnvironmentVariables($value)
    )
}
$cacheRoot = Get-ConfiguredRoot "PhotoIdentity__VideoProcessing__CacheRoot" (Join-Path $env:LOCALAPPDATA "PhotoIdentity\video-worker-cache")
$posterRoot = Get-ConfiguredRoot "PhotoIdentity__VideoPosterRoot" (Join-Path $env:LOCALAPPDATA "PhotoIdentity\video-derivatives")
if (-not (Test-Path -LiteralPath $cacheRoot -PathType Container) -or
    -not (Test-Path -LiteralPath $posterRoot -PathType Container)) {
    throw "Use existing configured cache and derivative roots."
}
Write-Host "Launcher, approved FFmpeg/FFprobe hashes and roots: PASS"
~~~

The tool helper verifies both files, recorded SHA-256 hashes and
absence of linked paths. The poster CLI additionally verifies cache
ownership, digest, revision/exclusion, and separation from originals.

## 2. Resolve PostgreSQL settings without printing credentials

The launcher stores an environment-variable **name**. This script
reads its existing value, verifies an explicit database name and
puts the credential into the current PowerShell process environment.

~~~powershell
$connectionVariable = ([string]$launcher.postgresConnectionEnvironmentVariable).Trim()
if ($connectionVariable -cnotmatch '^[A-Za-z_][A-Za-z0-9_]*$') {
    throw "Invalid PostgreSQL environment variable name."
}
$connectionString = $null
foreach ($scope in @("Process", "User", "Machine")) {
    $value = [Environment]::GetEnvironmentVariable($connectionVariable, $scope)
    if (-not [string]::IsNullOrWhiteSpace($value)) {
        $connectionString = $value
        break
    }
}
if ([string]::IsNullOrWhiteSpace($connectionString)) {
    throw "Existing PostgreSQL credentials not found."
}
[Environment]::SetEnvironmentVariable($connectionVariable, $connectionString, "Process")
$dbMatch = [regex]::Match(
    $connectionString, '(?i)(?:^|;)\s*Database\s*=\s*([^;]+)'
)
if (-not $dbMatch.Success) { throw "No explicit Database= value." }
$databaseName = $dbMatch.Groups[1].Value.Trim()
if ($databaseName -cnotmatch '^[a-z][a-z0-9_]{0,62}$') {
    throw "Database name fails bounded CLI validation."
}
Remove-Variable connectionString, dbMatch, value -ErrorAction SilentlyContinue
Write-Host "PostgreSQL settings: PASS"
~~~

Never print the connection string (it includes the password).
PowerShell elseif/else clauses must remain in the same statement;
this version deliberately uses one regex lookup instead of generic
provider-independent dictionary parsing.

## 3. Ensure the video lane is paused and idle

~~~powershell
$statusArgs = @(
    "run", "--project", "src/PhotoIdentity.Cli", "-c", "Release", "--",
    "video", "jobs", "status",
    "--postgres-connection-env", $connectionVariable,
    "--database", $databaseName
)
$statusOutput = dotnet @statusArgs
if ($LASTEXITCODE -ne 0) { throw "Video status failed." }
$status = ($statusOutput | Out-String) | ConvertFrom-Json
if ($status.overview.running -ne 0) {
    throw "Running video work must stop before poster extraction."
}
if ($status.control.enabled -and -not $status.control.paused) {
    throw "Video lane must be paused before poster extraction."
}
Write-Host "Video lane paused/disabled and idle: PASS"
~~~

If necessary, use the existing explicit video jobs pause operator
action, then recheck. Do not cancel queued work solely for this test.

## 4. Metadata-only preview

~~~powershell
$posterArgs = @(
    "run", "--project", "src/PhotoIdentity.Cli", "-c", "Release", "--",
    "video", "posters-from-proxies", "--enable-video",
    "--cache-root", $cacheRoot, "--derivatives", $posterRoot,
    "--ffmpeg", $ffmpeg, "--ffmpeg-sha256", $ffmpegSha,
    "--ffprobe", $ffprobe, "--ffprobe-sha256", $ffprobeSha,
    "--postgres-connection-env", $connectionVariable,
    "--database", $databaseName, "--max-videos", "1",
    "--timeout-seconds", "60"
)
$preview = dotnet @posterArgs
$exitCode = $LASTEXITCODE
$preview
if ($exitCode -ne 0) { throw "Poster preview failed." }
~~~

Expected: WI-0211 dry run: candidates=N; applied=0;
originalsRead=0; ffmpegRuns=0. N is 0 or 1. No poster, proxy
or original media bytes are opened by the preview.

## 5. Optional future one-poster generation — requires separate approval

**Already completed for WI-0211 acceptance. Do not run this again
without a new, deliberate operator decision.** After a successful
preview with one candidate, enough private local disk space for a
temporary verified proxy copy (at least 512 MiB free), approved
tool hashes, and an idle video lane, the authorized operator may run:

~~~powershell
# Only with separate operator authorization for one local proxy poster.
$applyOutput = dotnet @posterArgs --apply
$applyCode = $LASTEXITCODE
$applyOutput
if ($applyCode -ne 0) {
    throw "Poster generation failed: stop and investigate, do not retry blindly."
}
~~~

Successful output resembles: WI-0211 proxy poster backfill:
created=1; skipped=0; failed=0; originalsRead=0; hydration=0.

## 6. Visual check

With the local app running, inspect the read-only
/api/videos/coverage before and after a new authorized run.
The new poster should increase registered poster coverage by one
without changing playable-proxy coverage. Refresh /videos, confirm
the entire recognizable still fits within the card, and verify the
related video's native play/pause/seek controls. Repeat the
preview only to check that the already registered still is not
selected again; no second apply is part of this acceptance.

**Recorded successful acceptance (2026-11-05):** five local tests
passed; created=1, exit code 0, no unexpected OneDrive activity.
Registered posters 1 → 2, playable missing posters 15 → 14,
playable count unchanged at 16. The maintainer verified the
correct orientation, recognizable content, play/pause/seek, and,
after PR #742 merged, that full gallery posters look good.
Do not share filenames, private poster images, source paths,
revision IDs, GPS or PostgreSQL credentials in CI/PRs/chat.
