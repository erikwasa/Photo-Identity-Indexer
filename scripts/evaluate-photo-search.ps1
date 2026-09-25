[CmdletBinding()]
param(
    [Parameter()]
    [uri] $BaseUrl,

    [Parameter()]
    [string] $QuerySuite = (Join-Path $PSScriptRoot '..\experiments\photo-search\wi-0162-query-suite-template.json'),

    [Parameter()]
    [ValidateRange(1, 50)]
    [int] $TopK = 10,

    [Parameter(Mandatory)]
    [string] $OutputDirectory,

    [Parameter()]
    [switch] $Summarize,

    [Parameter()]
    [switch] $AllowSmallSample
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Normalize-BaseUrl {
    param([Parameter(Mandatory)][uri] $Value)
    return $Value.AbsoluteUri.TrimEnd('/')
}

function Get-RelevanceValue {
    param([Parameter(Mandatory)][string] $Value)

    $normalized = $Value.Trim().ToLowerInvariant()
    if ($normalized -in @('1', 'true', 'y', 'yes', 'relevant')) {
        return 1
    }
    if ($normalized -in @('0', 'false', 'n', 'no', 'irrelevant')) {
        return 0
    }
    throw "Relevance value '$Value' must be yes/no, true/false, 1/0, relevant/irrelevant or y/n."
}

$resolvedOutput = [System.IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $resolvedOutput | Out-Null
$resultsPath = Join-Path $resolvedOutput 'wi-0162-search-results.json'
$statusPath = Join-Path $resolvedOutput 'wi-0162-search-status.json'
$relevancePath = Join-Path $resolvedOutput 'wi-0162-relevance.csv'
$summaryPath = Join-Path $resolvedOutput 'wi-0162-summary.json'

if ($Summarize) {
    if (-not (Test-Path -LiteralPath $relevancePath)) {
        throw "Annotated relevance CSV not found: $relevancePath"
    }
    if (-not (Test-Path -LiteralPath $resultsPath)) {
        throw "Search result evidence not found: $resultsPath"
    }
    if (-not (Test-Path -LiteralPath $statusPath)) {
        throw "Search status evidence not found: $statusPath"
    }

    $rows = @(Import-Csv -LiteralPath $relevancePath)
    if ($rows.Count -eq 0) {
        throw 'The relevance CSV is empty.'
    }

    $annotated = foreach ($row in $rows) {
        if ([string]::IsNullOrWhiteSpace($row.Relevant)) {
            throw "Relevance is still blank for query '$($row.QueryId)' mode '$($row.Mode)' rank '$($row.Rank)'."
        }

        [pscustomobject]@{
            QueryId = $row.QueryId
            Pair = $row.Pair
            Category = $row.Category
            Language = $row.Language
            Query = $row.Query
            Mode = $row.Mode
            Rank = [int]$row.Rank
            Relevant = Get-RelevanceValue -Value $row.Relevant
        }
    }

    $perQuery = @(
        $annotated |
            Group-Object QueryId, Mode |
            ForEach-Object {
                $group = @($_.Group)
                $relevant = ($group | Measure-Object Relevant -Sum).Sum
                [pscustomobject]@{
                    queryId = $group[0].QueryId
                    pair = $group[0].Pair
                    category = $group[0].Category
                    language = $group[0].Language
                    query = $group[0].Query
                    mode = $group[0].Mode
                    judged = $group.Count
                    relevant = [int]$relevant
                    precisionAtK = [math]::Round(([double]$relevant / $group.Count), 4)
                }
            } |
            Sort-Object queryId, mode
    )

    $byModeLanguage = @(
        $annotated |
            Group-Object Mode, Language |
            ForEach-Object {
                $group = @($_.Group)
                $relevant = ($group | Measure-Object Relevant -Sum).Sum
                [pscustomobject]@{
                    mode = $group[0].Mode
                    language = $group[0].Language
                    judged = $group.Count
                    relevant = [int]$relevant
                    precision = [math]::Round(([double]$relevant / $group.Count), 4)
                }
            } |
            Sort-Object mode, language
    )

    $measurements = Get-Content -LiteralPath $resultsPath -Raw | ConvertFrom-Json
    $status = Get-Content -LiteralPath $statusPath -Raw | ConvertFrom-Json
    $latency = @(
        $measurements.measurements |
            Group-Object mode, language |
            ForEach-Object {
                $group = @($_.Group)
                [pscustomobject]@{
                    mode = $group[0].mode
                    language = $group[0].language
                    queryCount = $group.Count
                    averageServerMilliseconds = [math]::Round((($group | Measure-Object serverMilliseconds -Average).Average), 2)
                    maximumServerMilliseconds = [math]::Round((($group | Measure-Object serverMilliseconds -Maximum).Maximum), 2)
                    averageClientMilliseconds = [math]::Round((($group | Measure-Object clientMilliseconds -Average).Average), 2)
                }
            } |
            Sort-Object mode, language
    )

    $summary = [ordered]@{
        generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        topK = $measurements.topK
        queryCount = $measurements.queryCount
        annotatedRows = $annotated.Count
        index = [ordered]@{
            currentPhotoCount = $status.currentPhotoCount
            indexedPhotoCount = $status.indexedPhotoCount
            displayableCaptionCount = $status.displayableCaptionCount
            embeddingDimensions = $status.embeddingDimensions
            rawEmbeddingBytes = $status.rawEmbeddingBytes
            averageEmbeddingGenerationMilliseconds = $status.averageEmbeddingGenerationMilliseconds
            exactVectorSearch = $status.exactVectorSearch
            annIndexUsed = $status.annIndexUsed
            modelId = $status.modelId
            modelSha256 = $status.modelSha256
        }
        qualityByModeAndLanguage = $byModeLanguage
        latencyByModeAndLanguage = $latency
        perQuery = $perQuery
    }

    $summary | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $summaryPath -Encoding utf8
    Write-Host "Summary written to $summaryPath"
    Write-Host 'Review Swedish vs English semantic precision separately before deciding whether raw Swedish CLIP queries are acceptable.'
    exit 0
}

if ($null -eq $BaseUrl) {
    throw '-BaseUrl is required unless -Summarize is used.'
}

$resolvedQuerySuite = [System.IO.Path]::GetFullPath($QuerySuite)
if (-not (Test-Path -LiteralPath $resolvedQuerySuite)) {
    throw "Query suite not found: $resolvedQuerySuite"
}

$queries = @(Get-Content -LiteralPath $resolvedQuerySuite -Raw | ConvertFrom-Json)
if ($queries.Count -le 8) {
    throw "WI-0162 requires materially more than the WI-0127 maximum of 8 text queries; the supplied suite contains $($queries.Count)."
}
if (@($queries | Where-Object language -eq 'sv').Count -eq 0 -or
    @($queries | Where-Object language -eq 'en').Count -eq 0) {
    throw 'The WI-0162 query suite must contain both Swedish (sv) and English (en) queries.'
}

$base = Normalize-BaseUrl -Value $BaseUrl
$status = Invoke-RestMethod -Method Get -Uri "$base/api/photo-search/status"
$status | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $statusPath -Encoding utf8

if (-not $status.semanticModelAvailable) {
    throw 'Semantic search model is not installed. Run .\models\Get-SemanticSearchClipModel.ps1, restart the application, and retry.'
}
if (-not $status.exactVectorSearch -or $status.annIndexUsed) {
    throw 'WI-0162 expects exact local vector search with no ANN index during this evaluation.'
}
if (-not $AllowSmallSample -and [int]$status.indexedPhotoCount -le 185) {
    throw "Only $($status.indexedPhotoCount) photos are indexed. WI-0162 must use materially more than the 185-photo WI-0127 sample. Let indexing continue or pass -AllowSmallSample only for script smoke testing."
}

$modes = @('semantic', 'caption', 'combined')
$measurements = New-Object System.Collections.Generic.List[object]
$relevanceRows = New-Object System.Collections.Generic.List[object]

foreach ($query in $queries) {
    foreach ($mode in $modes) {
        $body = @{
            query = [string]$query.query
            mode = $mode
            limit = $TopK
        } | ConvertTo-Json

        $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
        $response = Invoke-RestMethod `
            -Method Post `
            -Uri "$base/api/photo-search/query" `
            -ContentType 'application/json' `
            -Body $body
        $stopwatch.Stop()

        $items = @($response.items)
        $measurements.Add([pscustomobject]@{
            queryId = [string]$query.id
            pair = [string]$query.pair
            category = [string]$query.category
            language = [string]$query.language
            query = [string]$query.query
            mode = $mode
            serverMilliseconds = [double]$response.searchMilliseconds
            clientMilliseconds = $stopwatch.Elapsed.TotalMilliseconds
            returned = $items.Count
            items = @(
                $items | ForEach-Object {
                    [pscustomobject]@{
                        rank = [array]::IndexOf($items, $_) + 1
                        revisionId = $_.revisionId
                        sources = @($_.sources)
                        semanticScore = $_.semanticScore
                        captionScore = $_.captionScore
                    }
                }
            )
        })

        for ($rank = 0; $rank -lt $items.Count; $rank++) {
            $item = $items[$rank]
            $relevanceRows.Add([pscustomobject]@{
                QueryId = [string]$query.id
                Pair = [string]$query.pair
                Category = [string]$query.category
                Language = [string]$query.language
                Query = [string]$query.query
                Mode = $mode
                Rank = $rank + 1
                RevisionId = [string]$item.revisionId
                Sources = (@($item.sources) -join '+')
                SemanticScore = $item.semanticScore
                CaptionScore = $item.captionScore
                Relevant = ''
            })
        }
    }
}

$evidence = [ordered]@{
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    baseUrl = $base
    topK = $TopK
    queryCount = $queries.Count
    status = $status
    measurements = $measurements
}
$evidence | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $resultsPath -Encoding utf8
$relevanceRows | Export-Csv -LiteralPath $relevancePath -NoTypeInformation -Encoding utf8

Write-Host ''
Write-Host "WI-0162 collection complete: $($queries.Count) queries x $($modes.Count) modes."
Write-Host "Indexed photos       : $($status.indexedPhotoCount) / $($status.currentPhotoCount)"
Write-Host "Displayable captions : $($status.displayableCaptionCount)"
Write-Host "Raw embedding bytes  : $($status.rawEmbeddingBytes)"
Write-Host "Results evidence     : $resultsPath"
Write-Host "Relevance worksheet  : $relevancePath"
Write-Host ''
Write-Host 'Open the CSV, mark Relevant as yes/no for every returned row, then run:'
Write-Host "  .\scripts\evaluate-photo-search.ps1 -OutputDirectory `"$resolvedOutput`" -Summarize"
