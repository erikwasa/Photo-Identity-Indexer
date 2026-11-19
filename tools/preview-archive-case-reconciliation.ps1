# Read-only preview of case-only archive source-key collisions.
# Input comes from tools/archive-case-alias-audit.sql, not the exact-duplicate API.
# Never opens/hydrates originals, calls PostgreSQL, or issues exclusion/purge.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$AuditCsv,

    [string]$OutputDirectory = (Join-Path (Get-Location).Path 'artifacts\archive-case-reconciliation')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Read-NonnegativeCount {
    param([string]$Value, [string]$Field)
    [long]$parsed = 0
    if (-not [long]::TryParse($Value, [ref]$parsed) -or $parsed -lt 0) {
        throw "Invalid nonnegative count in '$Field'."
    }
    return $parsed
}

function Normalize-SourceKey {
    param([string]$SourceKey)
    if ([string]::IsNullOrWhiteSpace($SourceKey)) {
        throw 'Audit CSV has a missing source_key.'
    }
    return $SourceKey.Replace('\', '/').ToLowerInvariant()
}

if (-not (Test-Path -LiteralPath $AuditCsv -PathType Leaf)) {
    throw "Audit CSV does not exist: $AuditCsv"
}
$rows = @(Import-Csv -LiteralPath $AuditCsv)
if ($rows.Count -eq 0) {
    throw 'Audit CSV is empty.'
}

$requiredColumns = @(
    'source_id', 'normalized_source_key', 'asset_id', 'source_key',
    'asset_created_at_utc', 'marked_missing', 'verification_state',
    'verified_revision_id', 'verified_content_sha256',
    'tag_actions', 'person_actions', 'manual_place_actions',
    'face_review_actions', 'person_labels', 'face_occurrences',
    'manual_history_actions'
)
$columns = @($rows[0].PSObject.Properties.Name)
foreach ($column in $requiredColumns) {
    if ($column -cnotin $columns) {
        throw "Audit CSV is missing required column '$column'."
    }
}

$evaluated = @(
    foreach ($row in $rows) {
        if ([string]::IsNullOrWhiteSpace($row.source_id) -or
            [string]::IsNullOrWhiteSpace($row.asset_id) -or
            [string]::IsNullOrWhiteSpace($row.normalized_source_key)) {
            throw 'Audit CSV has a missing source or asset identifier.'
        }
        $normalized = Normalize-SourceKey $row.source_key
        if ($normalized -cne $row.normalized_source_key.ToLowerInvariant()) {
            throw 'Audit CSV source_key does not agree with normalized_source_key.'
        }
        $tag = Read-NonnegativeCount $row.tag_actions 'tag_actions'
        $person = Read-NonnegativeCount $row.person_actions 'person_actions'
        $place = Read-NonnegativeCount $row.manual_place_actions 'manual_place_actions'
        $review = Read-NonnegativeCount $row.face_review_actions 'face_review_actions'
        $label = Read-NonnegativeCount $row.person_labels 'person_labels'
        $faces = Read-NonnegativeCount $row.face_occurrences 'face_occurrences'
        $manual = Read-NonnegativeCount $row.manual_history_actions 'manual_history_actions'
        if ($manual -ne ($tag + $person + $place + $review + $label)) {
            throw 'Audit CSV has an inconsistent manual_history_actions total.'
        }
        [bool]$missing = $false
        if (-not [bool]::TryParse($row.marked_missing, [ref]$missing)) {
            throw 'Audit CSV has an invalid marked_missing value.'
        }
        [pscustomobject]@{
            SourceId = $row.source_id
            NormalizedKey = $normalized
            AssetId = $row.asset_id
            SourceKey = $row.source_key
            CreatedAt = $row.asset_created_at_utc
            Missing = $missing
            Verification = $row.verification_state
            RevisionId = $row.verified_revision_id
            Hash = $row.verified_content_sha256
            TagActions = $tag
            PersonActions = $person
            PlaceActions = $place
            ReviewActions = $review
            PersonLabels = $label
            FaceOccurrences = $faces
            ManualHistory = $manual
        }
    }
)

$groups = @($evaluated | Group-Object -Property { "$($_.SourceId)|$($_.NormalizedKey)" })
$preview = @(
    foreach ($group in $groups) {
        $members = @($group.Group | Sort-Object -Property CreatedAt, AssetId)
        $assetIds = @($members.AssetId | Sort-Object -Unique)
        if ($assetIds.Count -ne $members.Count) {
            throw 'Audit CSV repeats an asset within a collision group.'
        }
        $withHistory = @($members | Where-Object { $_.ManualHistory -gt 0 })
        $missingCount = @($members | Where-Object Missing).Count
        $allVerified = $members.Count -eq 2 -and @(
            $members | Where-Object {
                $_.Verification -eq 'verified' -and
                $_.RevisionId -and
                $_.Hash -match '^[0-9a-fA-F]{64}$'
            }
        ).Count -eq 2

        $hashEvidence = if (-not $allVerified) {
            'incomplete-verification'
        } elseif ($members[0].Hash -ceq $members[1].Hash) {
            'same-verified-hash'
        } else {
            'different-verified-hash'
        }

        $conflict = if ($members.Count -ne 2) {
            'hold-multiple-aliases'
        } elseif ($withHistory.Count -ge 2) {
            'hold-both-have-manual-history'
        } elseif ($hashEvidence -eq 'different-verified-hash') {
            'hold-hash-disagreement'
        } elseif ($hashEvidence -ne 'same-verified-hash') {
            'hold-incomplete-verification'
        } elseif ($missingCount -gt 0) {
            'hold-missing-catalogue-record'
        } elseif ($withHistory.Count -eq 1) {
            'review-single-annotated-keeper-candidate'
        } else {
            'review-no-manual-keeper-preference'
        }

        $candidate = if ($members.Count -eq 2 -and $withHistory.Count -eq 1) {
            $withHistory[0].AssetId
        } else {
            ''
        }

        [pscustomobject]@{
            SourceId = $members[0].SourceId
            NormalizedSourceKey = $members[0].NormalizedKey
            MemberCount = $members.Count
            AssetIds = ($members.AssetId -join ';')
            SourceKeys = ($members.SourceKey -join ';')
            ManualHistoryMembers = $withHistory.Count
            ManualHistoryActions = ($members | Measure-Object -Property ManualHistory -Sum).Sum
            FaceOccurrences = ($members | Measure-Object -Property FaceOccurrences -Sum).Sum
            MissingMembers = $missingCount
            HashEvidence = $hashEvidence
            CandidateKeeperAssetId = $candidate
            Decision = $conflict
            SafeToDelete = 'false'
            RequiresOperatorReview = 'true'
        }
    }
)

# The preview never produces a deletion list, even for a single annotated
# keeper candidate. The other asset may carry unmodelled history or derivatives.
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$csvPath = Join-Path $OutputDirectory 'case-alias-reconciliation-preview.csv'
$preview |
    Sort-Object -Property SourceId, NormalizedSourceKey |
    Export-Csv -LiteralPath $csvPath -Encoding utf8

[pscustomobject]@{
    InputRows = $rows.Count
    CollisionGroups = $groups.Count
    GroupsWithBothManualHistories = @($preview | Where-Object { $_.ManualHistoryMembers -ge 2 }).Count
    GroupsWithOneManualHistory = @($preview | Where-Object { $_.ManualHistoryMembers -eq 1 }).Count
    GroupsWithNoManualHistory = @($preview | Where-Object { $_.ManualHistoryMembers -eq 0 }).Count
    SameVerifiedHashPairs = @($preview | Where-Object { $_.HashEvidence -eq 'same-verified-hash' }).Count
    DifferentVerifiedHashPairs = @($preview | Where-Object { $_.HashEvidence -eq 'different-verified-hash' }).Count
    IncompleteVerificationPairs = @($preview | Where-Object { $_.HashEvidence -eq 'incomplete-verification' }).Count
    OperatorReviewRequired = $preview.Count
    AutomaticallyDeletable = 0
    PrivatePreviewPath = $csvPath
}
