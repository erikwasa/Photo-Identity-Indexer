<#
.SYNOPSIS
    Creates and verifies a PostgreSQL custom-format backup from Podman on Windows.
.DESCRIPTION
    Intended for Windows PowerShell 5.1 or PowerShell 7. Streams the binary pg_dump
    output directly to a temporary Windows file (no dump staged inside the Podman
    virtual disk). Verifies the entire archive with pg_restore, computes SHA-256,
    and optionally copies the finished files into OneDrive.

    This backs up ONE PostgreSQL database. PostgreSQL global roles, other databases,
    application files and source photos are not included. Restore into a separate
    environment before relying on it as your only recovery option.

.EXAMPLE
    .\backup-postgres-catalogue-improved.ps1 `
        -DatabaseName 'photoidentity_main' `
        -CopyToOneDrive

.EXAMPLE
    .\backup-postgres-catalogue-improved.ps1 -ConfigurationPath '.\PhotoIdentity.launcher.json'

.EXAMPLE
    .\backup-postgres-catalogue-improved.ps1 `
        -DatabaseName 'photoidentity_main' `
        -OutputPath 'D:\Backups\photoidentity.dump' -CopyToOneDrive
#>
[CmdletBinding()]
param(
    [string]$DatabaseName,
    [string]$OutputPath,
    [string]$ConfigurationPath,
    [string]$ContainerName = 'postgres-postgres-1',
    [switch]$CopyToOneDrive,
    [string]$OneDriveDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$podman = Get-Command podman -ErrorAction SilentlyContinue
if (-not $podman) { throw 'Podman was not found on PATH.' }
$podmanExe = $podman.Source

# PowerShell 5.1 has no ProcessStartInfo.ArgumentList. Quote Windows
# command-line arguments correctly for ProcessStartInfo.Arguments instead.
function Quote-WindowsArgument {
    param([AllowEmptyString()][string]$Value)
    if ($Value.Length -eq 0) { return '""' }
    if ($Value -notmatch '[\s"]') { return $Value }

    $result = '"'
    $slashes = 0
    foreach ($char in $Value.ToCharArray()) {
        if ($char -eq '\') {
            $slashes++
        }
        elseif ($char -eq '"') {
            $result += ('\' * (2 * $slashes + 1)) + '"'
            $slashes = 0
        }
        else {
            $result += ('\' * $slashes) + [string]$char
            $slashes = 0
        }
    }
    return $result + ('\' * (2 * $slashes)) + '"'
}

# Use .NET byte streams rather than PowerShell's text pipeline or '>' so the
# binary .dump cannot be re-encoded by Windows PowerShell 5.1.
function Invoke-PodmanBinary {
    param(
        [Parameter(Mandatory=$true)][string[]]$Arguments,
        [string]$StdoutPath,
        [string]$StdinPath
    )
    if ([bool]$StdoutPath -eq [bool]$StdinPath) {
        throw 'Specify exactly one of StdoutPath or StdinPath.'
    }
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $podmanExe
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $psi.RedirectStandardError = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardInput = [bool]$StdinPath
    $psi.Arguments = (($Arguments | ForEach-Object { Quote-WindowsArgument ([string]$_) }) -join ' ')

    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $psi
    $stream = $null
    $errorTask = $null
    $outputTask = $null
    try {
        if (-not $process.Start()) { throw 'Could not start Podman.' }
        $errorTask = $process.StandardError.ReadToEndAsync()

        if ($StdoutPath) {
            $stream = [System.IO.File]::Open($StdoutPath,
                [System.IO.FileMode]::CreateNew,
                [System.IO.FileAccess]::Write,
                [System.IO.FileShare]::None)
            $process.StandardOutput.BaseStream.CopyTo($stream)
        }
        else {
            # pg_restore --file=/dev/null should not emit stdout, but drain it
            # asynchronously anyway so a verbose process cannot deadlock.
            $outputTask = $process.StandardOutput.ReadToEndAsync()
            $stream = [System.IO.File]::OpenRead($StdinPath)
            $stream.CopyTo($process.StandardInput.BaseStream)
            $process.StandardInput.Close()
        }

        $stream.Close()
        $stream = $null
        $process.WaitForExit()
        $stderr = $errorTask.GetAwaiter().GetResult()
        if ($outputTask) { $null = $outputTask.GetAwaiter().GetResult() }
        if ($process.ExitCode -ne 0) {
            throw "Podman command failed (exit $($process.ExitCode)): $stderr"
        }
        if (-not [string]::IsNullOrWhiteSpace($stderr)) {
            Write-Warning "Podman reported: $stderr"
        }
    }
    finally {
        if ($stream) { $stream.Dispose() }
        if ($process) {
            if (-not $process.HasExited) {
                try { $process.Kill(); $process.WaitForExit() } catch { }
            }
            $process.Dispose()
        }
    }
}

function Get-LauncherEnvironmentValue {
    param([Parameter(Mandatory=$true)][string]$Name)
    foreach ($scope in @('Process','User','Machine')) {
        $value = [Environment]::GetEnvironmentVariable($Name, $scope)
        if (-not [string]::IsNullOrWhiteSpace($value)) { return $value }
    }
    return $null
}

function Resolve-LauncherConfigurationPath {
    if ($ConfigurationPath) {
        $candidate = [Environment]::ExpandEnvironmentVariables($ConfigurationPath)
        if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            throw "Launcher config not found: $candidate"
        }
        return (Resolve-Path -LiteralPath $candidate).Path
    }
    if ($env:PHOTOIDENTITY_LAUNCHER_CONFIG) {
        $candidate = [Environment]::ExpandEnvironmentVariables($env:PHOTOIDENTITY_LAUNCHER_CONFIG)
        if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            throw "Launcher config not found: $candidate"
        }
        return (Resolve-Path -LiteralPath $candidate).Path
    }
    $candidates = @(
        (Join-Path $PSScriptRoot 'PhotoIdentity.launcher.json'),
        (Join-Path $env:LOCALAPPDATA 'PhotoIdentity\launcher.json')
    )
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }
    return $null
}

function Get-LauncherSelectedDatabase {
    $launcherPath = Resolve-LauncherConfigurationPath
    if (-not $launcherPath) { return $null }
    try {
        $configuration = Get-Content -LiteralPath $launcherPath -Raw | ConvertFrom-Json
    }
    catch { throw "Invalid launcher JSON: $launcherPath" }
    $variableProperty = $configuration.PSObject.Properties['postgresConnectionEnvironmentVariable']
    if (-not $variableProperty -or -not $variableProperty.Value) {
        throw 'Launcher configuration has no postgresConnectionEnvironmentVariable.'
    }
    $variableName = ([string]$variableProperty.Value).Trim()
    $connection = Get-LauncherEnvironmentValue -Name $variableName
    if (-not $connection) { throw "Launcher connection variable '$variableName' is missing." }

    $builder = New-Object System.Data.Common.DbConnectionStringBuilder
    try { $builder.ConnectionString = $connection }
    catch { throw 'Could not parse launcher PostgreSQL connection string.' }
    foreach ($key in @('Database', 'Initial Catalog')) {
        if ($builder.ContainsKey($key) -and $builder[$key]) {
            return ([string]$builder[$key]).Trim()
        }
    }
    throw 'Launcher PostgreSQL connection string did not specify a database.'
}

# Resolve the database explicitly or from the same launcher convention as the
# original script. Do NOT scan/guess among the 1,274 test databases.
$selection = 'explicit'
if ([string]::IsNullOrWhiteSpace($DatabaseName)) {
    $DatabaseName = Get-LauncherSelectedDatabase
    if (-not $DatabaseName) {
        throw 'Database is ambiguous. Supply -DatabaseName or -ConfigurationPath; no backup was started.'
    }
    $selection = 'launcher'
}

# Ensure Podman can reach the target container and it has the catalogue markers.
# Database name is passed through an environment variable, not interpolated
# into SQL or shell commands.
$probe = & $podmanExe exec -e "TARGET_DATABASE=$DatabaseName" $ContainerName sh -lc `
    'psql -X -A -t -U "$POSTGRES_USER" -d "$TARGET_DATABASE" -v ON_ERROR_STOP=1 -c "SELECT CASE WHEN to_regclass(''public.photo_identity_schema_migrations'') IS NOT NULL AND to_regclass(''public.asset_revisions'') IS NOT NULL AND to_regclass(''public.review_actions'') IS NOT NULL THEN 1 ELSE 0 END"'
if ($LASTEXITCODE -ne 0 -or ($probe -join '').Trim() -ne '1') {
    throw "Database '$DatabaseName' is unavailable or missing required Photo Identity schema markers. No backup was started."
}

if (-not $OutputPath) {
    $backupDir = Join-Path $env:LOCALAPPDATA 'PhotoIdentity\backups\postgresql'
    $OutputPath = Join-Path $backupDir ('photoidentity-postgresql-{0}.dump' -f (Get-Date -Format 'yyyyMMdd-HHmmss'))
}
$OutputPath = [System.IO.Path]::GetFullPath($OutputPath)
$reportPath = "$OutputPath.json"
$directory = Split-Path -Parent $OutputPath
[System.IO.Directory]::CreateDirectory($directory) | Out-Null

if ((Test-Path -LiteralPath $OutputPath) -or (Test-Path -LiteralPath $reportPath)) {
    throw 'Backup or metadata file already exists; existing backups will not be overwritten.'
}
if ($CopyToOneDrive) {
    if (-not $OneDriveDirectory) {
        if (-not $env:OneDrive) { throw 'OneDrive folder not found. Supply -OneDriveDirectory.' }
        $OneDriveDirectory = Join-Path $env:OneDrive 'DatabaseBackups'
    }
    $OneDriveDirectory = [System.IO.Path]::GetFullPath($OneDriveDirectory)
    [System.IO.Directory]::CreateDirectory($OneDriveDirectory) | Out-Null
    $cloudDump = Join-Path $OneDriveDirectory (Split-Path -Leaf $OutputPath)
    $cloudReport = "$cloudDump.json"
    if ((Test-Path -LiteralPath $cloudDump) -or (Test-Path -LiteralPath $cloudReport)) {
        throw 'OneDrive destination already exists; refusing to overwrite.'
    }
}

$partPath = "$OutputPath.partial-$([Guid]::NewGuid().ToString('N'))"
$cloudPart = $null
$success = $false
try {
    Write-Host "Backing up: $DatabaseName ($selection) from $ContainerName"
    Write-Host 'Streaming PostgreSQL custom archive directly to Windows...'
    Invoke-PodmanBinary -Arguments @(
        'exec', '-e', "TARGET_DATABASE=$DatabaseName", $ContainerName,
        'sh', '-lc', 'pg_dump -U "$POSTGRES_USER" -d "$TARGET_DATABASE" --format=custom --compress=6'
    ) -StdoutPath $partPath

    $size = (Get-Item -LiteralPath $partPath).Length
    if ($size -lt 6) { throw 'Dump is empty or suspiciously short.' }
    $headerStream = [System.IO.File]::OpenRead($partPath)
    try {
        $header = New-Object byte[] 5
        $read = $headerStream.Read($header, 0, 5)
        if ($read -ne 5 -or [System.Text.Encoding]::ASCII.GetString($header) -ne 'PGDMP') {
            throw 'Dump does not have the expected PostgreSQL custom archive header.'
        }
    }
    finally { $headerStream.Dispose() }

    Write-Host 'Verifying full archive decode (this does not modify the database)...'
    Invoke-PodmanBinary -Arguments @(
        'exec', '-i', $ContainerName, 'pg_restore', '--file=/dev/null'
    ) -StdinPath $partPath

    $sha256 = (Get-FileHash -LiteralPath $partPath -Algorithm SHA256).Hash.ToLowerInvariant()
    # Publish the final filename only after verification succeeds.
    Move-Item -LiteralPath $partPath -Destination $OutputPath

    $report = [ordered]@{
        capturedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        sourceDatabase = $DatabaseName
        databaseSelectionSource = $selection
        container = $ContainerName
        backupPath = $OutputPath
        backupBytes = [long]$size
        backupSha256 = $sha256
        format = 'pg_dump-custom'
        verification = 'pg_restore-full-archive-decode-to-dev-null'
        verificationPassed = $true
        restoreTestPerformed = $false
        note = 'Single database only. No cluster roles, other databases, source photos, or app settings. Protect this unencrypted backup as sensitive data.'
    }
    $report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $reportPath -Encoding UTF8

    if ($CopyToOneDrive) {
        Write-Host 'Copying verified backup to OneDrive folder...'
        $cloudPart = "$cloudDump.uploading-$([Guid]::NewGuid().ToString('N'))"
        Copy-Item -LiteralPath $OutputPath -Destination $cloudPart -ErrorAction Stop
        $cloudSha256 = (Get-FileHash -LiteralPath $cloudPart -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($cloudSha256 -ne $sha256) { throw 'OneDrive copy checksum mismatch.' }
        Move-Item -LiteralPath $cloudPart -Destination $cloudDump
        $cloudPart = $null
        Copy-Item -LiteralPath $reportPath -Destination $cloudReport -ErrorAction Stop
        Write-Host "OneDrive folder copy: $cloudDump"
        Write-Warning 'OneDrive upload is not confirmed until sync completes and the file is visible on the OneDrive website.'
    }

    $success = $true
    Write-Host "Backup completed: $OutputPath"
    Write-Host "Size: $([math]::Round($size / 1GB, 2)) GiB; SHA-256: $sha256"
    Write-Host 'Note: full archive decode is NOT a real restore test.'
}
finally {
    # Never delete older backups. Remove only incomplete files created during
    # this specific invocation; keep fully verified local backups on cloud errors.
    if (Test-Path -LiteralPath $partPath) {
        Remove-Item -LiteralPath $partPath -Force -ErrorAction SilentlyContinue
    }
    if ($cloudPart -and (Test-Path -LiteralPath $cloudPart)) {
        Remove-Item -LiteralPath $cloudPart -Force -ErrorAction SilentlyContinue
    }
    if (-not $success) {
        Write-Warning 'Backup job did not complete all requested steps. Check the error above. Existing backup files were not deleted.'
    }
}
