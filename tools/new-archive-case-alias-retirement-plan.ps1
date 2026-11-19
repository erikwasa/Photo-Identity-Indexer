# Offline, read-only generator of reversible-retirement CANDIDATES.
# No database, cloud, filesystem originals, exclusion, purge, or apply operations.
# NEVER use the private CSV as a deletion command or a production approval.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$EvidenceCsv,
    [Parameter(Mandatory = $true)][string]$EffectiveStateCsv,
    [string]$OutputDirectory = (Join-Path (Get-Location).Path 'artifacts\archive-case-retirement')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Require-Columns {
    param([object[]]$Rows, [string[]]$Columns, [string]$Label)
    if ($Rows.Count -lt 1) { throw "$Label CSV contains no rows." }
    $actual = @($Rows[0].PSObject.Properties.Name)
    foreach ($name in $Columns) {
        if ($name -cnotin $actual) { throw "$Label CSV is missing column '$name'." }
    }
}

function Read-Bool {
    param([AllowNull()][string]$Value, [string]$Field)
    if ($null -eq $Value) { throw "Missing '$Field' Boolean." }
    switch ($Value.Trim().ToLowerInvariant()) {
        't' { return $true }
        'true' { return $true }
        'f' { return $false }
        'false' { return $false }
        default { throw "Invalid '$Field' Boolean." }
    }
}

function Read-Count {
    param([string]$Value, [string]$Field)
    [long]$valueParsed = 0
    if (-not [long]::TryParse($Value, [ref]$valueParsed) -or $valueParsed -lt 0) {
        throw "Invalid nonnegative '$Field' count."
    }
    return $valueParsed
}

function Read-When {
    param([string]$Value)
    [datetimeoffset]$parsed = [datetimeoffset]::MinValue
    if (-not [datetimeoffset]::TryParse(
        $Value, [System.Globalization.CultureInfo]::InvariantCulture,
        [System.Globalization.DateTimeStyles]::AssumeUniversal,
        [ref]$parsed)) {
        throw 'Invalid asset creation timestamp.'
    }
    return $parsed.ToUniversalTime()
}

function Normalized-Key {
    param([string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value)) { throw 'Missing source key.' }
    return $Value.Replace('\', '/').ToLowerInvariant()
}

if (-not (Test-Path -LiteralPath $EvidenceCsv -PathType Leaf) -or
    -not (Test-Path -LiteralPath $EffectiveStateCsv -PathType Leaf)) {
    throw 'Both private evidence CSV files are required.'
}
$evidence = @(Import-Csv -LiteralPath $EvidenceCsv)
$effective = @(Import-Csv -LiteralPath $EffectiveStateCsv)

$counts = @(
    'revision_count','face_occurrence_count',
    'automatic_face_review_actions','human_or_unknown_face_review_actions',
    'automatic_person_labels','human_or_unknown_person_labels',
    'photo_tag_actions','photo_person_actions','manual_place_actions',
    'automatic_or_migrated_place_actions','manual_capture_date_actions',
    'presentation_preference_actions','manual_list_collection_items',
    'featured_face_references'
)
Require-Columns $evidence (@(
    'source_id','normalized_source_key','asset_id','source_key',
    'created_at_utc','marked_missing','verification_state',
    'verified_revision_id','verified_content_sha256','verified_size_bytes',
    'has_source_exclusion'
) + $counts) 'Retirement evidence'

$comparisonFields = @(
    'matching_effective_tags','matching_effective_manual_people',
    'matching_effective_place','matching_place_provenance',
    'matching_confirmed_face_people'
)
Require-Columns $effective (@(
    'source_id','normalized_source_key','first_asset_id','second_asset_id',
    'both_verified','matching_stored_hash'
) + $comparisonFields) 'Effective-state'

$rowsByKey = @{}
foreach ($row in $evidence) {
    if ([string]::IsNullOrWhiteSpace($row.source_id) -or
        [string]::IsNullOrWhiteSpace($row.asset_id)) {
        throw 'Missing source or asset identity in retirement evidence.'
    }
    $normalized = Normalized-Key $row.source_key
    if ($normalized -cne $row.normalized_source_key.ToLowerInvariant()) {
        throw 'Retirement evidence has an inconsistent normalized source key.'
    }
    foreach ($field in $counts) {
        $null = Read-Count $row.$field $field
    }
    $null = Read-Bool $row.marked_missing 'marked_missing'
    $null = Read-Bool $row.has_source_exclusion 'has_source_exclusion'
    $null = Read-When $row.created_at_utc

    $groupId = "$($row.source_id)|$normalized"
    if (-not $rowsByKey.ContainsKey($groupId)) {
        $rowsByKey[$groupId] = [System.Collections.Generic.List[object]]::new()
    }
    $rowsByKey[$groupId].Add($row)
}
if ($rowsByKey.Count -ne $effective.Count) {
    throw "Evidence contains $($rowsByKey.Count) groups but effective-state contains $($effective.Count)."
}

$usedGroups = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
$plan = @(
    foreach ($state in $effective) {
        $groupId = "$($state.source_id)|$($state.normalized_source_key)"
        if (-not $rowsByKey.ContainsKey($groupId) -or -not $usedGroups.Add($groupId)) {
            throw 'Effective-state export has an unknown or duplicate case-collision group.'
        }
        $members = @($rowsByKey[$groupId].ToArray())
        if ($members.Count -ne 2 -or $members[0].asset_id -ceq $members[1].asset_id) {
            throw 'Automation requires exactly two distinct assets per case-only path.'
        }
        if (@($members | Where-Object { $_.source_key -ceq $members[0].source_key }).Count -ne 1) {
            throw 'The pair does not have two distinct source-key spellings.'
        }
        $originalIds = @($members | ForEach-Object { $_.asset_id } | Sort-Object)
        $comparedIds = @(@($state.first_asset_id, $state.second_asset_id) | Sort-Object)
        if (($originalIds -join '|') -cne ($comparedIds -join '|')) {
            throw 'Effective-state asset IDs do not agree with retirement evidence.'
        }

        $ordered = @($members | Sort-Object -Property @(
            @{ Expression = { Read-When $_.created_at_utc }; Ascending = $true },
            @{ Expression = 'asset_id'; Ascending = $true }
        ))
        $older = $ordered[0]
        $later = $ordered[1]
        $timestampTie = (Read-When $older.created_at_utc) -eq (Read-When $later.created_at_utc)
        $laterProtectedFields = @(
            'human_or_unknown_face_review_actions',
            'human_or_unknown_person_labels',
            'photo_tag_actions',
            'photo_person_actions',
            'manual_place_actions',
            'manual_capture_date_actions',
            'presentation_preference_actions',
            'manual_list_collection_items',
            'featured_face_references'
        )
        [long]$laterProtected = 0
        foreach ($field in $laterProtectedFields) {
            $laterProtected += Read-Count $later.$field $field
        }
        $autoReviewsLater = Read-Count $later.automatic_face_review_actions 'automatic_face_review_actions'
        $autoLabelsLater = Read-Count $later.automatic_person_labels 'automatic_person_labels'

        $bothVerified = (Read-Bool $state.both_verified 'both_verified') -and
            $members[0].verification_state -eq 'verified' -and
            $members[1].verification_state -eq 'verified'
        $hashMatched = (Read-Bool $state.matching_stored_hash 'matching_stored_hash') -and
            $older.verified_content_sha256 -cmatch '^[0-9a-f]{64}$' -and
            $older.verified_content_sha256 -ceq $later.verified_content_sha256 -and
            $older.verified_revision_id -and $later.verified_revision_id

        $olderSize = if ([string]::IsNullOrWhiteSpace($older.verified_size_bytes)) {
            $null
        } else {
            Read-Count $older.verified_size_bytes 'verified_size_bytes'
        }
        $laterSize = if ([string]::IsNullOrWhiteSpace($later.verified_size_bytes)) {
            $null
        } else {
            Read-Count $later.verified_size_bytes 'verified_size_bytes'
        }
        # Unverified observations legitimately have no verified size. That
        # blocks retirement rather than making the entire audit unusable.
        $sameSize = $null -ne $olderSize -and $null -ne $laterSize -and
            $olderSize -eq $laterSize
        $anyMissing = (Read-Bool $older.marked_missing 'marked_missing') -or
            (Read-Bool $later.marked_missing 'marked_missing')
        $anyExcluded = (Read-Bool $older.has_source_exclusion 'has_source_exclusion') -or
            (Read-Bool $later.has_source_exclusion 'has_source_exclusion')

        $effectiveMatch = $true
        foreach ($field in $comparisonFields) {
            if (-not (Read-Bool $state.$field $field)) {
                $effectiveMatch = $false
            }
        }

        $blocks = [System.Collections.Generic.List[string]]::new()
        if ($timestampTie) { $blocks.Add('ambiguous-ingestion-order') }
        if (-not $bothVerified -or -not $hashMatched -or -not $sameSize) {
            $blocks.Add('content-evidence-incomplete-or-different')
        }
        if ($anyMissing) { $blocks.Add('catalogue-missing-state') }
        if ($anyExcluded) { $blocks.Add('source-exclusion-present') }
        if ($laterProtected -gt 0) { $blocks.Add('later-asset-has-protected-history-or-references') }
        if (-not $effectiveMatch) { $blocks.Add('effective-annotation-difference') }

        $eligible = $blocks.Count -eq 0
        [pscustomobject]@{
            SourceId = $older.source_id
            NormalizedSourceKey = $state.normalized_source_key
            SurvivorAssetId = $older.asset_id
            SurvivorSourceKey = $older.source_key
            SurvivorCreatedAtUtc = $older.created_at_utc
            SurvivorVerifiedRevisionId = $older.verified_revision_id
            SecondaryAssetId = $later.asset_id
            SecondarySourceKey = $later.source_key
            SecondaryCreatedAtUtc = $later.created_at_utc
            SecondaryVerifiedRevisionId = $later.verified_revision_id
            VerifiedContentSha256 = $older.verified_content_sha256
            VerifiedSizeBytes = $olderSize
            SecondaryAutomaticReviewActions = $autoReviewsLater
            SecondaryAutomaticPersonLabels = $autoLabelsLater
            SecondaryProtectedReferences = $laterProtected
            CurrentEffectiveAnnotationsMatch = $effectiveMatch
            ProposedAction = if ($eligible) { 'review-for-reversible-alias-retirement' } else { 'hold' }
            Blockers = if ($eligible) { '' } else { $blocks -join ';' }
            OriginalFilesToDelete = 0
            AssetRowsToDelete = 0
            ApplyEnabled = $false
            ApprovalRequired = $true
        }
    }
)

# This is a candidate manifest, not executable SQL, a deletion list, or an
# application of an archive-retirement mapping.
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$path = Join-Path $OutputDirectory 'retirement-candidates-private.csv'
$plan | Sort-Object SourceId, NormalizedSourceKey | Export-Csv -LiteralPath $path -Encoding utf8

[pscustomobject]@{
    CollisionGroups = $plan.Count
    ReversibleRetirementCandidates = @($plan | Where-Object { $_.ProposedAction -eq 'review-for-reversible-alias-retirement' }).Count
    HeldForReview = @($plan | Where-Object { $_.ProposedAction -eq 'hold' }).Count
    HeldForProtectedSecondaryHistory = @($plan | Where-Object { $_.Blockers -like '*later-asset-has-protected-history-or-references*' }).Count
    HeldForEffectiveAnnotationDifferences = @($plan | Where-Object { $_.Blockers -like '*effective-annotation-difference*' }).Count
    HeldForAmbiguousIngestionOrder = @($plan | Where-Object { $_.Blockers -like '*ambiguous-ingestion-order*' }).Count
    SecondaryAutomaticReviewActionsInCandidates = (
        $plan | Where-Object { $_.ProposedAction -eq 'review-for-reversible-alias-retirement' } |
        Measure-Object SecondaryAutomaticReviewActions -Sum
    ).Sum
    AutomaticallyExecuted = 0
    OriginalFilesDeleted = 0
    AssetRowsDeleted = 0
    PrivatePlanPath = $path
}
