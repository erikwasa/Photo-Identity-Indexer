[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $BaseUrl,
    [Parameter(Mandatory)] [pscustomobject] $Manifest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Invoke-JsonPost {
    param(
        [Parameter(Mandatory)] [string] $Uri,
        [Parameter(Mandatory)] [hashtable] $Body
    )

    return Invoke-RestMethod -Method Post -Uri $Uri -ContentType "application/json" `
        -Body ($Body | ConvertTo-Json -Depth 6) -TimeoutSec 10
}

function ConvertTo-ObjectArray {
    param([AllowNull()] $Value)

    $items = [System.Collections.Generic.List[object]]::new()
    foreach ($item in $Value) {
        $items.Add($item)
    }
    return $items.ToArray()
}

function Get-RequiredItemById {
    param(
        [Parameter(Mandatory)] [object[]] $Items,
        [Parameter(Mandatory)] [string] $Id,
        [Parameter(Mandatory)] [string] $Description
    )

    $matches = @($Items | Where-Object { $_.id -eq $Id })
    if ($matches.Count -ne 1) {
        throw "Expected exactly one $Description with id $Id, but found $($matches.Count)."
    }
    return $matches[0]
}

$smoke = [ordered]@{
    health = "passed"
    gallery = "not_run"
    hostedClient = "not_run"
    image = "not_run"
    assignmentUndo = "not_run"
    rejection = "not_run"
    bulkMutation = "not_run"
    suggestionAccept = "not_run"
    suggestionReject = "not_run"
    personRename = "not_run"
    personMerge = "not_run"
    cacheControl = "not_run"
}

$clientResponse = Invoke-WebRequest -Uri "$BaseUrl/" -UseBasicParsing -TimeoutSec 10
if ($clientResponse.StatusCode -ne 200 -or
    $clientResponse.Content.IndexOf("blazor.webassembly.js", [StringComparison]::OrdinalIgnoreCase) -lt 0) {
    throw "Hosted Blazor client was not served from the published application."
}
$smoke.hostedClient = "passed"

$galleryResponse = Invoke-WebRequest -Uri "$BaseUrl/api/review/faces?state=all" `
    -UseBasicParsing -TimeoutSec 10
$gallery = $galleryResponse.Content | ConvertFrom-Json
$galleryItems = @($gallery.Items)
if ($gallery.Total -ne $Manifest.FaceCount -or $galleryItems.Count -ne $Manifest.FaceCount) {
    throw "Review gallery did not return the prepared synthetic faces."
}
$smoke.gallery = "passed"

$galleryCache = [string]$galleryResponse.Headers["Cache-Control"]
if ($galleryCache.IndexOf("no-store", [StringComparison]::OrdinalIgnoreCase) -lt 0) {
    throw "Review gallery response did not include Cache-Control: no-store."
}

$mutationFace = Get-RequiredItemById -Items $galleryItems -Id $Manifest.MutationFaceId `
    -Description "mutation face"
if ($mutationFace.state -ne "unreviewed") {
    throw "The prepared mutation face was not unreviewed."
}

$imageResponse = Invoke-WebRequest -Uri "$BaseUrl$($mutationFace.ImageUrl)" `
    -UseBasicParsing -TimeoutSec 10
$imageCache = [string]$imageResponse.Headers["Cache-Control"]
if ($imageResponse.StatusCode -ne 200 -or $imageResponse.RawContentLength -le 0 -or
    $imageCache.IndexOf("no-store", [StringComparison]::OrdinalIgnoreCase) -lt 0) {
    throw "Review face image content or no-store caching was invalid."
}
$smoke.image = "passed"
$smoke.cacheControl = "passed"

$person = Invoke-JsonPost -Uri "$BaseUrl/api/review/people" -Body @{
    displayName = "Verification Person"
}
Invoke-JsonPost -Uri "$BaseUrl/api/review/faces/$($Manifest.MutationFaceId)/assign" -Body @{
    personId = $person.id
    actor = "verification:smoke"
    note = "Automated assignment followed by undo."
} | Out-Null
Invoke-JsonPost -Uri "$BaseUrl/api/review/faces/$($Manifest.MutationFaceId)/undo" -Body @{
    actor = "verification:smoke"
    note = "Automated undo confirms reversibility."
} | Out-Null
$details = Invoke-RestMethod -Uri "$BaseUrl/api/review/faces/$($Manifest.MutationFaceId)" -TimeoutSec 10
if (@($details.actions).Count -lt 2 -or $details.face.state -ne "unreviewed") {
    throw "Assignment and undo did not restore the unreviewed state with audit history."
}
$smoke.assignmentUndo = "passed"

Invoke-JsonPost -Uri "$BaseUrl/api/review/faces/$($Manifest.RejectionFaceId)/reject" -Body @{
    actor = "verification:rejection-smoke"
    note = "Automated face rejection."
} | Out-Null
$rejectionDetails = Invoke-RestMethod -Uri "$BaseUrl/api/review/faces/$($Manifest.RejectionFaceId)" `
    -TimeoutSec 10
$activeRejections = @($rejectionDetails.actions | Where-Object {
    $_.kind -eq "reject" -and -not $_.reversed
})
if ($rejectionDetails.face.state -ne "rejected" -or $activeRejections.Count -ne 1) {
    throw "Face rejection did not persist with append-only audit history."
}
$smoke.rejection = "passed"

$bulkFaceIds = @($Manifest.BulkFaceIds)
if ($bulkFaceIds.Count -ne 2) {
    throw "The fixture did not provide two bulk-review faces."
}
foreach ($bulkFaceId in $bulkFaceIds) {
    $bulkFace = Get-RequiredItemById -Items $galleryItems -Id $bulkFaceId `
        -Description "bulk-review face"
    if ($bulkFace.state -ne "unreviewed") {
        throw "Bulk-review face $bulkFaceId was not unreviewed before preview."
    }
}
$bulkPreview = Invoke-JsonPost -Uri "$BaseUrl/api/review/bulk/preview" -Body @{
    faceIds = $bulkFaceIds
    action = "assign"
    personId = $person.id
}
if ($bulkPreview.affectedCount -ne 2 -or $bulkPreview.requestedCount -ne 2) {
    throw "Bulk review preview did not report the expected affected count."
}
$bulkResult = Invoke-JsonPost -Uri "$BaseUrl/api/review/bulk/commit" -Body @{
    faceIds = $bulkFaceIds
    action = "assign"
    personId = $person.id
    expectedAffectedCount = $bulkPreview.affectedCount
    previewToken = $bulkPreview.previewToken
    confirm = $true
    actor = "verification:bulk-smoke"
    note = "Automated preview-first bulk assignment."
}
if ($bulkResult.affectedCount -ne 2) {
    throw "Bulk review commit did not apply the previewed affected count."
}
foreach ($bulkFaceId in $bulkFaceIds) {
    $bulkDetails = Invoke-RestMethod -Uri "$BaseUrl/api/review/faces/$bulkFaceId" -TimeoutSec 10
    if ($bulkDetails.face.state -ne "assigned") {
        throw "Bulk review did not persist an assignment for face $bulkFaceId."
    }
}
$smoke.bulkMutation = "passed"

$acceptResponse = Invoke-RestMethod `
    -Uri "$BaseUrl/api/review/faces/$($Manifest.SuggestionAcceptFaceId)/suggestions" `
    -TimeoutSec 10
$acceptSuggestions = @(ConvertTo-ObjectArray -Value $acceptResponse)
$acceptSuggestion = $acceptSuggestions | Where-Object { $_.status -eq "pending" } |
    Sort-Object rank | Select-Object -First 1
if ($null -eq $acceptSuggestion -or
    $acceptSuggestion.modelId -ne $Manifest.EmbedderModelId -or
    $acceptSuggestion.modelHash -ne $Manifest.EmbedderModelHash) {
    throw "The suggestion-accept target lacked a pending exact-revision suggestion."
}
$acceptedSuggestion = Invoke-JsonPost `
    -Uri "$BaseUrl/api/review/faces/$($Manifest.SuggestionAcceptFaceId)/suggestions/$($acceptSuggestion.id)/accept" `
    -Body @{
        actor = "verification:suggestion-accept-smoke"
        note = "Automated suggestion acceptance."
    }
$acceptedDetails = Invoke-RestMethod `
    -Uri "$BaseUrl/api/review/faces/$($Manifest.SuggestionAcceptFaceId)" -TimeoutSec 10
if ($acceptedSuggestion.status -ne "accepted" -or
    $acceptedSuggestion.latestAction.kind -ne "accept" -or
    $acceptedDetails.face.state -ne "assigned" -or
    $acceptedDetails.face.person.id -ne $acceptSuggestion.person.id) {
    throw "Suggestion acceptance did not create the normal audited assignment."
}
$smoke.suggestionAccept = "passed"

$rejectResponse = Invoke-RestMethod `
    -Uri "$BaseUrl/api/review/faces/$($Manifest.SuggestionRejectFaceId)/suggestions" `
    -TimeoutSec 10
$rejectSuggestions = @(ConvertTo-ObjectArray -Value $rejectResponse)
$rejectSuggestion = $rejectSuggestions | Where-Object { $_.status -eq "pending" } |
    Sort-Object rank | Select-Object -First 1
if ($null -eq $rejectSuggestion) {
    throw "The suggestion-reject target lacked a pending ranked suggestion."
}
$rejectedSuggestion = Invoke-JsonPost `
    -Uri "$BaseUrl/api/review/faces/$($Manifest.SuggestionRejectFaceId)/suggestions/$($rejectSuggestion.id)/reject" `
    -Body @{
        actor = "verification:suggestion-reject-smoke"
        note = "Automated durable face-person exclusion."
    }
$suggestionRejectDetails = Invoke-RestMethod `
    -Uri "$BaseUrl/api/review/faces/$($Manifest.SuggestionRejectFaceId)" -TimeoutSec 10
if ($rejectedSuggestion.status -ne "rejected" -or
    $rejectedSuggestion.latestAction.kind -ne "reject" -or
    $null -ne $rejectedSuggestion.latestAction.reviewActionId -or
    $suggestionRejectDetails.face.state -ne "unreviewed" -or
    @($suggestionRejectDetails.actions).Count -ne 0) {
    throw "Suggestion rejection changed the face state or canonical review history."
}
$smoke.suggestionReject = "passed"

$renamedDisplayName = "Renamed Verification Person"
$renameAction = Invoke-JsonPost -Uri "$BaseUrl/api/review/people/$($Manifest.RenamePersonId)/rename" `
    -Body @{
        displayName = $renamedDisplayName
        actor = "verification:rename-smoke"
        note = "Automated reversible rename."
    }
$maintenanceResponse = Invoke-RestMethod -Uri "$BaseUrl/api/review/people/maintenance" -TimeoutSec 10
$maintenancePeople = @(ConvertTo-ObjectArray -Value $maintenanceResponse)
$renamedPerson = Get-RequiredItemById -Items $maintenancePeople -Id $Manifest.RenamePersonId `
    -Description "renamed person"
$historyResponse = Invoke-RestMethod `
    -Uri "$BaseUrl/api/review/people/maintenance/history?limit=100" -TimeoutSec 10
$maintenanceHistory = @(ConvertTo-ObjectArray -Value $historyResponse)
if ($renameAction.kind -ne "rename" -or
    $renameAction.previousDisplayName -ne $Manifest.RenameOriginalDisplayName -or
    $renameAction.newDisplayName -ne $renamedDisplayName -or
    -not $renameAction.reversible -or
    $renamedPerson.displayName -ne $renamedDisplayName -or
    @($maintenanceHistory | Where-Object { $_.id -eq $renameAction.id }).Count -ne 1) {
    throw "Person rename did not update the active person and append history."
}
$smoke.personRename = "passed"

$mergeAction = Invoke-JsonPost -Uri "$BaseUrl/api/review/people/$($Manifest.MergeSourcePersonId)/merge" `
    -Body @{
        targetPersonId = $Manifest.MergeTargetPersonId
        confirmIrreversible = $true
        actor = "verification:merge-smoke"
        note = "Automated explicitly irreversible merge."
    }
$peopleAfterMergeResponse = Invoke-RestMethod `
    -Uri "$BaseUrl/api/review/people/maintenance" -TimeoutSec 10
$peopleAfterMerge = @(ConvertTo-ObjectArray -Value $peopleAfterMergeResponse)
$sourceAfterMerge = @($peopleAfterMerge | Where-Object { $_.id -eq $Manifest.MergeSourcePersonId })
$targetAfterMerge = Get-RequiredItemById -Items $peopleAfterMerge -Id $Manifest.MergeTargetPersonId `
    -Description "merge target person"
$mergeSourceDetails = Invoke-RestMethod `
    -Uri "$BaseUrl/api/review/faces/$($Manifest.MergeSourceFaceId)" -TimeoutSec 10
$historyAfterMergeResponse = Invoke-RestMethod `
    -Uri "$BaseUrl/api/review/people/maintenance/history?limit=100" -TimeoutSec 10
$historyAfterMerge = @(ConvertTo-ObjectArray -Value $historyAfterMergeResponse)
if ($mergeAction.kind -ne "merge" -or
    $mergeAction.targetPersonId -ne $Manifest.MergeTargetPersonId -or
    $mergeAction.reversible -or
    $sourceAfterMerge.Count -ne 0 -or
    $targetAfterMerge.labelCount -lt 2 -or
    $mergeSourceDetails.face.person.id -ne $Manifest.MergeTargetPersonId -or
    @($historyAfterMerge | Where-Object { $_.id -eq $mergeAction.id }).Count -ne 1) {
    throw "Person merge did not retire the source and consolidate its labels."
}
$smoke.personMerge = "passed"

return $smoke
