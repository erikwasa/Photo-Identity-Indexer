# Synthetic, local-only acceptance: no PostgreSQL, originals or API.
[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$generator = Join-Path $repo 'tools\new-archive-case-alias-batch-previews.ps1'
$root = Join-Path ([IO.Path]::GetTempPath()) ('photoidentity-alias-batches-' + [guid]::NewGuid().ToString('N'))
$evidenceCsv = Join-Path $root 'evidence.csv'
$stateCsv = Join-Path $root 'effective.csv'
$output = Join-Path $root 'new-session'
$sourceId = [guid]::NewGuid().ToString('D')
$sha = 'a' * 64

function Assert([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}
function Evidence {
    param(
        [int]$Number, [bool]$Secondary,
        [string]$AssetId, [string]$RevisionId,
        [bool]$Protected = $false, [bool]$Retired = $false,
        [int]$AutoReviews = 0
    )
    $name = 'folder/photo{0:d2}.jpg' -f $Number
    if (-not $Secondary) { $name = 'FOLDER/photo{0:d2}.jpg' -f $Number }
    [pscustomobject][ordered]@{
        source_id = $sourceId
        normalized_source_key = $name.ToLowerInvariant()
        asset_id = $AssetId
        source_key = $name
        created_at_utc = if ($Secondary) { '2026-11-20 12:00:00+00' } else { '2026-11-19 12:00:00+00' }
        marked_missing = if ($Retired) { 't' } else { 'f' }
        verification_state = 'verified'
        verified_revision_id = $RevisionId
        verified_content_sha256 = $sha
        verified_size_bytes = '1024'
        revision_count = '1'
        face_occurrence_count = '1'
        automatic_face_review_actions = "$AutoReviews"
        human_or_unknown_face_review_actions = if ($Protected) { '1' } else { '0' }
        automatic_person_labels = '0'
        human_or_unknown_person_labels = '0'
        photo_tag_actions = '0'
        photo_person_actions = '0'
        manual_place_actions = '0'
        automatic_or_migrated_place_actions = '0'
        manual_capture_date_actions = '0'
        presentation_preference_actions = '0'
        manual_list_collection_items = '0'
        featured_face_references = '0'
        has_source_exclusion = 'f'
    }
}
function State {
    param([int]$Number, [string]$First, [string]$Second)
    [pscustomobject][ordered]@{
        source_id = $sourceId
        normalized_source_key = ('folder/photo{0:d2}.jpg' -f $Number)
        first_asset_id = $First
        second_asset_id = $Second
        both_verified = 't'
        matching_stored_hash = 't'
        matching_effective_tags = 't'
        matching_effective_manual_people = 't'
        matching_effective_place = 't'
        matching_place_provenance = 't'
        matching_confirmed_face_people = 't'
    }
}

try {
    New-Item -ItemType Directory -Path $root | Out-Null
    $rows = [System.Collections.Generic.List[object]]::new()
    $states = [System.Collections.Generic.List[object]]::new()
    $retiredSecondary = $null
    $protectedSecondary = $null
    $autoSecondary = $null

    # 12 eligible; one auto-history; one protected and one already
    # retired group held. The former pilot's 5 retirements are NOT
    # presented as eligible after their missing-state flags become true.
    for ($n = 1; $n -le 14; $n++) {
        $older = [guid]::NewGuid().ToString('D')
        $newer = [guid]::NewGuid().ToString('D')
        $rows.Add((Evidence $n $false $older ([guid]::NewGuid().ToString('D'))))
        $rows.Add((Evidence $n $true $newer ([guid]::NewGuid().ToString('D')) -Protected:($n -eq 13) -Retired:($n -eq 14) -AutoReviews:([int]($n -eq 12))))
        $states.Add((State $n $older $newer))
        if ($n -eq 12) { $autoSecondary = $newer }
        if ($n -eq 13) { $protectedSecondary = $newer }
        if ($n -eq 14) { $retiredSecondary = $newer }
    }
    $rows | Export-Csv -LiteralPath $evidenceCsv -Encoding utf8
    $states | Export-Csv -LiteralPath $stateCsv -Encoding utf8

    $session = & $generator -RetirementEvidenceCsv $evidenceCsv -EffectiveStateCsv $stateCsv -MaxBatches 4 -PairsPerBatch 5 -ExpectedDatabaseSchemaVersion 45 -OutputDirectory $output
    Assert ($session.CollisionGroupsRevalidated -eq 14) 'All groups were not revalidated.'
    Assert ($session.EligibleUnretiredCandidates -eq 12) 'Already retired/protected group leaked into eligibility.'
    Assert ($session.NotEligibleIncludingRetired -eq 2) 'Retired and protected groups were not counted correctly.'
    Assert ($session.PairsStaged -eq 12) 'Incorrect total selected.'
    Assert ($session.BatchesStaged -eq 3) 'Expected three independent batches.'
    Assert ($session.AutomaticallyExecuted -eq 0) 'Unexpected Apply.'
    Assert ($session.DatabaseRowsDeleted -eq 0 -and $session.OriginalFilesDeleted -eq 0) 'Deletion flag enabled.'
    Assert (@($session.Batches | ForEach-Object { $_.Pairs } | Where-Object { $_ -ne 5 -and $_ -ne 2 }).Count -eq 0) 'Unexpected per-batch size.'

    $allSecondaries = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    $total = 0
    foreach ($batch in $session.Batches) {
        Assert (Test-Path -LiteralPath $batch.PrivateManifestPath -PathType Leaf) 'Missing private manifest.'
        Assert (Test-Path -LiteralPath $batch.PrivateMetadataPath -PathType Leaf) 'Missing private metadata.'
        $manifest = @(Import-Csv -LiteralPath $batch.PrivateManifestPath)
        $meta = Get-Content -LiteralPath $batch.PrivateMetadataPath -Raw | ConvertFrom-Json
        Assert ($manifest.Count -eq $batch.Pairs -and $manifest.Count -le 5) 'Batch limit was ignored.'
        Assert ($meta.expectedDatabaseSchemaVersion -eq 45) 'Schema 45 pin missing.'
        Assert ($meta.pilotPairs -eq $manifest.Count) 'Manifest count mismatch.'
        Assert ($meta.privatePilotManifestSha256 -eq (Get-FileHash -LiteralPath $batch.PrivateManifestPath -Algorithm SHA256).Hash.ToLowerInvariant()) 'Manifest SHA mismatch.'
        Assert ($meta.retirementEvidenceSha256 -eq (Get-FileHash -LiteralPath $evidenceCsv -Algorithm SHA256).Hash.ToLowerInvariant()) 'Evidence SHA mismatch.'
        Assert ($meta.effectiveStateEvidenceSha256 -eq (Get-FileHash -LiteralPath $stateCsv -Algorithm SHA256).Hash.ToLowerInvariant()) 'Effective-state SHA mismatch.'
        Assert (-not $meta.applyEnabled -and $meta.approvalRequired -and $meta.sourceFilesToDelete -eq 0 -and $meta.databaseRowsToDelete -eq 0) 'Safety gate missing.'
        foreach ($pair in $manifest) {
            Assert ($allSecondaries.Add($pair.SecondaryAssetId)) 'Batches overlap on a secondary.'
            Assert ($pair.SecondaryAssetId -cne $retiredSecondary -and $pair.SecondaryAssetId -cne $protectedSecondary) 'Unsafe candidate selected.'
            Assert ($pair.ApplyEnabled -eq 'false' -and $pair.DeleteSourceOriginal -eq 'false' -and $pair.DeleteDatabaseRows -eq 'false') 'Unsafe manifest flags.'
        }
        $total += $manifest.Count
    }
    Assert ($total -eq 12) 'Batch manifests lost a candidate.'
    Assert ($session.Batches[-1].WithAutomaticReviewHistory -eq 1) 'Automatic review case should be selected after no-history candidates.'
    $sessionSummary = Get-Content -LiteralPath $session.PrivateSessionReportPath -Raw | ConvertFrom-Json
    Assert ($sessionSummary.selectedPairs -eq 12 -and $sessionSummary.batches.Count -eq 3) 'Batch session summary mismatch.'

    $overwriteRejected = $false
    try {
        $null = & $generator -RetirementEvidenceCsv $evidenceCsv -EffectiveStateCsv $stateCsv -OutputDirectory $output
    }
    catch { $overwriteRejected = $true }
    Assert $overwriteRejected 'Existing private manifests could be overwritten.'

    $one = & $generator -RetirementEvidenceCsv $evidenceCsv -EffectiveStateCsv $stateCsv -MaxBatches 1 -PairsPerBatch 3 -OutputDirectory (Join-Path $root 'small-session')
    Assert ($one.PairsStaged -eq 3 -and $one.BatchesStaged -eq 1) 'Single-batch bound ignored.'

    (Get-Item -LiteralPath $evidenceCsv).LastWriteTimeUtc = [datetime]::UtcNow.AddHours(-8)
    $staleRejected = $false
    try {
        $null = & $generator -RetirementEvidenceCsv $evidenceCsv -EffectiveStateCsv $stateCsv -OutputDirectory (Join-Path $root 'stale-session')
    }
    catch { $staleRejected = $true }
    Assert $staleRejected 'Stale audit was accepted.'

    Write-Host 'Archive case-alias multi-batch previews synthetic acceptance: PASS'
}
finally {
    if (Test-Path -LiteralPath $root) {
        Remove-Item -LiteralPath $root -Recurse -Force
    }
}
