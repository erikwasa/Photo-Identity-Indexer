# WI-0191: optional, private, read-only maintained-host search regression.
# No collection writes, media decoding, model downloads or evidence generation.
[CmdletBinding()]
param(
    [string]$BaseUrl = "http://127.0.0.1:5080",
    [string]$PhotoQuery,
    [string]$VideoQuery,
    [ValidateSet("caption", "semantic", "combined")]
    [string]$PhotoMode = "caption",
    [ValidateSet("caption", "semantic", "combined")]
    [string]$VideoMode = "combined",
    [ValidateRange(1, 200)]
    [int]$Limit = 10
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$uri = [uri]$BaseUrl
if ($uri.Scheme -ne "http" -or
    $uri.Host -notin @("localhost", "127.0.0.1", "::1") -or
    $uri.AbsolutePath -ne "/" -or
    $uri.Query -or $uri.Fragment -or $uri.UserInfo) {
    throw "Use an HTTP loopback Photo Identity URL, e.g. http://127.0.0.1:5080."
}
$root = $BaseUrl.TrimEnd("/")

if ([string]::IsNullOrWhiteSpace($PhotoQuery)) {
    $PhotoQuery = Read-Host "Private known-positive photo search phrase"
}
if ([string]::IsNullOrWhiteSpace($VideoQuery)) {
    $VideoQuery = Read-Host "Private known-positive video scene phrase"
}
if ([string]::IsNullOrWhiteSpace($PhotoQuery) -or
    [string]::IsNullOrWhiteSpace($VideoQuery) -or
    $PhotoQuery.Length -gt 300 -or $VideoQuery.Length -gt 300) {
    throw "Provide two nonempty search phrases of at most 300 characters."
}

function Invoke-PrivateSearch {
    param([string]$Route, [string]$Phrase, [string]$Mode, [int]$Count)
    $body = @{ query = $Phrase; mode = $Mode; limit = $Count } | ConvertTo-Json
    return Invoke-RestMethod -Method Post -Uri "$root$Route" -ContentType "application/json" -Body $body -TimeoutSec 90
}

function Get-PhotoResultSignature {
    param($Response)
    # Compare order and component scores/sources, without emitting private IDs.
    $items = @(
        foreach ($item in $Response.items) {
            [ordered]@{
                Revision = [string]$item.revisionId
                Score = $item.combinedScore
                Semantic = $item.semanticScore
                Caption = $item.captionScore
                Sources = @($item.sources)
            }
        }
    )
    return (ConvertTo-Json -InputObject $items -Depth 7 -Compress)
}

function Get-SavedCollectionSignature {
    param($Collections)
    $items = @(
        foreach ($collection in @($Collections | Sort-Object id)) {
            [ordered]@{
                Id = [string]$collection.id
                Name = [string]$collection.name
                RevisionIds = @($collection.revisionIds)
            }
        }
    )
    return (ConvertTo-Json -InputObject $items -Depth 7 -Compress)
}

$collectionsBefore = @(Invoke-RestMethod -Uri "$root/api/photo-list-collections" -TimeoutSec 90)
$photoBefore = Invoke-PrivateSearch "/api/photo-search/query" $PhotoQuery $PhotoMode $Limit

# A known-positive control prevents a misleading zero-vs-zero pass.
if (@($photoBefore.items).Count -eq 0) {
    throw "Known-positive photo control returned no matches; choose a matching photo caption."
}

$video = Invoke-PrivateSearch "/api/video-search/query" $VideoQuery $VideoMode $Limit
if (@($video.items).Count -eq 0) {
    throw "Known-positive video control returned no matches; choose a matching indexed video scene."
}

$photoAfter = Invoke-PrivateSearch "/api/photo-search/query" $PhotoQuery $PhotoMode $Limit
$collectionsAfter = @(Invoke-RestMethod -Uri "$root/api/photo-list-collections" -TimeoutSec 90)

$rankSame = (Get-PhotoResultSignature $photoBefore) -ceq (Get-PhotoResultSignature $photoAfter)
$countsSame = (
    $photoBefore.indexedPhotoCount -eq $photoAfter.indexedPhotoCount -and
    $photoBefore.displayableCaptionCount -eq $photoAfter.displayableCaptionCount -and
    $photoBefore.mode -eq $photoAfter.mode -and
    $photoBefore.semanticModelAvailable -eq $photoAfter.semanticModelAvailable
)
$collectionsSame = (Get-SavedCollectionSignature $collectionsBefore) -ceq (Get-SavedCollectionSignature $collectionsAfter)
$videoIds = @($video.items | ForEach-Object { [string]$_.revisionId })
$videoUnique = @($videoIds | Sort-Object -Unique).Count -eq $videoIds.Count

[pscustomobject]@{
    PhotoMode = $photoBefore.mode
    PhotoResultsBefore = @($photoBefore.items).Count
    PhotoResultsAfter = @($photoAfter.items).Count
    PhotoRanksAndScoresUnchanged = $rankSame
    PhotoCountsUnchanged = $countsSame
    PhotoCollectionsUnchanged = $collectionsSame
    SavedCollectionsChecked = $collectionsBefore.Count
    VideoMode = $video.mode
    VideoResults = $videoIds.Count
    OneResultPerVideoRevision = $videoUnique
    PhotoMillisecondsBefore = [math]::Round([double]$photoBefore.searchMilliseconds, 1)
    PhotoMillisecondsAfter = [math]::Round([double]$photoAfter.searchMilliseconds, 1)
    VideoMilliseconds = [math]::Round([double]$video.searchMilliseconds, 1)
}

if (!$rankSame -or !$countsSame -or !$collectionsSame -or !$videoUnique) {
    throw "Search isolation regression detected; investigate locally without publishing captions or IDs."
}
# Timings are observations, not proof of unchanged pre-upgrade baseline.
