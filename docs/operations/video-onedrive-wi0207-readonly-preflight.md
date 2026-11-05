# WI-0207 — read-only OneDrive video hydration preflight (checkpoint 1)

**Scope: metadata and Windows Files On-Demand attributes only. No original-media permission.**
This bounded report inventories currently eligible, nonexcluded video
revisions lacking published proxies and compares live local/online-only/
pinned states with queued work and existing managed hydration evidence.

**Do not enable Include online-only videos yet.** The existing video
worker can issue a release immediately after its one video attempt,
without checking whether a later dependent video job still needs
the original or the user has pinned it. WI-0207 must independently
verify and correct that behavior before a real online-only transfer.
This audit neither causes nor approves a transfer.

## 1. Update the maintained Windows source and test

Run all commands in PowerShell 7.6:

~~~powershell
cd "C:\Kod\codex\Photo Identity Indexer"
git switch main
git pull --ff-only origin main

dotnet test tests/PhotoIdentity.Integration.Tests/PhotoIdentity.Integration.Tests.csproj -c Release --filter "FullyQualifiedName~VideoOnlineOnlyPreflightTests"
if ($LASTEXITCODE -ne 0) { throw "WI-0207 preflight tests failed." }
~~~

All selected tests use synthetic status snapshots and no original media.

## 2. Read the already approved launcher and local database name

Never print the PostgreSQL connection string because it contains a
password. The command only needs the environment-variable name and
database name, which the existing launcher already provides.

~~~powershell
$ErrorActionPreference = "Stop"
$choices = @(
    $env:PHOTOIDENTITY_LAUNCHER_CONFIG
    (Join-Path (Get-Location).Path "PhotoIdentity.launcher.json")
    (Join-Path (Get-Location).Path "launch.json")
    (Join-Path $env:LOCALAPPDATA "PhotoIdentity\launcher.json")
    (Join-Path $env:LOCALAPPDATA "PhotoIdentity\launch.json")
)
$launcherPath = $choices | Where-Object {
    $_ -and (Test-Path -LiteralPath (
        [Environment]::ExpandEnvironmentVariables($_)
    ) -PathType Leaf)
} | Select-Object -First 1
if (-not $launcherPath) {
    throw "Existing launcher configuration not found."
}
$launcherPath = (Resolve-Path -LiteralPath (
    [Environment]::ExpandEnvironmentVariables($launcherPath)
)).Path
$launcher = Get-Content -LiteralPath $launcherPath -Raw | ConvertFrom-Json
$connectionVariable = ([string]$launcher.postgresConnectionEnvironmentVariable).Trim()
if ($connectionVariable -cnotmatch '^[A-Za-z_][A-Za-z0-9_]*$') {
    throw "Invalid PostgreSQL environment-variable name."
}
$connectionString = $null
foreach ($scope in @("Process", "User", "Machine")) {
    $value = [Environment]::GetEnvironmentVariable($connectionVariable, $scope)
    if (-not [string]::IsNullOrWhiteSpace($value)) {
        $connectionString = $value
        break
    }
}
if (-not $connectionString) { throw "Existing PostgreSQL credentials missing." }
[Environment]::SetEnvironmentVariable(
    $connectionVariable, $connectionString, "Process"
)
$matched = [regex]::Match(
    $connectionString, '(?i)(?:^|;)\s*Database\s*=\s*([^;]+)'
)
if (-not $matched.Success) { throw "No explicit database configured." }
$databaseName = $matched.Groups[1].Value.Trim()
if ($databaseName -cnotmatch '^[a-z][a-z0-9_]{0,62}$') {
    throw "Invalid database name."
}
Remove-Variable connectionString, value, matched -ErrorAction SilentlyContinue
Write-Host "Approved launcher and database name: PASS"
~~~

The preflight independently requires loopback PostgreSQL and the
existing current schema, without any database initialization/migration.

## 3. Run the read-only OneDrive video preflight

~~~powershell
$preflightArgs = @(
    "run", "--project", "src/PhotoIdentity.Cli", "-c", "Release", "--",
    "video", "onedrive", "preflight",
    "--postgres-connection-env", $connectionVariable,
    "--database", $databaseName
)
$preflightJson = dotnet @preflightArgs
$preflightExit = $LASTEXITCODE
if ($preflightExit -ne 0) { throw "Read-only WI-0207 preflight failed." }
$preflight = ($preflightJson | Out-String) | ConvertFrom-Json
[pscustomobject]@{
    Protocol = $preflight.protocol
    ReadOnly = $preflight.readOnly
    OriginalBytesOpened = $preflight.originalContentOpened
    HydrationRequested = $preflight.oneDriveHydrationRequested
    ReleaseRequested = $preflight.oneDriveReleaseRequested
    ProxyMissingCandidates = $preflight.catalogueCurrentSupportedWithoutPublishedProxy
    StandardOrCompactCandidates = $preflight.standardOrCompactProfileCandidates
    FullRangeCandidates = $preflight.fullRangeProfileCandidates
    QueuedOrRunningDependents = $preflight.queuedOrRunningDependents
    ActiveManagedRecords = $preflight.activePhotoManagedHydrationRecords
}
$preflight.availability | Format-Table -AutoSize
~~~

Report only the aggregate counts and PASS/FAIL flags. Never paste
private paths, names, revision IDs, media frames, hashes or credentials.

Results are limited to 64 nonexcluded current metadata-supported
videos without already published playable proxies. More than 64
candidate revisions is an explicit failure instead of a partial
or archive-wide scan. Profile groups are metadata candidates only,
not guaranteed playable output. A Files On-Demand attribute is not
SHA/integrity proof. Active managed records count existing photo
hydration receipts; this does not prove video hydration has been
integrated with that ownership service.

## 4. Remaining WI-0207 gates

This report is the first read-only checkpoint, **not work-item
completion**. Next implementation must establish durable, shared
managed-hydration ownership for videos, safe storage admission,
original pinning preservation, current revision/exclusion fences,
active/pending dependent-job retention and safe bounded release
after work finishes. One explicitly approved real online-only
video hydration, processing/reuse and eligible release check is
mandatory; no disruptive OneDrive-outage campaign is required.

Until then leave Include online-only videos OFF. Do not run a
separate Archive advance, request a download, pin/unpin any file,
or start a video processing job for this first checkpoint.
