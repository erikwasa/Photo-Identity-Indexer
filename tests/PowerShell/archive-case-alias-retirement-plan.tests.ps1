# Source-free safety tests for the automated alias-retirement CANDIDATE planner.
# No PostgreSQL, Podman, source originals, or data-changing operations.
[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$planner = Join-Path $repo 'tools\new-archive-case-alias-retirement-plan.ps1'
$root = Join-Path ([IO.Path]::GetTempPath()) ('photoidentity-alias-retirement-' + [guid]::NewGuid().ToString('N'))
$evidenceCsv = Join-Path $root 'evidence.csv'
$effectiveCsv = Join-Path $root 'effective.csv'
$outputDirectory = Join-Path $root 'output'
$hashA = 'a' * 64
$hashB = 'b' * 64

function Assert([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function New-Evidence {
    param(
        [string]$Name,
        [string]$Asset,
        [bool]$IsNewer,
        [long]$ManualTagActions = 0,
        [long]$HumanFaceReviewActions = 0,
        [long]$AutomaticFaceReviewActions = 0,
        [bool]$Excluded = $false,
        [string]$Hash = $hashA,
        [string]$Timestamp = ''
    )
    $when = if ($Timestamp) { $Timestamp } elseif ($IsNewer) {
        '2026-11-20 09:00:00+00'
    } else { '2026-11-19 09:00:00+00' }

    [pscustomobject]@{
        source_id = 'test-source'
        normalized_source_key = $Name.ToLowerInvariant()
        asset_id = $Asset
        source_key = $Name
        created_at_utc = $when
        marked_missing = 'f'
        verification_state = 'verified'
        verified_revision_id = "$Asset-revision"
        verified_content_sha256 = $Hash
        verified_size_bytes = '12345'
        revision_count = '1'
        face_occurrence_count = '1'
        automatic_face_review_actions = "$AutomaticFaceReviewActions"
        human_or_unknown_face_review_actions = "$HumanFaceReviewActions"
        automatic_person_labels = '0'
        human_or_unknown_person_labels = '0'
        photo_tag_actions = "$ManualTagActions"
        photo_person_actions = '0'
        manual_place_actions = '0'
        automatic_or_migrated_place_actions = '0'
        manual_capture_date_actions = '0'
        presentation_preference_actions = '0'
        manual_list_collection_items = '0'
        featured_face_references = '0'
        has_source_exclusion = if ($Excluded) { 't' } else { 'f' }
    }
}

function New-Effective {
    param([string]$Name, [string]$Older, [string]$Newer, [bool]$PlaceMatch = $true)
    [pscustomobject]@{
        source_id = 'test-source'
        normalized_source_key = $Name.ToLowerInvariant()
        first_asset_id = $Older
        second_asset_id = $Newer
        both_verified = 't'
        matching_stored_hash = 't'
        matching_effective_tags = 't'
        matching_effective_manual_people = 't'
        matching_effective_place = if ($PlaceMatch) { 't' } else { 'f' }
        matching_place_provenance = 't'
        matching_confirmed_face_people = 't'
    }
}

try {
    New-Item -ItemType Directory -LiteralPath $root -Force | Out-Null

    # Keep the original identified copy; automatic face decisions on a later
    # duplicate do not count as human history. No metadata or bytes are deleted.
    $rows = @(
        (New-Evidence 'Fideli/auto.jpg' 'older-auto' $false -ManualTagActions 1)
        (New-Evidence 'fideli/auto.jpg' 'newer-auto' $true -AutomaticFaceReviewActions 3)
        (New-Evidence 'Fideli/human.jpg' 'older-human' $false)
        (New-Evidence 'fideli/human.jpg' 'newer-human' $true -HumanFaceReviewActions 1)
        (New-Evidence 'Fideli/place.jpg' 'older-place' $false)
        (New-Evidence 'fideli/place.jpg' 'newer-place' $true)
        (New-Evidence 'Fideli/tie.jpg' 'older-tie' $false -Timestamp '2026-11-20 09:00:00+00')
        (New-Evidence 'fideli/tie.jpg' 'newer-tie' $true)
        (New-Evidence 'Fideli/hash.jpg' 'older-hash' $false)
        (New-Evidence 'fideli/hash.jpg' 'newer-hash' $true -Hash $hashB)
        (New-Evidence 'Fideli/excluded.jpg' 'older-excluded' $false)
        (New-Evidence 'fideli/excluded.jpg' 'newer-excluded' $true -Excluded $true)
        (New-Evidence 'Fideli/tagged.jpg' 'older-tagged' $false)
        (New-Evidence 'fideli/tagged.jpg' 'newer-tagged' $true -ManualTagActions 1)
    )
    $states = @(
        (New-Effective 'Fideli/auto.jpg' 'older-auto' 'newer-auto')
        (New-Effective 'Fideli/human.jpg' 'older-human' 'newer-human')
        (New-Effective 'Fideli/place.jpg' 'older-place' 'newer-place' -PlaceMatch $false)
        (New-Effective 'Fideli/tie.jpg' 'older-tie' 'newer-tie')
        (New-Effective 'Fideli/hash.jpg' 'older-hash' 'newer-hash')
        (New-Effective 'Fideli/excluded.jpg' 'older-excluded' 'newer-excluded')
        (New-Effective 'Fideli/tagged.jpg' 'older-tagged' 'newer-tagged')
    )
    $rows | Export-Csv -LiteralPath $evidenceCsv -Encoding utf8
    $states | Export-Csv -LiteralPath $effectiveCsv -Encoding utf8
    $summary = & $planner -EvidenceCsv $evidenceCsv -EffectiveStateCsv $effectiveCsv -OutputDirectory $outputDirectory
    Assert ($summary.CollisionGroups -eq 7) 'Wrong collision count.'
    Assert ($summary.ReversibleRetirementCandidates -eq 1) 'Unexpected automated candidate count.'
    Assert ($summary.HeldForReview -eq 6) 'Unreviewed cases were not held.'
    Assert ($summary.HeldForEffectiveAnnotationDifferences -eq 1) 'Effective Place mismatch not held.'
    Assert ($summary.HeldForAmbiguousIngestionOrder -eq 1) 'Equal creation timestamps were accepted.'
    Assert ($summary.AutomaticallyExecuted -eq 0) 'Planner executed changes.'
    Assert ($summary.AssetRowsDeleted -eq 0) 'Planner claims to delete an asset.'

    $planPath = Join-Path $outputDirectory 'retirement-candidates-private.csv'
    $items = @(Import-Csv -LiteralPath $planPath)
    Assert ($items.Count -eq 7) 'Not exactly one proposed action per collision.'
    $auto = @($items | Where-Object { $_.NormalizedSourceKey -eq 'fideli/auto.jpg' })[0]
    Assert ($auto.ProposedAction -eq 'review-for-reversible-alias-retirement') 'Safe automatic-only duplicate not proposed.'
    Assert ($auto.SurvivorAssetId -eq 'older-auto') 'Original survivor not retained.'
    Assert ($auto.SecondaryAssetId -eq 'newer-auto') 'Wrong later candidate.'
    Assert ($auto.SecondaryAutomaticReviewActions -eq 3) 'Automatic review history was not classified.'
    Assert (@($items | Where-Object { $_.ApplyEnabled -ne 'False' }).Count -eq 0) 'Apply was inadvertently enabled.'
    Assert (@($items | Where-Object { $_.AssetRowsToDelete -ne '0' }).Count -eq 0) 'Deletion appeared.'

    $prior = Get-Content -LiteralPath $planPath -Raw
    $null = & $planner -EvidenceCsv $evidenceCsv -EffectiveStateCsv $effectiveCsv -OutputDirectory $outputDirectory
    Assert ((Get-Content -LiteralPath $planPath -Raw) -ceq $prior) 'Planner output was not deterministic.'

    $rows[1].human_or_unknown_person_labels = '1'
    $rows | Export-Csv -LiteralPath $evidenceCsv -Encoding utf8
    $nowHeld = & $planner -EvidenceCsv $evidenceCsv -EffectiveStateCsv $effectiveCsv -OutputDirectory $outputDirectory
    Assert ($nowHeld.ReversibleRetirementCandidates -eq 0) 'Human/unknown label was ignored.'

    $rows[1].human_or_unknown_person_labels = '0'
    $rows[1].verified_content_sha256 = 'wrong'
    $rows | Export-Csv -LiteralPath $evidenceCsv -Encoding utf8
    $nowHeld = & $planner -EvidenceCsv $evidenceCsv -EffectiveStateCsv $effectiveCsv -OutputDirectory $outputDirectory
    Assert ($nowHeld.ReversibleRetirementCandidates -eq 0) 'Invalid content evidence was ignored.'

    Write-Host 'Archive case-alias automatic retirement plan synthetic tests: PASS'
}
finally {
    if (Test-Path -LiteralPath $root) {
        Remove-Item -LiteralPath $root -Recurse -Force
    }
}
