# Offline and non-destructive: recompute a bounded pilot from fresh read-only
# evidence, never an old untrusted candidate manifest. No database/apply code.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$RetirementEvidenceCsv,
    [Parameter(Mandatory)][string]$EffectiveStateCsv,
    [ValidateRange(1, 5)][int]$MaxPairs = 5,
    [ValidateRange(1, 24)][int]$MaximumEvidenceAgeHours = 4,
    [ValidateSet(44, 45)][int]$ExpectedDatabaseSchemaVersion = 44,
    [string]$OutputDirectory = (Join-Path (Get-Location).Path 'artifacts\archive-case-pilot')
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Require-FreshSource {
    param([string]$Path, [string]$Label, [datetime]$Now)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Missing private $Label CSV: $Path"
    }
    $file = Get-Item -LiteralPath $Path
    if ($file.Length -eq 0) { throw "The $Label CSV is empty." }
    $age = $Now - $file.LastWriteTimeUtc
    if ($age.TotalHours -gt $MaximumEvidenceAgeHours -or $age.TotalMinutes -lt -5) {
        throw "The $Label CSV is outside the freshness window. Regenerate the read-only audits."
    }
    return $file.FullName
}

function Read-Bool {
    param([AllowNull()][string]$Value, [string]$Field)
    if ([string]::IsNullOrWhiteSpace($Value)) { throw "Missing Boolean '$Field'." }
    switch ($Value.Trim().ToLowerInvariant()) {
        'true' { return $true }
        'false' { return $false }
        't' { return $true }
        'f' { return $false }
        default { throw "Unexpected Boolean '$Field'." }
    }
}

$now = [datetime]::UtcNow
$evidencePath = Require-FreshSource $RetirementEvidenceCsv 'retirement evidence' $now
$effectivePath = Require-FreshSource $EffectiveStateCsv 'effective-state' $now
if ($evidencePath -ceq $effectivePath) {
    throw 'The retirement and effective-state audit CSVs must be distinct.'
}
$evidenceWritten = (Get-Item -LiteralPath $evidencePath).LastWriteTimeUtc
$effectiveWritten = (Get-Item -LiteralPath $effectivePath).LastWriteTimeUtc
if ([math]::Abs(($evidenceWritten - $effectiveWritten).TotalMinutes) -gt 15) {
    throw 'The two private audit exports were captured too far apart. Re-export both within 15 minutes.'
}

$planner = Join-Path $PSScriptRoot 'new-archive-case-alias-retirement-plan.ps1'
if (-not (Test-Path -LiteralPath $planner -PathType Leaf)) {
    throw 'The authoritative offline candidate planner is missing.'
}

# Rebuild ALL eligibility decisions; do not trust a saved or edited candidate CSV.
$recheckDirectory = Join-Path $OutputDirectory 'revalidated-input'
$summary = & $planner -EvidenceCsv $evidencePath -EffectiveStateCsv $effectivePath -OutputDirectory $recheckDirectory
$revalidated = @(Import-Csv -LiteralPath $summary.PrivatePlanPath)
if ($revalidated.Count -ne $summary.CollisionGroups) {
    throw 'The revalidated candidate report has an unexpected row count.'
}
$eligible = @($revalidated |
    Where-Object { $_.ProposedAction -eq 'review-for-reversible-alias-retirement' } |
    Sort-Object -Property SourceId, NormalizedSourceKey, SecondaryAssetId)
if ($eligible.Count -ne $summary.ReversibleRetirementCandidates) {
    throw 'Candidate selection disagrees with the authoritative planner.'
}
foreach ($candidate in $eligible) {
    if ((Read-Bool $candidate.ApplyEnabled 'ApplyEnabled') -or
        -not (Read-Bool $candidate.ApprovalRequired 'ApprovalRequired') -or
        $candidate.OriginalFilesToDelete -ne '0' -or
        $candidate.AssetRowsToDelete -ne '0' -or
        -not (Read-Bool $candidate.CurrentEffectiveAnnotationsMatch 'CurrentEffectiveAnnotationsMatch') -or
        $candidate.SecondaryProtectedReferences -ne '0' -or
        -not [string]::IsNullOrEmpty($candidate.Blockers)) {
        throw 'A candidate violated the non-destructive or zero-protected-history requirements.'
    }
}
if ($eligible.Count -eq 0) {
    throw 'No retirement candidates remain. No pilot manifest has been written.'
}

# Prefer a no-auto-action candidate and, for >1 pair, include exactly one
# low-activity automatic review case to exercise history preservation.
$withoutAuto = @($eligible | Where-Object {
    [long]::Parse($_.SecondaryAutomaticReviewActions) -eq 0
})
$withAuto = @($eligible | Where-Object {
    [long]::Parse($_.SecondaryAutomaticReviewActions) -gt 0
} | Sort-Object -Property @{
    Expression = { [long]::Parse($_.SecondaryAutomaticReviewActions) };
    Ascending = $true
}, SourceId, NormalizedSourceKey)
$chosen = [System.Collections.Generic.List[object]]::new()
if ($MaxPairs -gt 1 -and $withAuto.Count -gt 0) {
    $chosen.Add($withAuto[0])
}
foreach ($candidate in $withoutAuto) {
    if ($chosen.Count -ge $MaxPairs) { break }
    $chosen.Add($candidate)
}
foreach ($candidate in $eligible) {
    if ($chosen.Count -ge $MaxPairs) { break }
    if (@($chosen | Where-Object { $_.SecondaryAssetId -ceq $candidate.SecondaryAssetId }).Count -eq 0) {
        $chosen.Add($candidate)
    }
}

$pilot = @($chosen |
    Sort-Object -Property SourceId, NormalizedSourceKey, SecondaryAssetId |
    ForEach-Object {
        [pscustomobject][ordered]@{
            SourceId = $_.SourceId
            NormalizedSourceKey = $_.NormalizedSourceKey
            SurvivorAssetId = $_.SurvivorAssetId
            SurvivorSourceKey = $_.SurvivorSourceKey
            SurvivorCreatedAtUtc = $_.SurvivorCreatedAtUtc
            SurvivorVerifiedRevisionId = $_.SurvivorVerifiedRevisionId
            SecondaryAssetId = $_.SecondaryAssetId
            SecondarySourceKey = $_.SecondarySourceKey
            SecondaryCreatedAtUtc = $_.SecondaryCreatedAtUtc
            SecondaryVerifiedRevisionId = $_.SecondaryVerifiedRevisionId
            ExpectedSha256 = $_.VerifiedContentSha256
            ExpectedSizeBytes = $_.VerifiedSizeBytes
            SecondaryAutomaticReviewActions = $_.SecondaryAutomaticReviewActions
            Action = 'proposed-reversible-retirement'
            ApprovalRequired = 'true'
            ApplyEnabled = 'false'
            DeleteSourceOriginal = 'false'
            DeleteDatabaseRows = 'false'
        }
    })
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$manifestPath = Join-Path $OutputDirectory 'pilot-preview-private.csv'
$pilot | Export-Csv -LiteralPath $manifestPath -Encoding utf8

$manifestHash = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
$evidenceHash = (Get-FileHash -LiteralPath $evidencePath -Algorithm SHA256).Hash.ToLowerInvariant()
$effectiveHash = (Get-FileHash -LiteralPath $effectivePath -Algorithm SHA256).Hash.ToLowerInvariant()
$metadataPath = Join-Path $OutputDirectory 'pilot-preview-metadata.json'
[ordered]@{
    schemaVersion = 1
    status = 'preview-only'
    expectedDatabaseSchemaVersion = $ExpectedDatabaseSchemaVersion
    generatedAtUtc = [datetime]::UtcNow.ToString('O')
    collisionGroupsRevalidated = $summary.CollisionGroups
    retirementCandidatesRevalidated = $summary.ReversibleRetirementCandidates
    pilotPairs = $pilot.Count
    pilotPairsWithAutomaticReviewHistory = @(
        $pilot | Where-Object { [long]::Parse($_.SecondaryAutomaticReviewActions) -gt 0 }
    ).Count
    retirementEvidenceSha256 = $evidenceHash
    effectiveStateEvidenceSha256 = $effectiveHash
    privatePilotManifestSha256 = $manifestHash
    applyEnabled = $false
    approvalRequired = $true
    sourceFilesToDelete = 0
    databaseRowsToDelete = 0
} | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath $metadataPath -Encoding utf8

[pscustomobject]@{
    CollisionGroupsRevalidated = $summary.CollisionGroups
    RetirementCandidatesRevalidated = $summary.ReversibleRetirementCandidates
    PilotPairsSelected = $pilot.Count
    PilotPairsWithAutomaticReviewHistory = @(
        $pilot | Where-Object { [long]::Parse($_.SecondaryAutomaticReviewActions) -gt 0 }
    ).Count
    HeldForReview = $summary.HeldForReview
    PilotManifestSha256 = $manifestHash
    AutomaticallyExecuted = 0
    DatabaseRowsDeleted = 0
    OriginalFilesDeleted = 0
    PrivatePilotManifestPath = $manifestPath
    PrivateMetadataPath = $metadataPath
}
