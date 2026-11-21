# Synthetic only. Lowercase Fideli wins regardless of asset age and
# automatic effective annotations. No database or originals accessed.
[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$script = Join-Path $repo 'tools\new-archive-fideli-canonical-preview.ps1'
$root = Join-Path ([IO.Path]::GetTempPath()) ('photoidentity-fideli-canonical-' + [guid]::NewGuid().ToString('N'))
$evidenceCsv = Join-Path $root 'retirement.csv'
$effectiveCsv = Join-Path $root 'effective.csv'
$sourceId = [guid]::NewGuid().ToString('D')
$hash = 'a' * 64
function Assert([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}
function Evidence([int]$Number, [bool]$Upper, [string]$Id, [string]$Revision,
    [bool]$Missing = $false, [int]$ExplicitTags = 0,
    [bool]$WrongHash = $false, [bool]$LowerNewer = $false) {
    $prefix = if ($Upper) { 'Fideli/' } else { 'fideli/' }
    $earlier = '2026-11-19T12:00:00Z'
    $later = '2026-11-20T12:00:00Z'
    $created = if ($Upper -xor $LowerNewer) { $later } else { $earlier }
    [pscustomobject][ordered]@{
        source_id = $sourceId
        asset_id = $Id
        normalized_source_key = ('fideli/photo{0:d2}.jpg' -f $Number)
        source_key = ('{0}photo{1:d2}.jpg' -f $prefix, $Number)
        created_at_utc = $created
        marked_missing = if ($Missing) { 't' } else { 'f' }
        verification_state = 'verified'
        verified_revision_id = $Revision
        verified_content_sha256 = if ($WrongHash) { 'b' * 64 } else { $hash }
        verified_size_bytes = '100'
        has_source_exclusion = 'f'
        human_or_unknown_face_review_actions = '0'
        human_or_unknown_person_labels = '0'
        photo_tag_actions = [string]$ExplicitTags
        photo_person_actions = '0'
        manual_place_actions = '0'
        manual_capture_date_actions = '0'
        presentation_preference_actions = '0'
        manual_list_collection_items = '0'
        featured_face_references = '0'
    }
}
function State([int]$Number, [string]$Lower, [string]$Upper,
    [bool]$MatchingAnnotations = $true, [bool]$MatchingHash = $true) {
    [pscustomobject][ordered]@{
        source_id = $sourceId
        normalized_source_key = ('fideli/photo{0:d2}.jpg' -f $Number)
        first_asset_id = $Lower
        second_asset_id = $Upper
        both_verified = 't'
        matching_stored_hash = if ($MatchingHash) { 't' } else { 'f' }
        matching_effective_tags = if ($MatchingAnnotations) { 't' } else { 'f' }
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
    for ($n = 1; $n -le 5; $n++) {
        $lowerId = [guid]::NewGuid().ToString('D')
        $upperId = [guid]::NewGuid().ToString('D')
        # 1 automatic effective-state difference; 2 later lowercase;
        # 3 uppercase explicit tag; 4 hash mismatch; 5 inactive upper.
        $rows.Add((Evidence $n $false $lowerId ([guid]::NewGuid().ToString('D')) -LowerNewer:($n -eq 2)))
        $rows.Add((Evidence $n $true $upperId ([guid]::NewGuid().ToString('D')) -Missing:($n -eq 5) -ExplicitTags:([int]($n -eq 3)) -WrongHash:($n -eq 4) -LowerNewer:($n -eq 2)))
        $states.Add((State $n $lowerId $upperId -MatchingAnnotations:($n -ne 1) -MatchingHash:($n -ne 4)))
    }
    $rows | Export-Csv -LiteralPath $evidenceCsv -Encoding utf8
    $states | Export-Csv -LiteralPath $effectiveCsv -Encoding utf8
    $output = Join-Path $root 'preview-01'
    $result = & $script -RetirementEvidenceCsv $evidenceCsv -EffectiveStateCsv $effectiveCsv -OutputDirectory $output
    Assert ($result.PathCollisionGroupsRevalidated -eq 5) 'Wrong input group count.'
    Assert ($result.ExactFideliSpellingPairs -eq 5) 'Wrong exact Fideli scope.'
    Assert ($result.CandidateLowercaseCanonical -eq 2) 'Auto difference or lower-newer not accepted.'
    Assert ($result.OfCandidatesWithEffectiveMetadataDifferences -eq 1) 'Auto metadata difference not reported.'
    Assert ($result.OfCandidatesNotCompatibleWithCurrentRunner -eq 2) 'Old runner restrictions not reported.'
    Assert ($result.RequiresExplicitUpperHistoryReconciliation -eq 1) 'Upper explicit tags silently ignored.'
    Assert ($result.BlockedContentProof -eq 1) 'Mismatched SHA-256 passed.'
    Assert ($result.ExcludedOrInactive -eq 1) 'Already inactive upper was selected.'
    Assert ($result.AutomaticallyApplied -eq 0) 'Readonly guarantee violated.'
    $plan = @(Import-Csv -LiteralPath $result.PrivateAssessmentPath)
    Assert ($plan.Count -eq 5) 'Private assessment incomplete.'
    Assert ($plan[0].CanonicalSourceKey.StartsWith('fideli/', [StringComparison]::Ordinal)) 'Wrong keeper.'
    Assert ($plan[0].AutomaticWriteApproved -eq 'False') 'Unexpected write authorization.'
    Assert (@($plan | Where-Object Classification -eq 'candidate-lowercase-canonical').Count -eq 2) 'Incorrect candidate classifications.'
    $overwriteRejected = $false
    try {
        $null = & $script -RetirementEvidenceCsv $evidenceCsv -EffectiveStateCsv $effectiveCsv -OutputDirectory $output
    } catch { $overwriteRejected = $true }
    Assert $overwriteRejected 'Existing private preview was overwritten.'
    $batchGenerator = Join-Path $repo 'tools\new-archive-fideli-canonical-batches.ps1'
    $session = & $batchGenerator -RetirementEvidenceCsv $evidenceCsv -EffectiveStateCsv $effectiveCsv `
        -MaxBatches 1 -PairsPerBatch 2 -ExpectedDatabaseSchemaVersion 45 `
        -OutputDirectory (Join-Path $root 'canonical-batches')
    Assert ($session.CanonicalEligibleUnretired -eq 2) 'Canonical batch planner lost candidates.'
    Assert ($session.UppercaseExplicitHistoryHeld -eq 1) 'Protected upper edit leaked into batches.'
    Assert ($session.PairsStaged -eq 2 -and $session.BatchesStaged -eq 1) 'Canonical batch limit ignored.'
    Assert ($session.AutomaticallyApplied -eq 0) 'Generator performed unexpected Apply.'
    $batch = $session.Batches[0]
    $members = @(Import-Csv -LiteralPath $batch.PrivateManifestPath)
    $meta = Get-Content -LiteralPath $batch.PrivateMetadataPath -Raw | ConvertFrom-Json
    Assert ($members.Count -eq 2) 'Canonical batch size incorrect.'
    Assert ($meta.retirementPolicy -eq 'fideli-lowercase-canonical-v1') 'Missing explicit policy seal.'
    Assert ($meta.privatePilotManifestSha256 -eq (Get-FileHash -LiteralPath $batch.PrivateManifestPath -Algorithm SHA256).Hash.ToLowerInvariant()) 'Canonical batch hash not pinned.'
    Assert ($meta.pilotPairs -eq 2 -and $meta.expectedDatabaseSchemaVersion -eq 45) 'Canonical batch metadata count or schema incorrect.'
    Assert (@($members | Where-Object {
        $_.SurvivorSourceKey.StartsWith('fideli/', [StringComparison]::Ordinal) -and
        $_.SecondarySourceKey -ceq ('F' + $_.SurvivorSourceKey.Substring(1)) -and
        $_.ApplyEnabled -ceq 'false' -and $_.DeleteDatabaseRows -ceq 'false'
    }).Count -eq 2) 'Canonical batch identity/del safety flags failed.'
    Assert ($session.DatabaseRowsDeleted -eq 0 -and $session.OriginalsDeleted -eq 0) 'Unexpected destructive operation.'
    Write-Host 'Fideli canonical batch staging synthetic acceptance: PASS'
    Write-Host 'Fideli lowercase canonical preview synthetic acceptance: PASS'
}
finally {
    if (Test-Path -LiteralPath $root) {
        Remove-Item -LiteralPath $root -Recurse -Force
    }
}
