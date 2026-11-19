# Read-only join of case-alias audit and effective-state comparison CSVs.
# No database calls, source reads, hydration, exclusion, or mutation.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$AuditCsv,
    [Parameter(Mandatory = $true)][string]$EffectiveStateCsv,
    [string]$OutputDirectory = (Join-Path (Get-Location).Path 'artifacts\archive-case-effective-state')
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Read-PostgresBoolean {
    param([AllowNull()][string]$Value, [string]$Field)
    switch ($Value.Trim().ToLowerInvariant()) {
        't' { return $true }
        'true' { return $true }
        'f' { return $false }
        'false' { return $false }
        default { throw "Invalid Boolean in '$Field'." }
    }
}
function Parse-Count {
    param([string]$Value, [string]$Field)
    [long]$parsed = 0
    if (-not [long]::TryParse($Value, [ref]$parsed) -or $parsed -lt 0) {
        throw "Invalid nonnegative number in '$Field'."
    }
    return $parsed
}
function Require-Columns {
    param($Rows, [string[]]$Columns, [string]$File)
    if ($Rows.Count -eq 0) { throw "The '$File' CSV is empty." }
    $available = @($Rows[0].PSObject.Properties.Name)
    foreach ($name in $Columns) {
        if ($name -cnotin $available) {
            throw "Missing '$name' column in '$File' CSV."
        }
    }
}

if (-not (Test-Path -LiteralPath $AuditCsv -PathType Leaf) -or
    -not (Test-Path -LiteralPath $EffectiveStateCsv -PathType Leaf)) {
    throw 'Both private audit CSV files must exist.'
}
$audit = @(Import-Csv -LiteralPath $AuditCsv)
$states = @(Import-Csv -LiteralPath $EffectiveStateCsv)

Require-Columns $audit @(
    'source_id', 'normalized_source_key', 'asset_id',
    'manual_history_actions', 'face_occurrences'
) 'history'
Require-Columns $states @(
    'source_id', 'normalized_source_key',
    'first_asset_id', 'second_asset_id',
    'both_verified', 'matching_stored_hash',
    'first_marked_missing', 'second_marked_missing',
    'matching_effective_tags',
    'matching_effective_manual_people', 'matching_effective_place',
    'matching_place_provenance', 'matching_confirmed_face_people',
    'first_effective_tags', 'second_effective_tags',
    'first_effective_manual_people', 'second_effective_manual_people',
    'first_confirmed_face_people', 'second_confirmed_face_people',
    'first_reviewed_faces', 'second_reviewed_faces'
) 'effective-state'

# Use explicit nested maps to avoid collisions between the delimiter and
# private source keys. StringComparer.Ordinal preserves source ID boundaries.
$groupsBySource = [System.Collections.Generic.Dictionary[string,
    System.Collections.Generic.Dictionary[string,object]]]::new(
    [System.StringComparer]::Ordinal)
foreach ($row in $audit) {
    if (-not $groupsBySource.ContainsKey($row.source_id)) {
        $groupsBySource[$row.source_id] =
            [System.Collections.Generic.Dictionary[string,object]]::new(
                [System.StringComparer]::Ordinal)
    }
    $byKey = $groupsBySource[$row.source_id]
    $key = $row.normalized_source_key
    if ([string]::IsNullOrWhiteSpace($key)) { throw 'Blank normalized source key.' }
    if (-not $byKey.ContainsKey($key)) {
        $byKey[$key] = [System.Collections.Generic.List[object]]::new()
    }
    $byKey[$key].Add($row)
}

$expectedPairs = 0
foreach ($byKey in $groupsBySource.Values) {
    foreach ($members in $byKey.Values) {
        if ($members.Count -ne 2) {
            throw 'This effective-state comparison requires exactly two members per collision.'
        }
        if ($members[0].asset_id -ceq $members[1].asset_id) {
            throw 'A pair contains the same asset twice.'
        }
        $expectedPairs++
    }
}
if ($states.Count -ne $expectedPairs) {
    throw "Effective-state comparison has $($states.Count) rows but $expectedPairs pairs were expected."
}

$seen = [System.Collections.Generic.HashSet[string]]::new(
    [System.StringComparer]::Ordinal)
$decisions = @(
    foreach ($state in $states) {
        $source = $state.source_id
        $key = $state.normalized_source_key
        if (-not $groupsBySource.ContainsKey($source) -or
            -not $groupsBySource[$source].ContainsKey($key)) {
            throw 'Effective-state comparison contains an unknown collision group.'
        }
        $members = $groupsBySource[$source][$key]
        $originalIds = @($members | ForEach-Object { $_.asset_id } | Sort-Object)
        $incomingIds = @($state.first_asset_id, $state.second_asset_id | Sort-Object)
        if ($originalIds.Count -ne 2 -or
            -not (($originalIds -join '|') -ceq ($incomingIds -join '|'))) {
            throw 'Effective-state comparison asset IDs do not match the history audit.'
        }
        if (-not $seen.Add($source + '|' + $key)) {
            throw 'Duplicate effective-state collision group.'
        }

        $manualMembers = @(
            $members | Where-Object {
                (Parse-Count $_.manual_history_actions 'manual_history_actions') -gt 0
            }
        ).Count
        $faceOccurrences = (
            $members | ForEach-Object {
                Parse-Count $_.face_occurrences 'face_occurrences'
            } | Measure-Object -Sum
        ).Sum

        $bothVerified = Read-PostgresBoolean $state.both_verified 'both_verified'
        $hashSame = Read-PostgresBoolean $state.matching_stored_hash 'matching_stored_hash'
        $missing = (Read-PostgresBoolean $state.first_marked_missing 'first_marked_missing') -or
            (Read-PostgresBoolean $state.second_marked_missing 'second_marked_missing')
        $tagsSame = Read-PostgresBoolean $state.matching_effective_tags 'matching_effective_tags'
        $peopleSame = Read-PostgresBoolean $state.matching_effective_manual_people 'matching_effective_manual_people'
        $placeSame = Read-PostgresBoolean $state.matching_effective_place 'matching_effective_place'
        $placeSourceSame = Read-PostgresBoolean $state.matching_place_provenance 'matching_place_provenance'
        $facesSame = Read-PostgresBoolean $state.matching_confirmed_face_people 'matching_confirmed_face_people'
        $faceReviewCountsSame = (Parse-Count $state.first_reviewed_faces 'first_reviewed_faces') -eq
            (Parse-Count $state.second_reviewed_faces 'second_reviewed_faces')

        foreach ($column in @(
            'first_effective_tags','second_effective_tags',
            'first_effective_manual_people','second_effective_manual_people',
            'first_confirmed_face_people','second_confirmed_face_people'
        )) {
            $null = Parse-Count $state.$column $column
        }

        $different = -not ($tagsSame -and $peopleSame -and $placeSame -and
            $placeSourceSame -and $facesSame -and $faceReviewCountsSame)

        $decision = if (-not $bothVerified -or -not $hashSame -or $missing) {
            'hold-source-evidence'
        }
        elseif ($manualMembers -eq 2 -and $different) {
            'hold-both-annotated-effective-differences'
        }
        elseif ($manualMembers -eq 2) {
            'hold-both-annotated-history-migration'
        }
        elseif ($different) {
            'hold-effective-differences'
        }
        elseif ($manualMembers -eq 1) {
            'review-single-annotated-keeper-candidate'
        }
        else {
            'review-no-captured-manual-history'
        }

        [pscustomobject]@{
            SourceId = $source
            NormalizedSourceKey = $key
            FirstAssetId = $state.first_asset_id
            SecondAssetId = $state.second_asset_id
            ManualHistoryMembers = $manualMembers
            FaceOccurrences = $faceOccurrences
            BothVerified = $bothVerified
            MatchingStoredHash = $hashSame
            AnyMissing = $missing
            EffectiveTagsMatch = $tagsSame
            EffectiveManualPeopleMatch = $peopleSame
            EffectivePlaceMatch = $placeSame
            PlaceProvenanceMatch = $placeSourceSame
            ConfirmedFacePeopleMatch = $facesSame
            ReviewedFaceCountsMatch = $faceReviewCountsSame
            AnyEffectiveDifference = $different
            Decision = $decision
            SafeToDelete = $false
            RequiresOperatorReview = $true
        }
    }
)

# The report is a private review artifact. No 'Apply' or deletion output exists.
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$reportPath = Join-Path $OutputDirectory 'effective-state-review.csv'
$decisions | Sort-Object SourceId, NormalizedSourceKey |
    Export-Csv -LiteralPath $reportPath -Encoding utf8

[pscustomobject]@{
    CollisionGroups = $decisions.Count
    BothManualHistory = @($decisions | Where-Object { $_.ManualHistoryMembers -eq 2 }).Count
    OneManualHistory = @($decisions | Where-Object { $_.ManualHistoryMembers -eq 1 }).Count
    NoManualHistory = @($decisions | Where-Object { $_.ManualHistoryMembers -eq 0 }).Count
    GroupsWithAnyEffectiveDifference = @($decisions | Where-Object AnyEffectiveDifference).Count
    GroupsWithDifferentTags = @($decisions | Where-Object { -not $_.EffectiveTagsMatch }).Count
    GroupsWithDifferentManualPeople = @($decisions | Where-Object { -not $_.EffectiveManualPeopleMatch }).Count
    GroupsWithDifferentPlace = @($decisions | Where-Object { -not $_.EffectivePlaceMatch }).Count
    GroupsWithDifferentPlaceProvenance = @($decisions | Where-Object { -not $_.PlaceProvenanceMatch }).Count
    GroupsWithDifferentConfirmedFacePeople = @($decisions | Where-Object { -not $_.ConfirmedFacePeopleMatch }).Count
    GroupsWithDifferentReviewedFaceCounts = @($decisions | Where-Object { -not $_.ReviewedFaceCountsMatch }).Count
    BothAnnotatedWithDifferences = @($decisions | Where-Object { $_.ManualHistoryMembers -eq 2 -and $_.AnyEffectiveDifference }).Count
    HoldSourceEvidence = @($decisions | Where-Object { $_.Decision -eq 'hold-source-evidence' }).Count
    AutomaticallyDeletable = 0
    PrivateReviewPath = $reportPath
}
