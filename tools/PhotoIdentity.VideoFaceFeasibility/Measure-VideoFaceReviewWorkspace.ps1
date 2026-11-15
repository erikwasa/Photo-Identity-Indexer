<#
.SYNOPSIS
Validate and measure one private WI-0155 combined C/N/U review CSV.
.DESCRIPTION
Splits a source-bound combined worksheet into temporary source-specific CSVs,
then reuses the existing strict aggregate group/rank/N/U readers.
No database, inference, source edits, identities or private observations
are printed. The temp worksheets are removed even when validation fails.
Windows PowerShell 7.6 on a local filesystem.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ReviewedCsvPath,
    [Parameter(Mandatory)][string]$PrivateGroupReportPath,
    [Parameter(Mandatory)][string]$PrivatePeopleMapPath,
    [Parameter(Mandatory)][ValidatePattern('^V(?!00)[0-9]{2}$')]
    [string]$ClipAlias
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsWindows -or -not $env:LOCALAPPDATA) {
    throw 'Private combined review measurement requires Windows PowerShell 7.6.'
}
function RegularLocal([string]$Path,[long]$Maximum) {
    if (-not [IO.Path]::IsPathFullyQualified($Path)) {
        throw 'Private input paths must be fully qualified.'
    }
    $item = Get-Item -LiteralPath $Path -Force -ErrorAction Stop
    $blocked = [long][IO.FileAttributes]::ReparsePoint -bor
        [long][IO.FileAttributes]::Offline -bor [long]0x00040000 -bor [long]0x00400000
    if ($item.PSIsContainer -or $item.Length -lt 1 -or
        $item.Length -gt $Maximum -or
        ([long]$item.Attributes -band $blocked) -ne 0 -or
        ((Get-Item -LiteralPath $item.DirectoryName -Force).Attributes -band
          [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw 'Private input must be a regular local file.'
    }
    return $item.FullName
}
$csvPath = RegularLocal $ReviewedCsvPath (256KB)
$reportPath = RegularLocal $PrivateGroupReportPath (64MB)
$mapPath = RegularLocal $PrivatePeopleMapPath (4MB)
$hash = (Get-FileHash -LiteralPath $reportPath -Algorithm SHA256).Hash.ToLowerInvariant()

$rows = @(Import-Csv -LiteralPath $csvPath -Encoding utf8)
$columns = @('Source','Clip','AuditReportSha256','Group','Observations',
  'Decision','MergeInto','SelectedPerson','ReviewerNotes',
  'Sample','Frame','Face','Label','PersonAlias')
$actual = if ($rows.Count -gt 0) {
    @($rows[0].PSObject.Properties | ForEach-Object Name)
} else { @() }
if ($rows.Count -lt 3 -or $rows.Count -gt 256 -or
    ($actual -join '|') -cne ($columns -join '|')) {
    throw 'Unrecognized or oversized private combined review worksheet.'
}
$groups = [Collections.Generic.List[object]]::new()
$noise = [Collections.Generic.List[object]]::new()
$uncorroborated = [Collections.Generic.List[object]]::new()
$seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$stage = 0
foreach ($row in $rows) {
    if ([string]$row.Clip -cne $ClipAlias -or
        [string]$row.AuditReportSha256 -cne $hash) {
        throw 'Combined review does not match the clip or report SHA-256.'
    }
    $source = [string]$row.Source
    $next = switch -CaseSensitive ($source) {
        'CenterFaceShadow' { 0 }
        'CenterFaceNoise' { 1 }
        'CenterFaceUncorroborated' { 2 }
        default { throw 'Unknown private review source.' }
    }
    if ($next -lt $stage) { throw 'Private review sources are reordered.' }
    $stage = $next
    if ($next -eq 0) {
        if ([string]$row.Group -cnotmatch '^C(?!000)[0-9]{3}$' -or
            [string]$row.Observations -cnotmatch '^[1-9][0-9]*$' -or
            $row.Sample -cne '' -or $row.Frame -cne '' -or
            $row.Face -cne '' -or $row.Label -cne '' -or
            $row.PersonAlias -cne '' -or
            -not $seen.Add('C:' + [string]$row.Group)) {
            throw 'Invalid or duplicated C-group evidence.'
        }
        $groups.Add([pscustomobject][ordered]@{
            Clip=$ClipAlias;Group=$row.Group;Observations=$row.Observations
            Decision=$row.Decision;MergeInto=$row.MergeInto
            SelectedPerson=$row.SelectedPerson;ReviewerNotes=$row.ReviewerNotes
        })
    } else {
        $prefix = if ($next -eq 1) { 'N' } else { 'U' }
        $target = if ($next -eq 1) { $noise } else { $uncorroborated }
        if ([string]$row.Sample -cnotmatch ('^' + $prefix + '(?!000)[0-9]{3}$') -or
            [string]$row.Frame -cnotmatch '^[1-9][0-9]*$' -or
            [string]$row.Face -cnotmatch '^[1-9][0-9]*$' -or
            $row.Group -cne '' -or $row.Observations -cne '' -or
            $row.Decision -cne '' -or $row.MergeInto -cne '' -or
            $row.SelectedPerson -cne '' -or $row.ReviewerNotes -cne '' -or
            -not $seen.Add($prefix + ':' + [string]$row.Sample)) {
            throw 'Invalid or duplicated private audit evidence.'
        }
        $target.Add([pscustomobject][ordered]@{
            Clip=$ClipAlias;AuditReportSha256=$hash;Sample=$row.Sample
            Frame=$row.Frame;Face=$row.Face;Label=$row.Label
            PersonAlias=$row.PersonAlias
        })
    }
}
if ($groups.Count -lt 1 -or $groups.Count -gt 64 -or
    $noise.Count -lt 1 -or $noise.Count -gt 96 -or
    $uncorroborated.Count -lt 1 -or $uncorroborated.Count -gt 96) {
    throw 'Missing or excessive C/N/U source rows.'
}
# The existing readers verify each sample's ordinal, frame and face against
# the report, and enforce all human-decision rules; no duplicates can pass.
$root = Join-Path $env:LOCALAPPDATA 'PhotoIdentity\WI0155\workspace-measure'
New-Item -ItemType Directory -Path $root -Force | Out-Null
if (((Get-Item -LiteralPath $root -Force).Attributes -band
    [IO.FileAttributes]::ReparsePoint) -ne 0) {
    throw 'Temporary private measurement directory must not be linked.'
}
$folder = Join-Path $root ([Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $folder | Out-Null
try {
    $gCsv = Join-Path $folder 'C.csv'
    $nCsv = Join-Path $folder 'N.csv'
    $uCsv = Join-Path $folder 'U.csv'
    $groups | Export-Csv -LiteralPath $gCsv -NoTypeInformation -Encoding utf8
    $noise | Export-Csv -LiteralPath $nCsv -NoTypeInformation -Encoding utf8
    $uncorroborated | Export-Csv -LiteralPath $uCsv -NoTypeInformation -Encoding utf8

    $groupArgs = @{
        ReviewedCsvPath = $gCsv
        PrivateGroupReportPath = $reportPath
        ClipAlias = $ClipAlias
        GroupSource = 'CenterFaceShadow'
    }
    $groupResult = & (Join-Path $PSScriptRoot 'Measure-VideoFaceGroupReview.ps1') @groupArgs
    $rankResult = & (Join-Path $PSScriptRoot 'Measure-VideoFaceGroupRankAccuracy.ps1') @groupArgs -PrivatePeopleMapPath $mapPath
    $noiseResult = & (Join-Path $PSScriptRoot 'Measure-VideoFaceUnassignedReview.ps1') -ReviewedCsvPath $nCsv -PrivateGroupReportPath $reportPath -ClipAlias $ClipAlias -AuditSource CenterFaceNoise
    $nonmatchResult = & (Join-Path $PSScriptRoot 'Measure-VideoFaceUnassignedReview.ps1') -ReviewedCsvPath $uCsv -PrivateGroupReportPath $reportPath -ClipAlias $ClipAlias -AuditSource CenterFaceUncorroborated

    if (-not $groupResult.PrivateInputValidated -or
        -not $rankResult.InputEvidenceValidated -or
        -not $noiseResult.SourceEvidenceValidated -or
        -not $nonmatchResult.SourceEvidenceValidated) {
        throw 'Private group-first aggregate source validation failed.'
    }
    [pscustomobject]@{
        SchemaVersion = 1
        ClipAlias = $ClipAlias
        CombinedEvidenceValidated = $true
        GroupMetrics = $groupResult
        RankMetrics = $rankResult
        NoiseMetrics = $noiseResult
        UncorroboratedMetrics = $nonmatchResult
    }
} finally {
    if (Test-Path -LiteralPath $folder -PathType Container) {
        Remove-Item -LiteralPath $folder -Recurse -Force
    }
}
