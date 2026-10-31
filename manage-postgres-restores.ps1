[CmdletBinding()]
param(
    [string]$RemoveDatabase,
    [string]$ExpectedMarker,
    [string]$ProductionDatabaseName,
    [switch]$ConfirmRemoval
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'postgres-restore-lifecycle.ps1')

if ($ConfirmRemoval -and [string]::IsNullOrWhiteSpace($RemoveDatabase)) {
    throw '-ConfirmRemoval requires -RemoveDatabase. Inventory is always read-only by default.'
}
if (-not [string]::IsNullOrWhiteSpace($RemoveDatabase)) {
    if (-not $ConfirmRemoval -or -not (Test-GeneratedRestoreName $RemoveDatabase) -or
        -not (Test-RestoreOwnershipMarker $ExpectedMarker) -or
        [string]::IsNullOrWhiteSpace($ProductionDatabaseName) -or
        $RemoveDatabase -ceq $ProductionDatabaseName) {
        throw 'Removal requires an exact generated name, matching ownership marker, explicit production database exclusion and -ConfirmRemoval.'
    }
}

$script:podman = Get-Command podman -ErrorAction SilentlyContinue
if ($null -eq $script:podman) { throw 'Podman is required.' }
$script:containerId = $null
Push-Location (Join-Path $PSScriptRoot 'deploy\postgres')
try {
    $script:containerId = (& $script:podman.Source compose ps -q postgres).Trim()
    if ([string]::IsNullOrWhiteSpace($script:containerId)) {
        throw 'The repository PostgreSQL service is not running.'
    }

    if (-not [string]::IsNullOrWhiteSpace($RemoveDatabase)) {
        # Protection is inside Remove-OwnedRestoreDatabase, including exact
        # marker, no active sessions, and non-production source name.
        Remove-OwnedRestoreDatabase -Name $RemoveDatabase -ExpectedMarker $ExpectedMarker -SourceDatabase $ProductionDatabaseName
        Write-Host "Removed only verified, owned restored database '$RemoveDatabase'."
        return
    }

    # Read-only discovery; an old unmarked verification database is NOT
    # automatically eligible for removal, even if its name looks correct.
    $sql = "SELECT datname || '|' || COALESCE(shobj_description(oid, 'pg_database'), '') || '|' || pg_database_size(oid)::text || '|' || (SELECT COUNT(*) FROM pg_stat_activity WHERE datname = pg_database.datname)::text FROM pg_database WHERE datname ~ '^photoidentity_restore_[0-9]{14}_[a-f0-9]{6}$' ORDER BY datname;"
    $rows = @(Invoke-RestoreAdminSql -Sql $sql)
    foreach ($line in $rows) {
        $parts = @(([string]$line).Split('|'))
        if ($parts.Count -ne 4 -or -not (Test-GeneratedRestoreName $parts[0])) {
            throw 'Unexpected inventory result; no cleanup was performed.'
        }
        $bytes = [long]0
        $sessions = [int]0
        if (-not [long]::TryParse($parts[2], [ref]$bytes) -or
            -not [int]::TryParse($parts[3], [ref]$sessions)) {
            throw 'Invalid inventory size/session data.'
        }
        [pscustomobject]@{
            Name = $parts[0]
            SizeGiB = [math]::Round($bytes / 1GB, 2)
            Sessions = $sessions
            Ownership = $(if (Test-RestoreOwnershipMarker $parts[1]) { 'owned' } else { 'unmarked-legacy-do-not-auto-remove' })
            Marker = $parts[1]
        }
    }
}
finally {
    Pop-Location
}
