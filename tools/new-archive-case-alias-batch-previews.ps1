# Read-only, no originals and no database writes. Generate independent
# five-pair operator preview manifests from one FRESH authoritative audit.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$RetirementEvidenceCsv,
    [Parameter(Mandatory)][string]$EffectiveStateCsv,
    [ValidateRange(1, 4)][int]$MaxBatches = 4,
    [ValidateRange(1, 5)][int]$PairsPerBatch = 5,
    [ValidateRange(1, 24)][int]$MaximumEvidenceAgeHours = 4,
    [ValidateSet(44, 45)][int]$ExpectedDatabaseSchemaVersion = 45,
    [string]$OutputDirectory = (Join-Path (Get-Location).Path (
        'artifacts\archive-case-batch-session-' + [datetime]::UtcNow.ToString('yyyyMMddTHHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 6)))
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Require-FreshAudit {
    param([string]$Path, [string]$Label)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "The $Label evidence CSV does not exist."
    }
    $file = Get-Item -LiteralPath $Path
    if ($file.Length -eq 0) { throw "The $Label evidence CSV is empty." }
    $age = [datetime]::UtcNow - $file.LastWriteTimeUtc
    if ($age.TotalHours -gt $MaximumEvidenceAgeHours -or $age.TotalMinutes -lt -5) {
        throw "The $Label evidence CSV is stale or future-dated; re-export both audits."
    }
    return $file
}

function Read-Flag {
    param([string]$Value, [string]$Name)
    switch -CaseSensitive ($Value) {
        'True'  { return $true }
        'true'  { return $true }
        'False' { return $false }
        'false' { return $false }
        default { throw "Candidate has an invalid $Name safety flag." }
    }
}

$evidenceFile = Require-FreshAudit $RetirementEvidenceCsv 'retirement'
$stateFile = Require-FreshAudit $EffectiveStateCsv 'effective-state'
if ($evidenceFile.FullName -ceq $stateFile.FullName) {
    throw 'The two private audit paths must be distinct.'
}
if ([math]::Abs(($evidenceFile.LastWriteTimeUtc - $stateFile.LastWriteTimeUtc).TotalMinutes) -gt 15) {
    throw 'The two private audit CSVs were not captured within 15 minutes.'
}

$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $OutputDirectory) {
    throw 'Batch output directory exists. Use a NEW private directory; do not overwrite rollback manifests.'
}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

# Recompute every decision; never accept an old, manually curated candidate CSV.
$planner = Join-Path $PSScriptRoot 'new-archive-case-alias-retirement-plan.ps1'
$summary = & $planner -EvidenceCsv $evidenceFile.FullName -EffectiveStateCsv $stateFile.FullName -OutputDirectory (Join-Path $OutputDirectory 'revalidated-input')
$rows = @(Import-Csv -LiteralPath $summary.PrivatePlanPath)
if ($rows.Count -ne $summary.CollisionGroups) {
    throw 'Revalidated collision count mismatch; no batch may be approved.'
}
$eligible = @($rows | Where-Object {
    $_.ProposedAction -ceq 'review-for-reversible-alias-retirement'
})
if ($eligible.Count -ne $summary.ReversibleRetirementCandidates) {
    throw 'Revalidated eligible count mismatch; no batch may be approved.'
}
foreach ($candidate in $eligible) {
    if ((Read-Flag $candidate.ApplyEnabled 'ApplyEnabled') -or
        -not (Read-Flag $candidate.ApprovalRequired 'ApprovalRequired') -or
        $candidate.OriginalFilesToDelete -cne '0' -or
        $candidate.AssetRowsToDelete -cne '0' -or
        -not (Read-Flag $candidate.CurrentEffectiveAnnotationsMatch 'CurrentEffectiveAnnotationsMatch') -or
        $candidate.SecondaryProtectedReferences -cne '0' -or
        -not [string]::IsNullOrEmpty($candidate.Blockers) -or
        [string]::IsNullOrWhiteSpace($candidate.SourceId) -or
        [string]::IsNullOrWhiteSpace($candidate.SecondaryAssetId) -or
        [string]::IsNullOrWhiteSpace($candidate.NormalizedSourceKey)) {
        throw 'An eligible candidate violated non-destructive or protected-history constraints.'
    }
}
if ($eligible.Count -eq 0) {
    throw 'No eligible unretired aliases remain. No batch manifests generated.'
}

# Prefer secondaries without automatic face history, but never discard a
# transactionally eligible candidate merely because it has auto history.
$ordered = @($eligible | Sort-Object -Property @(
    @{ Expression = { [long]$_.SecondaryAutomaticReviewActions -gt 0 }; Ascending = $true },
    'SourceId', 'NormalizedSourceKey', 'SecondaryAssetId'
))
$toStage = [Math]::Min($ordered.Count, $MaxBatches * $PairsPerBatch)
$selected = @($ordered | Select-Object -First $toStage)
$secondaries = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$paths = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($candidate in $selected) {
    if (-not $secondaries.Add([string]$candidate.SecondaryAssetId) -or
        -not $paths.Add("$($candidate.SourceId)|$($candidate.NormalizedSourceKey)")) {
        throw 'Overlapping case-alias sources or secondaries found across batches.'
    }
}

$generatedUtc = [datetime]::UtcNow.ToString('O')
$evidenceHash = (Get-FileHash -LiteralPath $evidenceFile.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
$stateHash = (Get-FileHash -LiteralPath $stateFile.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
$batchReports = [System.Collections.Generic.List[object]]::new()
$batchCount = [int][Math]::Ceiling($selected.Count / [double]$PairsPerBatch)

for ($index = 0; $index -lt $batchCount; $index++) {
    $batchName = 'batch-{0:d2}' -f ($index + 1)
    $batchRoot = Join-Path $OutputDirectory $batchName
    New-Item -ItemType Directory -Path $batchRoot | Out-Null

    $start = $index * $PairsPerBatch
    $batchCandidates = @($selected | Select-Object -Skip $start -First $PairsPerBatch)
    $manifestRows = @($batchCandidates | ForEach-Object {
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
    if ($manifestRows.Count -lt 1 -or $manifestRows.Count -gt $PairsPerBatch) {
        throw 'Invalid batch partition; no further manifests generated.'
    }

    $manifestPath = Join-Path $batchRoot 'pilot-preview-private.csv'
    $metadataPath = Join-Path $batchRoot 'pilot-preview-metadata.json'
    $manifestRows | Export-Csv -LiteralPath $manifestPath -Encoding utf8
    $digest = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $withAuto = @($batchCandidates | Where-Object {
        [long]$_.SecondaryAutomaticReviewActions -gt 0
    }).Count
    [ordered]@{
        schemaVersion = 1
        status = 'preview-only'
        expectedDatabaseSchemaVersion = $ExpectedDatabaseSchemaVersion
        generatedAtUtc = $generatedUtc
        collisionGroupsRevalidated = $summary.CollisionGroups
        retirementCandidatesRevalidated = $summary.ReversibleRetirementCandidates
        pilotPairs = $manifestRows.Count
        pilotPairsWithAutomaticReviewHistory = $withAuto
        retirementEvidenceSha256 = $evidenceHash
        effectiveStateEvidenceSha256 = $stateHash
        privatePilotManifestSha256 = $digest
        applyEnabled = $false
        approvalRequired = $true
        sourceFilesToDelete = 0
        databaseRowsToDelete = 0
    } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $metadataPath -Encoding utf8

    $batchReports.Add([pscustomobject][ordered]@{
        Batch = $batchName
        Pairs = $manifestRows.Count
        WithAutomaticReviewHistory = $withAuto
        ManifestSha256 = $digest
        PrivateManifestPath = $manifestPath
        PrivateMetadataPath = $metadataPath
    })
}

$sessionPath = Join-Path $OutputDirectory 'batch-session-private.json'
[ordered]@{
    schemaVersion = 1
    status = 'preview-only'
    generatedAtUtc = $generatedUtc
    expectedDatabaseSchemaVersion = $ExpectedDatabaseSchemaVersion
    revalidatedCollisionGroups = $summary.CollisionGroups
    eligibleUnretiredCandidates = $summary.ReversibleRetirementCandidates
    heldForReview = $summary.HeldForReview
    selectedPairs = $selected.Count
    batches = @($batchReports.ToArray())
    automaticallyExecuted = 0
    databaseRowsDeleted = 0
    originalFilesDeleted = 0
} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $sessionPath -Encoding utf8

[pscustomobject]@{
    CollisionGroupsRevalidated = $summary.CollisionGroups
    EligibleUnretiredCandidates = $summary.ReversibleRetirementCandidates
    HeldForReview = $summary.HeldForReview
    PairsStaged = $selected.Count
    BatchesStaged = $batchReports.Count
    BatchSizeLimit = $PairsPerBatch
    BatchesWithAutomaticReviewHistory = @($batchReports | Where-Object {
        $_.WithAutomaticReviewHistory -gt 0
    }).Count
    AutomaticallyExecuted = 0
    DatabaseRowsDeleted = 0
    OriginalFilesDeleted = 0
    PrivateSessionReportPath = $sessionPath
    Batches = $batchReports.ToArray()
}
