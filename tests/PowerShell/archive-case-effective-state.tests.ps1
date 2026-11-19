# Offline synthetic test for effective-state reconciliation audit. No PostgreSQL.
[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$script = Join-Path $repo 'tools\summarize-archive-case-effective-state.ps1'
$root = Join-Path ([IO.Path]::GetTempPath()) ('photoidentity-effective-state-' + [guid]::NewGuid().ToString('N'))
$auditCsv = Join-Path $root 'audit.csv'
$stateCsv = Join-Path $root 'state.csv'
$outputDir = Join-Path $root 'output'

function Assert([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}
function New-AuditMember([string]$Key, [string]$Asset, [long]$History) {
    [pscustomobject]@{
        source_id = 'source-1'
        normalized_source_key = $Key.ToLowerInvariant()
        asset_id = $Asset
        manual_history_actions = "$History"
        face_occurrences = '0'
    }
}
function New-State([string]$Key, [string]$A, [string]$B) {
    [pscustomobject]@{
        source_id = 'source-1'
        normalized_source_key = $Key.ToLowerInvariant()
        first_asset_id = $A
        second_asset_id = $B
        both_verified = 't'
        matching_stored_hash = 't'
        first_marked_missing = 'f'
        second_marked_missing = 'f'
        matching_effective_tags = 't'
        matching_effective_manual_people = 't'
        matching_effective_place = 't'
        matching_place_provenance = 't'
        matching_confirmed_face_people = 't'
        first_effective_tags = '0'
        second_effective_tags = '0'
        first_effective_manual_people = '0'
        second_effective_manual_people = '0'
        first_confirmed_face_people = '0'
        second_confirmed_face_people = '0'
        first_reviewed_faces = '0'
        second_reviewed_faces = '0'
    }
}

try {
    New-Item -ItemType Directory -Path $root -Force | Out-Null
    $audit = @(
        (New-AuditMember 'Fideli/both-conflict.jpg' 'both-a' 3)
        (New-AuditMember 'Fideli/both-conflict.jpg' 'both-b' 2)
        (New-AuditMember 'Fideli/both-equal.jpg' 'equal-a' 3)
        (New-AuditMember 'Fideli/both-equal.jpg' 'equal-b' 4)
        (New-AuditMember 'Fideli/one.jpg' 'one-a' 1)
        (New-AuditMember 'Fideli/one.jpg' 'one-b' 0)
        (New-AuditMember 'Fideli/none.jpg' 'none-a' 0)
        (New-AuditMember 'Fideli/none.jpg' 'none-b' 0)
        (New-AuditMember 'Fideli/missing.jpg' 'missing-a' 0)
        (New-AuditMember 'Fideli/missing.jpg' 'missing-b' 0)
    )
    $states = @(
        (New-State 'Fideli/both-conflict.jpg' 'both-a' 'both-b')
        (New-State 'Fideli/both-equal.jpg' 'equal-a' 'equal-b')
        (New-State 'Fideli/one.jpg' 'one-a' 'one-b')
        (New-State 'Fideli/none.jpg' 'none-a' 'none-b')
        (New-State 'Fideli/missing.jpg' 'missing-a' 'missing-b')
    )
    $states[0].matching_effective_tags = 'f'
    $states[0].matching_effective_manual_people = 'f'
    $states[0].first_effective_tags = '1'
    $states[0].first_effective_manual_people = '1'
    $states[0].matching_confirmed_face_people = 'f'
    $states[0].first_confirmed_face_people = '1'
    $states[3].matching_effective_place = 'f'
    $states[3].first_reviewed_faces = '2'
    $states[3].second_reviewed_faces = '1'
    $states[4].first_marked_missing = 't'

    $audit | Export-Csv -LiteralPath $auditCsv -Encoding utf8
    $states | Export-Csv -LiteralPath $stateCsv -Encoding utf8

    $summary = & $script -AuditCsv $auditCsv -EffectiveStateCsv $stateCsv -OutputDirectory $outputDir
    Assert ($summary.CollisionGroups -eq 5) 'Wrong group count.'
    Assert ($summary.BothManualHistory -eq 2) 'Wrong both-annotated count.'
    Assert ($summary.OneManualHistory -eq 1) 'Wrong single-annotated count.'
    Assert ($summary.NoManualHistory -eq 2) 'Wrong unannotated count.'
    Assert ($summary.GroupsWithAnyEffectiveDifference -eq 2) 'Wrong difference count.'
    Assert ($summary.BothAnnotatedWithDifferences -eq 1) 'Missed both-annotated conflict.'
    Assert ($summary.GroupsWithDifferentTags -eq 1) 'Wrong tag conflict count.'
    Assert ($summary.GroupsWithDifferentPlace -eq 1) 'Wrong Place conflict count.'
    Assert ($summary.HoldSourceEvidence -eq 1) 'Missing source evidence not blocked.'
    Assert ($summary.AutomaticallyDeletable -eq 0) 'A destructive candidate was authorized.'

    $rows = @(Import-Csv (Join-Path $outputDir 'effective-state-review.csv'))
    Assert ($rows.Count -eq 5) 'Report must contain one row per pair.'
    Assert (@($rows | Where-Object { $_.SafeToDelete -ne 'False' }).Count -eq 0) 'Report allows deletion.'
    Assert (@($rows | Where-Object { $_.RequiresOperatorReview -ne 'True' }).Count -eq 0) 'Report omits required review.'
    $both = @($rows | Where-Object { $_.NormalizedSourceKey -eq 'fideli/both-conflict.jpg' })[0]
    Assert ($both.Decision -eq 'hold-both-annotated-effective-differences') 'Both-annotated conflict not blocked.'
    $same = @($rows | Where-Object { $_.NormalizedSourceKey -eq 'fideli/both-equal.jpg' })[0]
    Assert ($same.Decision -eq 'hold-both-annotated-history-migration') 'Matching effective state must not authorize deletion.'

    $first = Get-Content -LiteralPath (Join-Path $outputDir 'effective-state-review.csv') -Raw
    $null = & $script -AuditCsv $auditCsv -EffectiveStateCsv $stateCsv -OutputDirectory $outputDir
    Assert ((Get-Content -LiteralPath (Join-Path $outputDir 'effective-state-review.csv') -Raw) -ceq $first) 'Report is not deterministic.'

    $states[0].second_asset_id = 'WRONG-ASSET'
    $states | Export-Csv -LiteralPath $stateCsv -Encoding utf8
    $bad = $false
    try { $null = & $script -AuditCsv $auditCsv -EffectiveStateCsv $stateCsv -OutputDirectory $outputDir } catch { $bad = $true }
    Assert $bad 'Mismatched assets were accepted.'
    $states[0].second_asset_id = 'both-b'

    $states[0].matching_effective_tags = 'invalid-boolean'
    $states | Export-Csv -LiteralPath $stateCsv -Encoding utf8
    $bad = $false
    try { $null = & $script -AuditCsv $auditCsv -EffectiveStateCsv $stateCsv -OutputDirectory $outputDir } catch { $bad = $true }
    Assert $bad 'Invalid Boolean values were accepted.'

    Write-Host 'Archive case-alias effective-state synthetic tests: PASS'
}
finally {
    if (Test-Path -LiteralPath $root) {
        Remove-Item -LiteralPath $root -Recurse -Force
    }
}
