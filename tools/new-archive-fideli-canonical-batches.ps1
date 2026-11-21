# READ-ONLY. Stage up to four 1..5 pair manifests for the exact
# fideli/ survivor + Fideli/ secondary case-only archive policy.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$RetirementEvidenceCsv,
    [Parameter(Mandatory)][string]$EffectiveStateCsv,
    [ValidateRange(1, 4)][int]$MaxBatches = 4,
    [ValidateRange(1, 5)][int]$PairsPerBatch = 5,
    [switch]$AllEligible,
    [ValidateSet(44, 45, 46)][int]$ExpectedDatabaseSchemaVersion = 45,
    [string]$OutputDirectory = (Join-Path (Get-Location).Path (
        'artifacts\fideli-canonical-batch-session-' +
        [datetime]::UtcNow.ToString('yyyyMMddTHHmmss') + '-' +
        [guid]::NewGuid().ToString('N').Substring(0, 6)))
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $OutputDirectory) {
    throw 'Canonical batch output already exists; never overwrite prior private rollback manifests.'
}

# Compute all decisions from the paired, time-limited audits rather than a
# user-supplied plan. This creates an immutable private policy assessment.
$assessment = & (Join-Path $PSScriptRoot 'new-archive-fideli-canonical-preview.ps1') `
    -RetirementEvidenceCsv $RetirementEvidenceCsv `
    -EffectiveStateCsv $EffectiveStateCsv `
    -OutputDirectory (Join-Path $OutputDirectory 'policy-assessment')
$eligible = @(Import-Csv -LiteralPath $assessment.PrivateAssessmentPath |
    Where-Object Classification -eq 'candidate-lowercase-canonical' |
    Sort-Object SourceId, NormalizedSourceKey)
if ($eligible.Count -ne $assessment.CandidateLowercaseCanonical) {
    throw 'Policy-assessment candidate count differs.'
}
if ($eligible.Count -eq 0) {
    throw 'No unretired eligible Fideli aliases remain.'
}

$evidence = @(Import-Csv -LiteralPath $RetirementEvidenceCsv)
if ($evidence.Count -eq 0 -or
    'automatic_face_review_actions' -cnotin @($evidence[0].PSObject.Properties.Name)) {
    throw 'The canonical batch evidence lacks automatic face-review action counts.'
}
$recordsByAsset = @{}
foreach ($record in $evidence) {
    $identity = "$($record.source_id)|$($record.asset_id)"
    if ($recordsByAsset.ContainsKey($identity)) {
        throw 'Duplicate asset identity in the authoritative audit.'
    }
    $recordsByAsset.Add($identity, $record)
}
# The opt-in all-eligible form produces ONE sealed manifest, not hundreds of pilots.
$batchLimit = if ($AllEligible) { 1 } else { $MaxBatches }
$batchSize = if ($AllEligible) { $eligible.Count } else { $PairsPerBatch }
if ($batchSize -gt 5000) { throw 'Bulk manifest safety limit is 5000 pairs.' }
$selected = @($eligible | Select-Object -First ($batchLimit * $batchSize))
$secondaries = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
$paths = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
$rows = @(
    foreach ($pair in $selected) {
        $source = $pair.SourceId
        $lowerIdentity = "$source|$($pair.CanonicalAssetId)"
        $upperIdentity = "$source|$($pair.UppercaseAssetId)"
        if (-not $recordsByAsset.ContainsKey($lowerIdentity) -or
            -not $recordsByAsset.ContainsKey($upperIdentity)) {
            throw 'Selected source assets are absent from the current audit.'
        }
        $lower = $recordsByAsset[$lowerIdentity]
        $upper = $recordsByAsset[$upperIdentity]
        if ($lower.source_key -cne $pair.CanonicalSourceKey -or
            $upper.source_key -cne $pair.UppercaseSourceKey -or
            -not $lower.source_key.StartsWith('fideli/', [System.StringComparison]::Ordinal) -or
            $upper.source_key -cne ('F' + $lower.source_key.Substring(1)) -or
            $lower.normalized_source_key -cne $pair.NormalizedSourceKey -or
            $upper.normalized_source_key -cne $pair.NormalizedSourceKey) {
            throw 'Canonical source path identity mismatch.'
        }
        if ($lower.verified_content_sha256 -cnotmatch '^[0-9a-f]{64}$' -or
            $lower.verified_content_sha256 -cne $upper.verified_content_sha256 -or
            $lower.verified_size_bytes -cne $upper.verified_size_bytes -or
            [string]::IsNullOrWhiteSpace($lower.verified_revision_id) -or
            [string]::IsNullOrWhiteSpace($upper.verified_revision_id) -or
            $lower.verification_state -cne 'verified' -or
            $upper.verification_state -cne 'verified') {
            throw 'Verified SHA, byte size or revision evidence differs.'
        }
        if ($lower.marked_missing -notin @('f', 'false', 'False') -or
            $upper.marked_missing -notin @('f', 'false', 'False') -or
            $lower.has_source_exclusion -notin @('f', 'false', 'False') -or
            $upper.has_source_exclusion -notin @('f', 'false', 'False') -or
            [long]$pair.UppercaseExplicitActionCount -ne 0) {
            throw 'Inactive, excluded or explicitly protected upper asset was selected.'
        }
        if (-not $secondaries.Add($upperIdentity) -or
            -not $paths.Add("$source|$($pair.NormalizedSourceKey)")) {
            throw 'Overlapping canonical secondary/path across the session.'
        }
        [pscustomobject][ordered]@{
            SourceId = $source
            NormalizedSourceKey = $pair.NormalizedSourceKey
            SurvivorAssetId = $lower.asset_id
            SurvivorSourceKey = $lower.source_key
            SurvivorCreatedAtUtc = $lower.created_at_utc
            SurvivorVerifiedRevisionId = $lower.verified_revision_id
            SecondaryAssetId = $upper.asset_id
            SecondarySourceKey = $upper.source_key
            SecondaryCreatedAtUtc = $upper.created_at_utc
            SecondaryVerifiedRevisionId = $upper.verified_revision_id
            ExpectedSha256 = $lower.verified_content_sha256
            ExpectedSizeBytes = $lower.verified_size_bytes
            SecondaryAutomaticReviewActions = $upper.automatic_face_review_actions
            Action = 'proposed-reversible-retirement'
            ApprovalRequired = 'true'
            ApplyEnabled = 'false'
            DeleteSourceOriginal = 'false'
            DeleteDatabaseRows = 'false'
        }
    }
)

$evidenceHash = (Get-FileHash -LiteralPath $RetirementEvidenceCsv -Algorithm SHA256).Hash.ToLowerInvariant()
$effectiveHash = (Get-FileHash -LiteralPath $EffectiveStateCsv -Algorithm SHA256).Hash.ToLowerInvariant()
$generatedUtc = [datetime]::UtcNow.ToString('O')
$batches = [System.Collections.Generic.List[object]]::new()
for ($offset = 0; $offset -lt $rows.Count; $offset += $batchSize) {
    $name = if ($AllEligible) { 'bulk-01' } else { 'batch-{0:d2}' -f ($batches.Count + 1) }
    $dir = Join-Path $OutputDirectory $name
    New-Item -ItemType Directory -Path $dir | Out-Null
    $members = @($rows | Select-Object -Skip $offset -First $batchSize)
    $manifestPath = Join-Path $dir 'pilot-preview-private.csv'
    $metadataPath = Join-Path $dir 'pilot-preview-metadata.json'
    $members | Export-Csv -LiteralPath $manifestPath -Encoding utf8
    $digest = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
    [ordered]@{
        schemaVersion = 1
        status = 'preview-only'
        retirementPolicy = 'fideli-lowercase-canonical-v1'
        expectedDatabaseSchemaVersion = $ExpectedDatabaseSchemaVersion
        generatedAtUtc = $generatedUtc
        collisionGroupsRevalidated = $assessment.PathCollisionGroupsRevalidated
        retirementCandidatesRevalidated = $eligible.Count
        pilotPairs = $members.Count
        pilotPairsWithAutomaticReviewHistory = @($members |
            Where-Object { [long]$_.SecondaryAutomaticReviewActions -gt 0 }).Count
        retirementEvidenceSha256 = $evidenceHash
        effectiveStateEvidenceSha256 = $effectiveHash
        privatePilotManifestSha256 = $digest
        applyEnabled = $false
        approvalRequired = $true
        sourceFilesToDelete = 0
        databaseRowsToDelete = 0
    } | ConvertTo-Json -Depth 4 |
        Set-Content -LiteralPath $metadataPath -Encoding utf8
    $batches.Add([pscustomobject][ordered]@{
        Batch = $name
        Pairs = $members.Count
        ManifestSha256 = $digest
        PrivateManifestPath = $manifestPath
        PrivateMetadataPath = $metadataPath
    })
}
$sessionPath = Join-Path $OutputDirectory 'canonical-session-private.json'
[ordered]@{
    schemaVersion = 1
    status = 'preview-only'
    retirementPolicy = 'fideli-lowercase-canonical-v1'
    generatedAtUtc = $generatedUtc
    pathCollisionGroupsRevalidated = $assessment.PathCollisionGroupsRevalidated
    exactFideliSpellingPairs = $assessment.ExactFideliSpellingPairs
    eligibleLowercaseCanonical = $eligible.Count
    explicitlyProtectedUppercase = $assessment.RequiresExplicitUpperHistoryReconciliation
    selectedPairs = $selected.Count
    batches = @($batches.ToArray())
    automaticallyApplied = 0
    originalsDeleted = 0
    databaseRowsDeleted = 0
} | ConvertTo-Json -Depth 6 |
    Set-Content -LiteralPath $sessionPath -Encoding utf8
[pscustomobject]@{
    PathCollisionGroupsRevalidated = $assessment.PathCollisionGroupsRevalidated
    CanonicalEligibleUnretired = $eligible.Count
    UppercaseExplicitHistoryHeld = $assessment.RequiresExplicitUpperHistoryReconciliation
    PairsStaged = $rows.Count
    BatchesStaged = $batches.Count
    BatchSizeLimit = $batchSize
    RetirementPolicy = 'fideli-lowercase-canonical-v1'
    AutomaticallyApplied = 0
    AllEligible = [bool]$AllEligible
    OriginalsDeleted = 0
    DatabaseRowsDeleted = 0
    PrivateSessionReportPath = $sessionPath
    Batches = $batches.ToArray()
}
