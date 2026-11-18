<#
.SYNOPSIS
Inspect, retain selected frame files, or explicitly release ONE WI-0155-owned
temporary coverage run. No automatic cleanup or deletion of unmarked runs.
.EXAMPLE
.\Complete-VideoFaceCoverageRun.ps1 -RunDirectory $coverage.FramesDirectory
.EXAMPLE
.\Complete-VideoFaceCoverageRun.ps1 -RunDirectory $coverage.FramesDirectory -VerifiedReportPath $privateReport -RetainFrames @('frame-001.png') -RetentionDirectory $privateRetention -ApproveCleanup
.EXAMPLE
.\Complete-VideoFaceCoverageRun.ps1 -RunDirectory $coverage.FramesDirectory -AbandonRun -ApproveCleanup
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $RunDirectory,
    [string] $VerifiedReportPath,
    [string[]] $RetainFrames = @(),
    [string] $RetentionDirectory,
    [switch] $AbandonRun,
    [switch] $ApproveCleanup
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-RegularPath {
    param([string] $Path, [bool] $Directory)
    $item = Get-Item -LiteralPath $Path -Force -ErrorAction Stop
    if ([bool]$item.PSIsContainer -ne $Directory) { throw 'Unexpected file/directory type.' }
    $unsafe = [long][IO.FileAttributes]::ReparsePoint -bor [long][IO.FileAttributes]::Offline -bor
        [long]0x00040000 -bor [long]0x00400000
    if (([long]$item.Attributes -band $unsafe) -ne 0) {
        throw 'Offline, linked or reparse-point paths are not permitted.'
    }
    return $item
}

if ([string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
    throw 'LOCALAPPDATA is required.'
}
$root = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'PhotoIdentity\Temp\wi0155\coverage'))
$run = [IO.Path]::GetFullPath($RunDirectory)
$comparison = [StringComparison]::OrdinalIgnoreCase
if (-not [string]::Equals([IO.Path]::GetDirectoryName($run.TrimEnd('\','/')), $root, $comparison) -or
    -not [IO.Path]::GetFileName($run).StartsWith('coverage-', [StringComparison]::Ordinal)) {
    throw 'Only an immediate WI-0155 coverage child directory can be managed.'
}
[void](Assert-RegularPath -Path $root -Directory $true)
[void](Assert-RegularPath -Path $run -Directory $true)
$markerPath = Join-Path $run 'wi0155-run.json'
[void](Assert-RegularPath -Path $markerPath -Directory $false)
$marker = Get-Content -LiteralPath $markerPath -Raw | ConvertFrom-Json
$parsedRunId = [guid]::Empty
if ($marker.schemaVersion -ne 1 -or
    $marker.kind -cne 'PhotoIdentity.WI0155.CoverageWorkspace' -or
    $marker.state -cnotin @('extracting','extracted') -or
    -not [guid]::TryParse([string]$marker.runId, [ref]$parsedRunId)) {
    throw 'Missing or invalid owned-workspace marker.'
}

$allowed = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($name in @('wi0155-run.json','frames.json','extraction-metadata.json')) {
    [void]$allowed.Add($name)
}
$frames = @()
$manifestPath = Join-Path $run 'frames.json'
if (Test-Path -LiteralPath $manifestPath -PathType Leaf) {
    [void](Assert-RegularPath -Path $manifestPath -Directory $false)
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($manifest.schemaVersion -ne 1 -or @($manifest.frames).Count -lt 1 -or
        @($manifest.frames).Count -gt 180) { throw 'Invalid frame manifest.' }
    $frames = @($manifest.frames | ForEach-Object { [string]$_.file })
    if (@($frames | Sort-Object -Unique).Count -ne $frames.Count) {
        throw 'Duplicate frame names.'
    }
    foreach ($name in $frames) {
        if ($name -cnotmatch '^frame-[0-9]{3}\.(png|jpg)$') {
            throw 'Unexpected frame manifest filename.'
        }
        [void]$allowed.Add($name)
    }
}
# An interrupted extraction may lack frames.json. In that case only
# machine-generated frame names and the three known metadata names are allowed.
$entries = @(Get-ChildItem -LiteralPath $run -Force)
foreach ($entry in $entries) {
    [void](Assert-RegularPath -Path $entry.FullName -Directory $false)
    if (-not $allowed.Contains($entry.Name)) {
        if ($marker.state -cne 'extracting' -or
            $entry.Name -cnotmatch '^frame-[0-9]{3}\.(png|jpg)$') {
            throw 'Unexpected file in workspace; refusing to delete anything.'
        }
        [void]$allowed.Add($entry.Name)
    }
}
if ($marker.state -ceq 'extracted' -and
    (@($frames).Count -eq 0 -or @($entries | Where-Object {
        $_.Name -match '^frame-[0-9]{3}\.(png|jpg)$'
    }).Count -ne $frames.Count)) {
    throw 'Completed run does not match its frame manifest.'
}

if ($RetainFrames.Count -gt 0 -and ($AbandonRun -or -not $ApproveCleanup)) {
    throw 'Retention requires explicit completion and cleanup approval.'
}
if ($RetainFrames.Count -gt 0 -and [string]::IsNullOrWhiteSpace($RetentionDirectory)) {
    throw 'Specify an independent retention directory.'
}
if (-not $ApproveCleanup) {
    [pscustomobject]@{
        RunId = $marker.runId
        State = $marker.state
        Files = $entries.Count
        Bytes = ($entries | Measure-Object Length -Sum).Sum
        CleanupApproved = $false
    }
    return
}
if (-not $AbandonRun) {
    if ($marker.state -cne 'extracted') {
        throw 'Only completed extraction runs can be finalized; use -AbandonRun for failed runs.'
    }
    if ([string]::IsNullOrWhiteSpace($VerifiedReportPath)) {
        throw 'Provide an existing private analysis report, or explicitly abandon the run.'
    }
    $report = [IO.Path]::GetFullPath($VerifiedReportPath)
    [void](Assert-RegularPath -Path $report -Directory $false)
    if ([string]::Equals($report, $run, $comparison) -or
        $report.StartsWith($run + [IO.Path]::DirectorySeparatorChar, $comparison) -or
        $report.StartsWith($root + [IO.Path]::DirectorySeparatorChar, $comparison)) {
        throw 'Verification report must be outside the temporary coverage root.'
    }
    if ((Get-Item -LiteralPath $report).Length -eq 0) {
        throw 'The verification report is empty.'
    }
} elseif ($RetainFrames.Count -gt 0 -or $VerifiedReportPath) {
    throw 'Abandoning a run cannot retain frames or claim an analysis report.'
}

$retained = @()
if ($RetainFrames.Count -gt 0) {
    $destination = [IO.Path]::GetFullPath($RetentionDirectory)
    if ($destination.StartsWith($root + [IO.Path]::DirectorySeparatorChar, $comparison) -or
        [string]::Equals($destination, $root, $comparison)) {
        throw 'Retention must be outside the temporary coverage root.'
    }
    if (-not (Test-Path -LiteralPath $destination -PathType Container)) {
        throw 'Create the retention directory explicitly before use.'
    }
    [void](Assert-RegularPath -Path $destination -Directory $true)
    if (@($RetainFrames | Sort-Object -Unique).Count -ne $RetainFrames.Count) {
        throw 'Duplicate retention requests.'
    }
    foreach ($name in $RetainFrames) {
        if ($name -cnotin $frames) { throw 'Retention name is not in the completed manifest.' }
        $source = Join-Path $run $name
        [void](Assert-RegularPath -Path $source -Directory $false)
        $target = Join-Path $destination ('{0}-{1}' -f $marker.runId, $name)
        if (Test-Path -LiteralPath $target) { throw 'Retention target already exists.' }
    }
    foreach ($name in $RetainFrames) {
        $source = Join-Path $run $name
        $target = Join-Path $destination ('{0}-{1}' -f $marker.runId, $name)
        Copy-Item -LiteralPath $source -Destination $target -ErrorAction Stop
        $a = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
        $b = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
        if ($a -cne $b) { throw 'Retention verification failed; temporary run preserved.' }
        $retained += $target
    }
}
# Delete only validated direct children, never recurse or touch another run.
# Any failure leaves the remaining files in place for operator review.
foreach ($entry in $entries) {
    Remove-Item -LiteralPath $entry.FullName -Force -ErrorAction Stop
}
Remove-Item -LiteralPath $run -Force -ErrorAction Stop
[pscustomobject]@{
    RunId = $marker.runId
    DeletedWorkspace = $run
    RetainedCount = $retained.Count
    Abandoned = [bool]$AbandonRun
}
