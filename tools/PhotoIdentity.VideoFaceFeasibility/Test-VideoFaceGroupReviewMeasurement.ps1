<#
.SYNOPSIS
Windows PowerShell 7.6 synthetic tests for private WI-0155 group measurements.
No real clips, people, face images, inference, DB or network access.
#>
[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

$folder = Join-Path $env:TEMP (
    'wi0155-measure-test-' + [Guid]::NewGuid().ToString('N'))
New-Item -Path $folder -ItemType Directory | Out-Null
try {
    $csv = Join-Path $folder 'review.csv'
    $json = Join-Path $folder 'group-report.json'
    $script = Join-Path $PSScriptRoot 'Measure-VideoFaceGroupReview.ps1'
    $groups = @(
        @{Group='G001'; Observations=1; Decision='Keep group'; MergeInto=''},
        @{Group='G002'; Observations=1; Decision='Merge group'; MergeInto='G001'},
        @{Group='G003'; Observations=1; Decision='Keep group'; MergeInto=''},
        @{Group='G004'; Observations=54; Decision='Dismiss false detection'; MergeInto=''},
        @{Group='G005'; Observations=1; Decision='Dismiss false detection'; MergeInto=''},
        @{Group='G006'; Observations=1; Decision='Merge group'; MergeInto='G003'}
    )
    $rows = @(
        foreach ($item in $groups) {
            [pscustomobject][ordered]@{
                Clip='V03'
                Group=$item.Group
                Observations=$item.Observations
                Decision=$item.Decision
                MergeInto=$item.MergeInto
                SelectedPerson=''
                ReviewerNotes=''
            }
        }
    )
    $rows | Export-Csv -LiteralPath $csv -NoTypeInformation -Encoding utf8

    $groupJson = @(
        foreach ($item in $groups) {
            @{alias=$item.Group; observations=$item.Observations; representativeCrops=@()}
        }
    )
    @{schemaVersion=1;clips=@(
        @{alias='V03';review=@{
            aggregate=@{facesDetected=82}
            diagnostics=@{provisionalGroups=@{
                schemaVersion=1; maximumGroups=64
                observationsConsidered=82
                observationsAssigned=59
                observationsUnassigned=23
                groups=$groupJson
            }}
        }}
    )} | ConvertTo-Json -Depth 12 |
        Set-Content -LiteralPath $json -Encoding utf8

    $result = & $script -ReviewedCsvPath $csv -PrivateGroupReportPath $json
    Assert-True ($result.TotalGroups -eq 6) 'Incorrect group total.'
    Assert-True ($result.GroupsReviewed -eq 6) 'Incorrect reviewed count.'
    Assert-True ($result.KeptGroups -eq 2 -and $result.MergeGroups -eq 2) 'Incorrect merge/keep counts.'
    Assert-True ($result.DismissedGroups -eq 2 -and $result.ProposedFinalGroups -eq 2) 'Incorrect final review outcome.'
    Assert-True ($result.AssignedGroupObservations -eq 59 -and
        $result.DismissedGroupObservations -eq 55 -and
        $result.KeptOrMergeObservations -eq 4) 'Incorrect assigned/dismissed counts.'
    Assert-True ($result.ObservationsUnassigned -eq 23 -and
        $result.GroupCapacityReached -eq $false) 'Incorrect grouped JSON reconciliation.'
    $resultWithoutReport = & $script -ReviewedCsvPath $csv
    Assert-True ($null -eq $resultWithoutReport.ObservationsUnassigned -and
        $null -eq $resultWithoutReport.GroupCapacityReached) 'CSV-only mode claimed unavailable unassigned evidence.'

    # Ensure private aliases, paths, notes and IDs are not in the aggregate result.
    $text = $result | ConvertTo-Json -Depth 5
    foreach ($private in @('G001','V03','review.csv','group-report.json','SelectedPerson')) {
        Assert-True (-not $text.Contains($private)) 'Private review content leaked in aggregate.'
    }

    # Invalid or circular merge chains must be rejected.
    $rows[0].Decision = 'Merge group'
    $rows[0].MergeInto = 'G002'
    $rows | Export-Csv -LiteralPath $csv -NoTypeInformation -Encoding utf8
    $refused = $false
    try {
        & $script -ReviewedCsvPath $csv | Out-Null
    } catch { $refused = $true }
    Assert-True $refused 'Circular merge was accepted.'

    # Restore the good CSV; fail closed on a mismatched source report.
    $rows[0].Decision = 'Keep group'
    $rows[0].MergeInto = ''
    $rows | Export-Csv -LiteralPath $csv -NoTypeInformation -Encoding utf8
    $mismatched = Join-Path $folder 'inconsistent-report.json'
    $changedReport = Get-Content -LiteralPath $json -Raw | ConvertFrom-Json
    $changedReport.clips[0].review.diagnostics.provisionalGroups.observationsAssigned = 60
    $changedReport | ConvertTo-Json -Depth 12 |
        Set-Content -LiteralPath $mismatched -Encoding utf8
    $refused = $false
    try {
        & $script -ReviewedCsvPath $csv -PrivateGroupReportPath $mismatched |
            Out-Null
    } catch { $refused = $true }
    Assert-True $refused 'Mismatched candidate totals were accepted.'

    Write-Host 'WI-0155 group-review measurement synthetic tests: PASS'
}
finally {
    if (Test-Path -LiteralPath $folder) {
        Remove-Item -LiteralPath $folder -Recurse -Force
    }
}
