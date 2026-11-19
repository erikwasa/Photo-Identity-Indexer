# Source-free PowerShell 7.6 tests. No PostgreSQL, Podman or originals accessed.
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$scriptPath = Join-Path $repo 'tools\preview-archive-case-reconciliation.ps1'
$root = Join-Path ([IO.Path]::GetTempPath()) ("photoidentity-case-preview-" + [guid]::NewGuid().ToString("N"))
$inputCsv = Join-Path $root 'input.csv'
$outputDir = Join-Path $root 'output'
$hashA = 'a' * 64
$hashB = 'b' * 64

function Assert {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

function New-Row {
    param(
        [string]$Key,
        [string]$Asset,
        [int]$History,
        [string]$Hash = $hashA,
        [string]$Verification = 'verified',
        [bool]$Missing = $false
    )
    return [pscustomobject]@{
        source_id = 'test-source'
        normalized_source_key = $Key.Replace('\','/').ToLowerInvariant()
        asset_id = $Asset
        source_key = $Key
        asset_created_at_utc = '2026-11-19T10:00:00Z'
        marked_missing = "$Missing"
        verification_state = $Verification
        verified_revision_id = "$Asset-revision"
        verified_content_sha256 = $Hash
        tag_actions = "$History"
        person_actions = '0'
        manual_place_actions = '0'
        face_review_actions = '0'
        person_labels = '0'
        face_occurrences = '0'
        manual_history_actions = "$History"
    }
}

try {
    New-Item -ItemType Directory -Path $root -Force | Out-Null
    $rows = @(
        (New-Row 'Fideli/both.jpg' 'both-old' 2)
        (New-Row 'fideli/both.jpg' 'both-new' 1)
        (New-Row 'Fideli/one.jpg' 'one-old' 2)
        (New-Row 'fideli/one.jpg' 'one-new' 0)
        (New-Row 'Fideli/none.jpg' 'none-old' 0)
        (New-Row 'fideli/none.jpg' 'none-new' 0)
        (New-Row 'Fideli/different.jpg' 'diff-old' 0 -Hash $hashA)
        (New-Row 'fideli/different.jpg' 'diff-new' 0 -Hash $hashB)
        (New-Row 'Fideli/unverified.jpg' 'pending-old' 1 -Verification 'unverified')
        (New-Row 'fideli/unverified.jpg' 'pending-new' 0)
        (New-Row 'Fideli/missing.jpg' 'missing-old' 1 -Missing $true)
        (New-Row 'fideli/missing.jpg' 'missing-new' 0)
    )
    $rows | Export-Csv -LiteralPath $inputCsv -Encoding utf8

    $summary = & $scriptPath -AuditCsv $inputCsv -OutputDirectory $outputDir
    Assert ($summary.InputRows -eq 12) 'Input count mismatch.'
    Assert ($summary.CollisionGroups -eq 6) 'Collision count mismatch.'
    Assert ($summary.GroupsWithBothManualHistories -eq 1) 'Dual-annotated pair was not classified.'
    Assert ($summary.GroupsWithOneManualHistory -eq 3) 'Single-annotated pair count mismatch.'
    Assert ($summary.GroupsWithNoManualHistory -eq 2) 'Unannotated pair count mismatch.'
    Assert ($summary.DifferentVerifiedHashPairs -eq 1) 'Hash disagreement was not detected.'
    Assert ($summary.IncompleteVerificationPairs -eq 1) 'Missing verification not detected.'
    Assert ($summary.AutomaticallyDeletable -eq 0) 'The preview authorized deletion.'

    $reportPath = Join-Path $outputDir 'case-alias-reconciliation-preview.csv'
    $report = @(Import-Csv -LiteralPath $reportPath)
    Assert ($report.Count -eq 6) 'Expected exactly one report row per collision pair.'
    Assert (@($report | Where-Object { $_.SafeToDelete -ne 'false' }).Count -eq 0) 'Deletion authorization appeared.'
    Assert (@($report | Where-Object { $_.RequiresOperatorReview -ne 'true' }).Count -eq 0) 'Operator review omitted.'

    $one = @($report | Where-Object { $_.NormalizedSourceKey -eq 'fideli/one.jpg' })[0]
    Assert ($one.CandidateKeeperAssetId -eq 'one-old') 'Annotated keeper candidate not retained.'
    Assert ($one.Decision -eq 'review-single-annotated-keeper-candidate') 'One-annotated classification wrong.'
    $both = @($report | Where-Object { $_.NormalizedSourceKey -eq 'fideli/both.jpg' })[0]
    Assert ($both.CandidateKeeperAssetId -eq '') 'Both annotated pair must not select a keeper.'
    Assert ($both.Decision -eq 'hold-both-have-manual-history') 'Both annotated pair is not blocked.'
    $different = @($report | Where-Object { $_.NormalizedSourceKey -eq 'fideli/different.jpg' })[0]
    Assert ($different.Decision -eq 'hold-hash-disagreement') 'Hash disagreement not blocking.'
    $unverified = @($report | Where-Object { $_.NormalizedSourceKey -eq 'fideli/unverified.jpg' })[0]
    Assert ($unverified.Decision -eq 'hold-incomplete-verification') 'Unverified pair not blocked.'
    $missing = @($report | Where-Object { $_.NormalizedSourceKey -eq 'fideli/missing.jpg' })[0]
    Assert ($missing.Decision -eq 'hold-missing-catalogue-record') 'Missing pair not blocked.'

    $before = Get-Content -LiteralPath $reportPath -Raw
    $null = & $scriptPath -AuditCsv $inputCsv -OutputDirectory $outputDir
    Assert ((Get-Content -LiteralPath $reportPath -Raw) -ceq $before) 'Preview is not deterministic.'

    # Any incomplete audit must be refused rather than being misinterpreted as
    # no annotations.
    $bad = Join-Path $root 'bad.csv'
    'source_id,source_key' | Set-Content -LiteralPath $bad -Encoding utf8
    'fake,path.jpg' | Add-Content -LiteralPath $bad
    $threw = $false
    try { $null = & $scriptPath -AuditCsv $bad -OutputDirectory $outputDir } catch { $threw = $true }
    Assert $threw 'Incomplete audit CSV was accepted.'

    Write-Host 'Archive case-alias preview synthetic tests: PASS'
}
finally {
    if (Test-Path -LiteralPath $root) {
        Remove-Item -LiteralPath $root -Force -Recurse
    }
}
