# Synthetic PowerShell acceptance. No production DB, files or API.
[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$script = Join-Path $repo 'tools\get-archive-fideli-retirement-readiness.ps1'
$temp = Join-Path ([IO.Path]::GetTempPath()) ('photoidentity-fideli-readiness-' + [guid]::NewGuid().ToString('N'))
$csv = Join-Path $temp 'plan.csv'

function Assert([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}
function Row([string]$SourceKey, [string]$Survivor, [string]$Secondary,
             [string]$Action, [string]$Blockers) {
    [pscustomobject][ordered]@{
        NormalizedSourceKey = $SourceKey
        SurvivorSourceKey = $Survivor
        SecondarySourceKey = $Secondary
        ProposedAction = $Action
        Blockers = $Blockers
    }
}
try {
    New-Item -ItemType Directory -Path $temp | Out-Null
    @(
        (Row 'fideli/one.jpg' 'fideli/one.jpg' 'Fideli/one.jpg' 'review-for-reversible-alias-retirement' '')
        (Row 'fideli/two.jpg' 'Fideli/two.jpg' 'fideli/two.jpg' 'review-for-reversible-alias-retirement' '')
        (Row 'fideli/three.jpg' 'fideli/three.jpg' 'Fideli/three.jpg' 'hold' 'later-asset-has-protected-history-or-references;effective-annotation-difference')
        (Row 'fideli/four.jpg' 'fideli/four.jpg' 'Fideli/four.jpg' 'hold' 'catalogue-missing-state')
        (Row 'other/five.jpg' 'other/five.jpg' 'Other/five.jpg' 'review-for-reversible-alias-retirement' '')
    ) | Export-Csv -LiteralPath $csv -Encoding utf8

    $report = & $script -PrivatePlanCsv $csv
    Assert ($report.TotalCollisionGroups -eq 5) 'Incorrect total.'
    Assert ($report.ScopedCollisionGroups -eq 4) 'Scoped casefold group count incorrect.'
    Assert ($report.EligibleUnretired -eq 2) 'Unexpected eligible count.'
    Assert ($report.EligibleWithLowercaseSurvivorAndUppercaseSecondary -eq 1) 'Case direction misclassified.'
    Assert ($report.EligibleOtherCaseDirection -eq 1) 'Reversed case direction not shown.'
    Assert ($report.HeldOrAlreadyRetired -eq 2) 'Held count missing.'
    Assert (@($report.HeldReasons).Count -eq 3) 'Held blocker reasons incomplete.'
    Assert ((@($report.HeldReasons | Where-Object Reason -eq 'catalogue-missing-state')[0]).Groups -eq 1) 'Retired state lost.'
    Assert ($report.AutomaticallyApplied -eq 0 -and $report.FilesDeleted -eq 0 -and $report.RowsDeleted -eq 0) 'Read-only guarantee flag changed.'
    Write-Host 'Fideli case-alias retirement readiness synthetic test: PASS'
}
finally {
    if (Test-Path -LiteralPath $temp) {
        Remove-Item -LiteralPath $temp -Recurse -Force
    }
}
