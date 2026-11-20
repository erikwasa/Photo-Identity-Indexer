# No PostgreSQL or source media: private pilot preview only.
[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$previewScript = Join-Path $repo 'tools\new-archive-case-alias-pilot-preview.ps1'
$root = Join-Path ([IO.Path]::GetTempPath()) ('photoidentity-alias-pilot-' + [guid]::NewGuid().ToString('N'))
$evidenceCsv = Join-Path $root 'evidence.csv'
$effectiveCsv = Join-Path $root 'effective.csv'
$outputDirectory = Join-Path $root 'preview'
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
    New-Item -ItemType Directory -Path $root -Force | Out-Null
    $rows = @(
        (New-Evidence 'Fideli/auto.jpg' 'old-auto' $false)
        (New-Evidence 'fideli/auto.jpg' 'new-auto' $true -AutomaticFaceReviewActions 3)
        (New-Evidence 'Fideli/safe-one.jpg' 'old-one' $false)
        (New-Evidence 'fideli/safe-one.jpg' 'new-one' $true)
        (New-Evidence 'Fideli/safe-two.jpg' 'old-two' $false)
        (New-Evidence 'fideli/safe-two.jpg' 'new-two' $true)
        (New-Evidence 'Fideli/blocked.jpg' 'old-block' $false)
        (New-Evidence 'fideli/blocked.jpg' 'new-block' $true -HumanFaceReviewActions 1)
    )
    $states = @(
        (New-Effective 'Fideli/auto.jpg' 'old-auto' 'new-auto')
        (New-Effective 'Fideli/safe-one.jpg' 'old-one' 'new-one')
        (New-Effective 'Fideli/safe-two.jpg' 'old-two' 'new-two')
        (New-Effective 'Fideli/blocked.jpg' 'old-block' 'new-block')
    )
    $rows | Export-Csv -LiteralPath $evidenceCsv -Encoding utf8
    $states | Export-Csv -LiteralPath $effectiveCsv -Encoding utf8

    $summary = & $previewScript -RetirementEvidenceCsv $evidenceCsv -EffectiveStateCsv $effectiveCsv -MaxPairs 2 -OutputDirectory $outputDirectory
    Assert ($summary.CollisionGroupsRevalidated -eq 4) 'Wrong group count.'
    Assert ($summary.RetirementCandidatesRevalidated -eq 3) 'Wrong candidate count.'
    Assert ($summary.HeldForReview -eq 1) 'The human-edited duplicate was not held.'
    Assert ($summary.PilotPairsSelected -eq 2) 'The maximum pilot size was not respected.'
    Assert ($summary.PilotPairsWithAutomaticReviewHistory -eq 1) 'Pilot must include one auto-review case.'
    Assert ($summary.AutomaticallyExecuted -eq 0) 'Preview ran a database operation.'

    $manifest = @(Import-Csv -LiteralPath $summary.PrivatePilotManifestPath)
    Assert ($manifest.Count -eq 2) 'Expected exactly two private rows.'
    Assert (@($manifest | Where-Object {
        $_.SecondaryAssetId -eq 'new-block'
    }).Count -eq 0) 'Protected secondary was selected.'
    Assert (@($manifest | Where-Object {
        $_.SecondaryAssetId -eq 'new-auto'
    }).Count -eq 1) 'Auto-history sample was omitted.'
    Assert (@($manifest | Where-Object {
        $_.ApplyEnabled -ne 'false' -or
        $_.ApprovalRequired -ne 'true' -or
        $_.DeleteSourceOriginal -ne 'false' -or
        $_.DeleteDatabaseRows -ne 'false'
    }).Count -eq 0) 'A manifest row allows destructive or unapproved work.'

    $meta = Get-Content -LiteralPath $summary.PrivateMetadataPath -Raw | ConvertFrom-Json
    Assert ($meta.expectedDatabaseSchemaVersion -eq 44) 'Schema compatibility not pinned.'
    $v45 = & $previewScript -RetirementEvidenceCsv $evidenceCsv -EffectiveStateCsv $effectiveCsv -MaxPairs 2 -ExpectedDatabaseSchemaVersion 45 -OutputDirectory $outputDirectory
    $v45Meta = Get-Content -LiteralPath $v45.PrivateMetadataPath -Raw | ConvertFrom-Json
    Assert ($v45Meta.expectedDatabaseSchemaVersion -eq 45) 'Explicit schema 45 metadata was not pinned.'
    Assert ($meta.applyEnabled -eq $false) 'Metadata claims apply is enabled.'
    Assert ($meta.privatePilotManifestSha256 -eq $summary.PilotManifestSha256) 'Digest mismatch.'
    Assert ($meta.retirementEvidenceSha256 -eq (Get-FileHash $evidenceCsv -Algorithm SHA256).Hash.ToLowerInvariant()) 'Evidence digest mismatch.'

    $before = Get-Content -LiteralPath $summary.PrivatePilotManifestPath -Raw
    $rerun = & $previewScript -RetirementEvidenceCsv $evidenceCsv -EffectiveStateCsv $effectiveCsv -MaxPairs 2 -OutputDirectory $outputDirectory
    Assert ((Get-Content -LiteralPath $rerun.PrivatePilotManifestPath -Raw) -ceq $before) 'Preview was not deterministic.'

    $one = & $previewScript -RetirementEvidenceCsv $evidenceCsv -EffectiveStateCsv $effectiveCsv -MaxPairs 1 -OutputDirectory $outputDirectory
    Assert ($one.PilotPairsSelected -eq 1) 'Single pair limit was ignored.'
    Assert ($one.PilotPairsWithAutomaticReviewHistory -eq 0) 'Single pilot should prefer no auto history.'

    $rows[1].human_or_unknown_person_labels = '1'
    $rows | Export-Csv -LiteralPath $evidenceCsv -Encoding utf8
    $afterHuman = & $previewScript -RetirementEvidenceCsv $evidenceCsv -EffectiveStateCsv $effectiveCsv -MaxPairs 2 -OutputDirectory $outputDirectory
    Assert ($afterHuman.RetirementCandidatesRevalidated -eq 2) 'Preview did not recompute eligibility.'
    Assert ($afterHuman.PilotPairsWithAutomaticReviewHistory -eq 0) 'Human-protected candidate leaked into pilot.'

    (Get-Item -LiteralPath $evidenceCsv).LastWriteTimeUtc = [datetime]::UtcNow.AddHours(-8)
    $rejectedStale = $false
    try {
        $null = & $previewScript -RetirementEvidenceCsv $evidenceCsv -EffectiveStateCsv $effectiveCsv -OutputDirectory $outputDirectory
    } catch {
        $rejectedStale = $true
    }
    Assert $rejectedStale 'Outdated private audit was accepted.'

    (Get-Item -LiteralPath $evidenceCsv).LastWriteTimeUtc = [datetime]::UtcNow
    (Get-Item -LiteralPath $effectiveCsv).LastWriteTimeUtc = [datetime]::UtcNow.AddMinutes(-20)
    $rejectedUnpaired = $false
    try {
        $null = & $previewScript -RetirementEvidenceCsv $evidenceCsv -EffectiveStateCsv $effectiveCsv -OutputDirectory $outputDirectory
    } catch {
        $rejectedUnpaired = $true
    }
    Assert $rejectedUnpaired 'Audits captured more than 15 minutes apart were accepted.'

    Write-Host 'Archive case-alias bounded pilot preview synthetic tests: PASS'
}
finally {
    if (Test-Path -LiteralPath $root) {
        Remove-Item -LiteralPath $root -Force -Recurse
    }
}
