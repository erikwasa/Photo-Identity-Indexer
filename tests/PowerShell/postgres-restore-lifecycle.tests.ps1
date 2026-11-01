# Source-free, Podman-free destructive-path guard checks for WI-0208.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'postgres-restore-lifecycle.ps1')

$script:validName = 'photoidentity_restore_20261031235500_abcdef'
$script:validMarker = 'photoidentity-restore:v1:' + ('a' * 32) + ':' + ('b' * 64)
$script:dropCalls = 0
$script:throwDrop = $false
$script:record = $null
$script:postDropMissing = $true
$script:checked = 0
$script:detailChecks = 0
$script:transientBusyChecks = 0
$script:markerFlipAfterChecks = 0

function Assert-Condition {
    param([bool]$Condition, [string]$Message)
    $script:checked++
    if (-not $Condition) { throw "ASSERTION FAILED: $Message" }
}

function Assert-Throws {
    param([scriptblock]$Action, [string]$Message)
    $script:checked++
    try { & $Action } catch { return }
    throw "ASSERTION FAILED (expected failure): $Message"
}

# Override only the Podman/SQL boundary, not the ownership gate under test.
function Get-RestoreDatabaseDetails {
    param([string]$Name)
    $script:detailChecks++
    if ($script:dropCalls -gt 0 -and $script:postDropMissing) { return $null }
    if ($null -eq $script:record) { return $null }
    if ($script:markerFlipAfterChecks -gt 0 -and
        $script:detailChecks -gt $script:markerFlipAfterChecks) {
        $script:record.Marker = 'photoidentity-restore:v1:' + ('c' * 32) + ':' + ('b' * 64)
    }
    if ($script:transientBusyChecks -gt 0) {
        $script:transientBusyChecks--
        return [pscustomobject]@{
            Name = $script:record.Name
            Marker = $script:record.Marker
            Owned = $script:record.Owned
            Bytes = $script:record.Bytes
            Sessions = 1
        }
    }
    return $script:record
}
function Invoke-RestoreDrop {
    param([string]$Name)
    $script:dropCalls++
    if ($script:throwDrop) { throw 'simulated drop failure' }
}
function Reset-Record {
    $script:dropCalls = 0
    $script:detailChecks = 0
    $script:transientBusyChecks = 0
    $script:markerFlipAfterChecks = 0
    $script:throwDrop = $false
    $script:postDropMissing = $true
    $script:record = [pscustomobject]@{
        Name = $script:validName
        Marker = $script:validMarker
        Owned = $true
        Bytes = 4096L
        Sessions = 0
    }
}

Assert-Condition (Test-GeneratedRestoreName $script:validName) 'exact generated name accepted'
Assert-Condition (-not (Test-GeneratedRestoreName 'photoidentity_main')) 'production name excluded'
Assert-Condition (-not (Test-GeneratedRestoreName 'photoidentity_restore_20261031235500_abcdef_extra')) 'suffix excluded'
Assert-Condition (-not (Test-GeneratedRestoreName "photoidentity_restore_20261031235500_abcdef';drop database x;--")) 'SQL injection excluded'
Assert-Condition (Test-RestoreOwnershipMarker $script:validMarker) 'valid marker accepted'
Assert-Condition (-not (Test-RestoreOwnershipMarker 'photoidentity-restore:v1:abc')) 'incomplete marker excluded'

Reset-Record
Remove-OwnedRestoreDatabase -Name $script:validName -ExpectedMarker $script:validMarker -SourceDatabase 'photoidentity_main'
Assert-Condition ($script:dropCalls -eq 1) 'owned idle restore drops exactly once'

Reset-Record
Assert-Throws { Remove-OwnedRestoreDatabase -Name $script:validName -ExpectedMarker $script:validMarker -SourceDatabase $script:validName } 'production/source exclusion'
Assert-Condition ($script:dropCalls -eq 0) 'source guard blocks drop'
Assert-Throws { Remove-OwnedRestoreDatabase -Name 'photoidentity_main' -ExpectedMarker $script:validMarker -SourceDatabase 'other' } 'non-generated target'
Assert-Condition ($script:dropCalls -eq 0) 'non-generated target never dropped'
Reset-Record
$script:record.Marker = 'not-owned'
$script:record.Owned = $false
Assert-Throws { Remove-OwnedRestoreDatabase -Name $script:validName -ExpectedMarker $script:validMarker -SourceDatabase 'photoidentity_main' } 'unmarked legacy database'
Assert-Condition ($script:dropCalls -eq 0) 'unmarked database never dropped'
Reset-Record
$script:record.Marker = 'photoidentity-restore:v1:' + ('c' * 32) + ':' + ('b' * 64)
Assert-Throws { Remove-OwnedRestoreDatabase -Name $script:validName -ExpectedMarker $script:validMarker -SourceDatabase 'photoidentity_main' } 'mismatched ownership token'
Assert-Condition ($script:dropCalls -eq 0) 'mismatched marker never dropped'
Reset-Record
$script:record.Sessions = 1
Assert-Throws { Remove-OwnedRestoreDatabase -Name $script:validName -ExpectedMarker $script:validMarker -SourceDatabase 'photoidentity_main' } 'live session'
Assert-Condition ($script:dropCalls -eq 0) 'active connection never terminated'

# Automatic cleanup tolerates short-lived sessions, with strict bounds and
# revalidated marker on EVERY attempt. Manual cleanup above remains immediate.
Reset-Record
$script:transientBusyChecks = 2
Remove-OwnedRestoreDatabase -Name $script:validName -ExpectedMarker $script:validMarker `
    -SourceDatabase 'photoidentity_main' -SessionWaitAttempts 3 -SessionWaitMilliseconds 1
Assert-Condition ($script:dropCalls -eq 1) 'transient sessions allow one safe drop after becoming idle'
Assert-Condition ($script:detailChecks -eq 4) 'two busy probes, idle probe, and post-drop check'

Reset-Record
$script:record.Sessions = 1
Assert-Throws {
    Remove-OwnedRestoreDatabase -Name $script:validName -ExpectedMarker $script:validMarker `
        -SourceDatabase 'photoidentity_main' -SessionWaitAttempts 3 -SessionWaitMilliseconds 1
} 'persistent sessions still prevent automatic cleanup'
Assert-Condition ($script:detailChecks -eq 3 -and $script:dropCalls -eq 0) 'bounded busy probes never drop'

Reset-Record
$script:transientBusyChecks = 1
$script:markerFlipAfterChecks = 1
Assert-Throws {
    Remove-OwnedRestoreDatabase -Name $script:validName -ExpectedMarker $script:validMarker `
        -SourceDatabase 'photoidentity_main' -SessionWaitAttempts 3 -SessionWaitMilliseconds 1
} 'changed ownership during retry fails closed'
Assert-Condition ($script:dropCalls -eq 0 -and $script:detailChecks -eq 2) 'ownership rechecked before drop'

Reset-Record
$script:record = $null
Assert-Throws { Remove-OwnedRestoreDatabase -Name $script:validName -ExpectedMarker $script:validMarker -SourceDatabase 'photoidentity_main' } 'missing target'
Assert-Condition ($script:dropCalls -eq 0) 'missing target never dropped'
Reset-Record
$script:throwDrop = $true
Assert-Throws { Remove-OwnedRestoreDatabase -Name $script:validName -ExpectedMarker $script:validMarker -SourceDatabase 'photoidentity_main' } 'failed drop propagated'
Assert-Condition ($script:dropCalls -eq 1) 'failed drop attempted only once'
Reset-Record
$script:postDropMissing = $false
Assert-Throws { Remove-OwnedRestoreDatabase -Name $script:validName -ExpectedMarker $script:validMarker -SourceDatabase 'photoidentity_main' } 'database remains after nominal drop'

$root = Join-Path ([IO.Path]::GetTempPath()) ('wi0208-report-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($root) | Out-Null
try {
    $path = Join-Path $root 'verification.json'
    $report = [ordered]@{ passed = $true; cleanupStatus = 'pending'; cleanupRequired = $false }
    Write-RestoreVerificationReport -Path $path -Report $report
    Assert-Condition ((Get-Content $path -Raw | ConvertFrom-Json).cleanupStatus -eq 'pending') 'initial report precedes drop'
    $report.cleanupStatus = 'removed'
    Write-RestoreVerificationReport -Path $path -Report $report
    Assert-Condition ((Get-Content $path -Raw | ConvertFrom-Json).cleanupStatus -eq 'removed') 'atomic report replacement'
    Assert-Condition (@(Get-ChildItem $root -Filter '*.tmp').Count -eq 0) 'no temporary report artifacts'
    Assert-Throws { Write-RestoreVerificationReport -Path (Join-Path $root 'missing\verification.json') -Report $report } 'report publication failure propagates'
}
finally {
    Remove-Item -LiteralPath $root -Force -Recurse -ErrorAction SilentlyContinue
}
Write-Host "WI-0208 synthetic safety/report assertions: $script:checked passed."
