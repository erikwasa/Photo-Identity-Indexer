# Read-only source-specific canonicalization evidence: always prefer
# fideli/ (lowercase) over Fideli/ (capitalized), regardless of creation
# order or automatically generated metadata differences.
# This is NOT an Apply manifest and MUST NOT be fed to alias-pilot.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$RetirementEvidenceCsv,
    [Parameter(Mandatory)][string]$EffectiveStateCsv,
    [string]$OutputDirectory = (Join-Path (Get-Location).Path (
        'artifacts\fideli-canonical-preview-' +
        [datetime]::UtcNow.ToString('yyyyMMddTHHmmss') + '-' +
        [guid]::NewGuid().ToString('N').Substring(0, 6)))
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Require-Columns {
    param([object[]]$Rows, [string[]]$Names, [string]$Label)
    if ($Rows.Count -lt 1) { throw "$Label contains no rows." }
    $columns = @($Rows[0].PSObject.Properties.Name)
    foreach ($name in $Names) {
        if ($name -cnotin $columns) { throw "$Label missing '$name'." }
    }
}
function Is-True {
    param([AllowNull()][string]$Value, [string]$Field)
    if ($null -eq $Value) { throw "Missing Boolean $Field." }
    switch ($Value.Trim().ToLowerInvariant()) {
        'true' { return $true }
        't' { return $true }
        'false' { return $false }
        'f' { return $false }
        default { throw "Invalid Boolean $Field." }
    }
}
function Read-Nonnegative {
    param([AllowNull()][string]$Value, [string]$Field)
    [long]$number = 0
    if ($null -eq $Value -or
        -not [long]::TryParse($Value, [ref]$number) -or $number -lt 0) {
        throw "Invalid count $Field."
    }
    return $number
}

foreach ($p in @($RetirementEvidenceCsv, $EffectiveStateCsv)) {
    if (-not (Test-Path -LiteralPath $p -PathType Leaf)) {
        throw 'Both private audit exports are required.'
    }
    $age = [datetime]::UtcNow - (Get-Item -LiteralPath $p).LastWriteTimeUtc
    if ($age.TotalHours -gt 4 -or $age.TotalMinutes -lt -5) {
        throw 'Audit export is stale or future-dated. Re-export both.'
    }
}
$firstTime = (Get-Item -LiteralPath $RetirementEvidenceCsv).LastWriteTimeUtc
$secondTime = (Get-Item -LiteralPath $EffectiveStateCsv).LastWriteTimeUtc
if ([Math]::Abs(($firstTime - $secondTime).TotalMinutes) -gt 15) {
    throw 'Audit exports must be captured within 15 minutes of one another.'
}

$evidence = @(Import-Csv -LiteralPath $RetirementEvidenceCsv)
$effective = @(Import-Csv -LiteralPath $EffectiveStateCsv)
$counts = @(
    'human_or_unknown_face_review_actions','human_or_unknown_person_labels',
    'photo_tag_actions','photo_person_actions','manual_place_actions',
    'manual_capture_date_actions','presentation_preference_actions',
    'manual_list_collection_items','featured_face_references'
)
Require-Columns $evidence (@(
    'source_id','asset_id','normalized_source_key','source_key',
    'created_at_utc','marked_missing','verification_state',
    'verified_revision_id','verified_content_sha256','verified_size_bytes',
    'has_source_exclusion'
) + $counts) 'Retirement evidence'
$comparison = @(
    'matching_effective_tags','matching_effective_manual_people',
    'matching_effective_place','matching_place_provenance',
    'matching_confirmed_face_people'
)
Require-Columns $effective (@(
    'source_id','normalized_source_key','first_asset_id','second_asset_id',
    'both_verified','matching_stored_hash'
) + $comparison) 'Effective annotations'

$sourceGroups = @($evidence | Group-Object {
    "$($_.source_id)|$($_.normalized_source_key)"
})
$states = @{}
foreach ($state in $effective) {
    $key = "$($state.source_id)|$($state.normalized_source_key)"
    if ($states.ContainsKey($key)) {
        throw 'Duplicate effective-state group key.'
    }
    $states.Add($key, $state)
}
if ($sourceGroups.Count -ne $effective.Count) {
    throw 'Retirement and effective-state group totals differ.'
}

$preview = @(
    foreach ($group in $sourceGroups) {
        if ($group.Count -ne 2) {
            throw 'Case-only evidence contains a group other than two.'
        }
        if (-not $states.ContainsKey($group.Name)) {
            throw 'Effective-state evidence missing a case group.'
        }
        $state = $states[$group.Name]
        $members = @($group.Group)
        $matchedIds = @($members | ForEach-Object { $_.asset_id } | Sort-Object)
        $stateIds = @($state.first_asset_id, $state.second_asset_id | Sort-Object)
        if (($matchedIds -join '|') -cne ($stateIds -join '|')) {
            throw 'Effective-state IDs differ from source evidence.'
        }
        $lower = @($members | Where-Object {
            $_.source_key.StartsWith('fideli/', [StringComparison]::Ordinal)
        })
        $capital = @($members | Where-Object {
            $_.source_key.StartsWith('Fideli/', [StringComparison]::Ordinal)
        })
        if ($lower.Count -ne 1 -or $capital.Count -ne 1) { continue }
        $survivor = $lower[0]
        $secondary = $capital[0]
        # Only the FIRST LETTER of the folder component may differ.
        if (('F' + $survivor.source_key.Substring(1)) -cne $secondary.source_key) {
            continue
        }
        if ($survivor.asset_id -ceq $secondary.asset_id) {
            throw 'Same asset ID appeared twice in the Fideli pair.'
        }

        $anyMissing = (Is-True $survivor.marked_missing 'survivor missing') -or
            (Is-True $secondary.marked_missing 'secondary missing')
        $anyExcluded = (Is-True $survivor.has_source_exclusion 'survivor excluded') -or
            (Is-True $secondary.has_source_exclusion 'secondary excluded')
        $verified = (Is-True $state.both_verified 'both_verified') -and
            $survivor.verification_state -ceq 'verified' -and
            $secondary.verification_state -ceq 'verified' -and
            -not [string]::IsNullOrWhiteSpace($survivor.verified_revision_id) -and
            -not [string]::IsNullOrWhiteSpace($secondary.verified_revision_id)
        $equalHash = (Is-True $state.matching_stored_hash 'matching_stored_hash') -and
            $survivor.verified_content_sha256 -cmatch '^[0-9a-f]{64}$' -and
            $survivor.verified_content_sha256 -ceq $secondary.verified_content_sha256
        [long]$firstBytes = 0
        [long]$secondBytes = 0
        $sameSize = [long]::TryParse($survivor.verified_size_bytes, [ref]$firstBytes) -and
            [long]::TryParse($secondary.verified_size_bytes, [ref]$secondBytes) -and
            $firstBytes -eq $secondBytes -and $firstBytes -ge 0

        [long]$upperExplicitActions = 0
        foreach ($field in $counts) {
            $upperExplicitActions += Read-Nonnegative $secondary.$field $field
        }
        $stateDifferences = @($comparison | Where-Object {
            -not (Is-True $state.$_ $_)
        })
        $lowerIsOlder = [datetimeoffset]::Parse(
            $survivor.created_at_utc,
            [System.Globalization.CultureInfo]::InvariantCulture
        ) -lt [datetimeoffset]::Parse(
            $secondary.created_at_utc,
            [System.Globalization.CultureInfo]::InvariantCulture
        )

        $classification = if ($anyMissing) {
            'already-inactive-or-missing'
        } elseif ($anyExcluded) {
            'blocked-source-exclusion'
        } elseif (-not $verified -or -not $equalHash -or -not $sameSize) {
            'blocked-content-proof'
        } elseif ($upperExplicitActions -gt 0) {
            'needs-upper-explicit-history-reconciliation'
        } else {
            'candidate-lowercase-canonical'
        }

        [pscustomobject][ordered]@{
            SourceId = $survivor.source_id
            NormalizedSourceKey = $state.normalized_source_key
            CanonicalAssetId = $survivor.asset_id
            CanonicalSourceKey = $survivor.source_key
            CanonicalVerifiedRevisionId = $survivor.verified_revision_id
            UppercaseAssetId = $secondary.asset_id
            UppercaseSourceKey = $secondary.source_key
            UppercaseVerifiedRevisionId = $secondary.verified_revision_id
            MatchingVerifiedSha256 = $equalHash
            MatchingVerifiedByteSize = $sameSize
            UppercaseExplicitActionCount = $upperExplicitActions
            EffectiveAnnotationDifferences = $stateDifferences -join ';'
            LowercaseIsOlder = $lowerIsOlder
            Classification = $classification
            ExistingFivePairRunnerCompatible = (
                $classification -ceq 'candidate-lowercase-canonical' -and
                $lowerIsOlder -and $stateDifferences.Count -eq 0
            )
            AutomaticWriteApproved = $false
        }
    }
)
# The private CSV is an assessment only, not the existing CLI's manifest format.
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $OutputDirectory) {
    throw 'Use a fresh output directory; do not overwrite old private proofs.'
}
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$outPath = Join-Path $OutputDirectory 'fideli-canonical-assessment-private.csv'
$preview | Export-Csv -LiteralPath $outPath -Encoding utf8
$classes = @($preview | Group-Object Classification |
    Sort-Object Name | ForEach-Object {
        [pscustomobject]@{ Classification = $_.Name; Groups = $_.Count }
    })
$eligible = @($preview | Where-Object Classification -eq 'candidate-lowercase-canonical')
$differences = @($eligible | Where-Object {
    -not [string]::IsNullOrWhiteSpace($_.EffectiveAnnotationDifferences)
})
[pscustomobject]@{
    PathCollisionGroupsRevalidated = $sourceGroups.Count
    ExactFideliSpellingPairs = $preview.Count
    CandidateLowercaseCanonical = $eligible.Count
    OfCandidatesWithEffectiveMetadataDifferences = $differences.Count
    OfCandidatesNotCompatibleWithCurrentRunner = @($eligible | Where-Object {
        -not $_.ExistingFivePairRunnerCompatible
    }).Count
    RequiresExplicitUpperHistoryReconciliation = @($preview | Where-Object {
        $_.Classification -eq 'needs-upper-explicit-history-reconciliation'
    }).Count
    BlockedContentProof = @($preview | Where-Object Classification -eq 'blocked-content-proof').Count
    ExcludedOrInactive = @($preview | Where-Object {
        $_.Classification -in @('already-inactive-or-missing', 'blocked-source-exclusion')
    }).Count
    Classifications = $classes
    PrivateAssessmentPath = $outPath
    AutomaticallyApplied = 0
    DatabaseRowsDeleted = 0
    OriginalFilesDeleted = 0
}
