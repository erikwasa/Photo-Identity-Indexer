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
        -Body ($Body | ConvertTo-Json -Depth 8) -TimeoutSec 10
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

function Assert-HostedClientRoute {
    param([Parameter(Mandatory)] [string] $Path)

    $response = Invoke-WebRequest -Uri "$BaseUrl$Path" -UseBasicParsing -TimeoutSec 10
    if ($response.StatusCode -ne 200 -or
        $response.Content.IndexOf("blazor.webassembly.js", [StringComparison]::OrdinalIgnoreCase) -lt 0) {
        throw "Hosted Blazor client route '$Path' was not served by the published application."
    }
}

function Assert-PrivacyLimitedJson {
    param(
        [Parameter(Mandatory)] [string] $Content,
        [Parameter(Mandatory)] [string] $Description
    )

    foreach ($privateValue in @($Manifest.DatabasePath, $Manifest.ArtifactDirectory)) {
        if (-not [string]::IsNullOrWhiteSpace([string]$privateValue) -and
            $Content.IndexOf([string]$privateValue, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
            throw "$Description exposed a private verification path."
        }
    }

    foreach ($privateField in @("rootLocator", "cropStoragePath", "embedding", "vector")) {
        if ($Content.IndexOf($privateField, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
            throw "$Description exposed private field '$privateField'."
        }
    }
}

$smoke = [ordered]@{
    health = "passed"
    hostedClient = "not_run"
    workflowPages = "not_run"
    gallery = "not_run"
    suggestionGallery = "not_run"
    queueNavigation = "not_run"
    image = "not_run"
    assignmentUndo = "not_run"
    rejection = "not_run"
    bulkMutation = "not_run"
    personAudit = "not_run"
    bulkSuggestionMutation = "not_run"
    suggestionAccept = "not_run"
    suggestionReject = "not_run"
    personRename = "not_run"
    personMerge = "not_run"
    cacheControl = "not_run"
}

Assert-HostedClientRoute -Path "/"
$smoke.hostedClient = "passed"
foreach ($route in @("/suggestions", "/bulk-suggestions", "/audit", "/progress", "/people")) {
    Assert-HostedClientRoute -Path $route
}
$smoke.workflowPages = "passed"

if ([int]$Manifest.UnreviewedCount -le 40) {
    throw "The interactive fixture must contain more than one 40-card review page."
}

$galleryResponse = Invoke-WebRequest -Uri "$BaseUrl/api/review/faces?state=all&offset=0&limit=200" `
    -UseBasicParsing -TimeoutSec 10
$gallery = $galleryResponse.Content | ConvertFrom-Json
$galleryItems = @($gallery.Items)
if ($gallery.Total -ne $Manifest.FaceCount -or $galleryItems.Count -ne $Manifest.FaceCount) {
    throw "Review gallery did not return the prepared synthetic faces."
}
Assert-PrivacyLimitedJson -Content $galleryResponse.Content -Description "Review gallery"
$smoke.gallery = "passed"

$galleryCache = [string]$galleryResponse.Headers["Cache-Control"]
if ($galleryCache.IndexOf("no-store", [StringComparison]::OrdinalIgnoreCase) -lt 0) {
    throw "Review gallery response did not include Cache-Control: no-store."
}

$modelId = [Uri]::EscapeDataString([string]$Manifest.EmbedderModelId)
$modelHash = [Uri]::EscapeDataString([string]$Manifest.EmbedderModelHash)
$suggestionGalleryUri = "$BaseUrl/api/review/suggestion-faces?state=unreviewed&offset=0&limit=100&sort=suggested-person&modelId=$modelId&modelHash=$modelHash"
$suggestionGalleryResponse = Invoke-WebRequest -Uri $suggestionGalleryUri -UseBasicParsing -TimeoutSec 10
$suggestionGallery = $suggestionGalleryResponse.Content | ConvertFrom-Json
$suggestionItems = @($suggestionGallery.Items)
$rankedSuggestionItems = @($suggestionItems | Where-Object { $null -ne $_.topSuggestion })
if ($rankedSuggestionItems.Count -lt 4) {
    throw "Suggestion gallery did not expose enough pending rank-one suggestions."
}
foreach ($item in $rankedSuggestionItems) {
    if ($item.topSuggestion.rank -ne 1 -or
        $item.topSuggestion.modelId -ne $Manifest.EmbedderModelId -or
        $item.topSuggestion.modelHash -ne $Manifest.EmbedderModelHash) {
        throw "Suggestion gallery returned a non-rank-one or wrong-revision suggestion."
    }
}
Assert-PrivacyLimitedJson -Content $suggestionGalleryResponse.Content -Description "Suggestion gallery"
$smoke.suggestionGallery = "passed"

$queueTarget = Get-RequiredItemById -Items $suggestionItems -Id $Manifest.SuggestionAcceptFaceId `
    -Description "queue-navigation face"
$queueDetails = Invoke-RestMethod -Uri (
    "$BaseUrl/api/review/suggestion-faces/$($queueTarget.id)?state=unreviewed&sort=suggested-person&modelId=$modelId&modelHash=$modelHash") `
    -TimeoutSec 10
if ($null -eq $queueDetails.navigation -or
    $queueDetails.navigation.position -lt 1 -or
    $queueDetails.navigation.total -ne $suggestionGallery.Total -or
    ($queueDetails.navigation.total -gt 1 -and
        [string]::IsNullOrWhiteSpace([string]$queueDetails.navigation.previousFaceId) -and
        [string]::IsNullOrWhiteSpace([string]$queueDetails.navigation.nextFaceId))) {
    throw "Suggestion details did not preserve the exact-model queue navigation scope."
}
$smoke.queueNavigation = "passed"

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

$auditUri = "$BaseUrl/api/review/people/$($person.id)/assigned-faces?offset=0&limit=40&sort=assigned-desc&modelId=$modelId&modelHash=$modelHash"
$auditResponse = Invoke-WebRequest -Uri $auditUri -UseBasicParsing -TimeoutSec 10
$audit = $auditResponse.Content | ConvertFrom-Json
$auditItems = @($audit.Items)
if ($audit.Total -ne 2 -or $auditItems.Count -ne 2 -or
    @($auditItems | Where-Object { $_.assignedPerson.id -ne $person.id }).Count -ne 0) {
    throw "Person audit did not return the complete active assignment set."
}
Assert-PrivacyLimitedJson -Content $auditResponse.Content -Description "Person audit"
$smoke.personAudit = "passed"

$freshSuggestionGallery = Invoke-RestMethod -Uri $suggestionGalleryUri -TimeoutSec 10
$freshSuggestionItems = @($freshSuggestionGallery.Items)
$excludedFaceIds = @(
    $Manifest.SuggestionAcceptFaceId,
    $Manifest.SuggestionRejectFaceId,
    $Manifest.RejectionFaceId,
    $Manifest.MergeSourceFaceId
) + $bulkFaceIds
$bulkSuggestionCandidates = @($freshSuggestionItems | Where-Object {
    $null -ne $_.topSuggestion -and $excludedFaceIds -notcontains $_.id
})
$bulkSuggestionGroup = $bulkSuggestionCandidates |
    Group-Object -Property { $_.topSuggestion.person.id } |
    Where-Object { $_.Count -ge 2 } |
    Sort-Object Count -Descending |
    Select-Object -First 1
if ($null -eq $bulkSuggestionGroup) {
    throw "The fixture did not expose two eligible rank-one suggestions for one person."
}
$bulkSuggestionFaces = @($bulkSuggestionGroup.Group | Select-Object -First 2)
$bulkSuggestionIds = @($bulkSuggestionFaces | ForEach-Object { [long]$_.topSuggestion.id })
$bulkSuggestionPreview = Invoke-JsonPost -Uri "$BaseUrl/api/review/bulk-suggestions/preview" -Body @{
    suggestionIds = $bulkSuggestionIds
    modelId = $Manifest.EmbedderModelId
    modelHash = $Manifest.EmbedderModelHash
}
if ($bulkSuggestionPreview.requestedCount -ne 2 -or
    $bulkSuggestionPreview.affectedCount -ne 2 -or
    $bulkSuggestionPreview.person.id -ne $bulkSuggestionGroup.Name) {
    throw "Bulk suggestion preview did not bind one same-person affected set."
}
$bulkSuggestionResult = Invoke-JsonPost -Uri "$BaseUrl/api/review/bulk-suggestions/commit" -Body @{
    suggestionIds = $bulkSuggestionIds
    modelId = $Manifest.EmbedderModelId
    modelHash = $Manifest.EmbedderModelHash
    expectedAffectedCount = $bulkSuggestionPreview.affectedCount
    previewToken = $bulkSuggestionPreview.previewToken
    confirm = $true
    actor = "verification:bulk-suggestion-smoke"
    note = "Automated preview-first grouped suggestion acceptance."
}
if ($bulkSuggestionResult.affectedCount -ne 2 -or
    $bulkSuggestionResult.person.id -ne $bulkSuggestionPreview.person.id) {
    throw "Bulk suggestion commit did not apply the previewed same-person group."
}
foreach ($bulkSuggestionFace in $bulkSuggestionFaces) {
    $bulkSuggestionDetails = Invoke-RestMethod -Uri "$BaseUrl/api/review/faces/$($bulkSuggestionFace.id)" -TimeoutSec 10
    $bulkSuggestionState = Invoke-RestMethod -Uri "$BaseUrl/api/review/faces/$($bulkSuggestionFace.id)/suggestions" -TimeoutSec 10
    $accepted = @($bulkSuggestionState | Where-Object { $_.id -eq $bulkSuggestionFace.topSuggestion.id })
    if ($bulkSuggestionDetails.face.state -ne "assigned" -or
        $bulkSuggestionDetails.face.person.id -ne $bulkSuggestionPreview.person.id -or
        $accepted.Count -ne 1 -or
        $accepted[0].status -ne "accepted" -or
        $accepted[0].latestAction.kind -ne "accept" -or
        $null -eq $accepted[0].latestAction.reviewActionId) {
        throw "Bulk suggestion acceptance did not create linked assignment and suggestion audit actions."
    }
}
$smoke.bulkSuggestionMutation = "passed"

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
    $targetAfterMerge.photoCount -lt 2 -or
    $mergeSourceDetails.face.person.id -ne $Manifest.MergeTargetPersonId -or
    @($historyAfterMerge | Where-Object { $_.id -eq $mergeAction.id }).Count -ne 1) {
    throw "Person merge did not retire the source and consolidate its labels."
}
$smoke.personMerge = "passed"

return $smoke