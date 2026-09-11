[CmdletBinding()]
param(
    [Uri]$BaseUri = "http://localhost:5080",
    [Parameter(Mandatory = $true)]
    [Guid]$CollectionId,
    [ValidateRange(1, 10)]
    [int]$RepeatCount = 3,
    [switch]$IncludePreparedOriginals,
    [ValidateRange(1, 200)]
    [int]$MaximumPreparationItems = 5,
    [ValidateRange(5, 900)]
    [int]$PreparationTimeoutSeconds = 120,
    [string]$OutputPath
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$base = $BaseUri.AbsoluteUri.TrimEnd('/')

function Invoke-JsonRequest {
    param(
        [Parameter(Mandatory = $true)][ValidateSet("GET", "POST", "DELETE")][string]$Method,
        [Parameter(Mandatory = $true)][string]$Path,
        [object]$Body
    )

    $parameters = @{
        Method = $Method
        Uri = "$base$Path"
        UseBasicParsing = $true
    }
    if ($PSBoundParameters.ContainsKey("Body")) {
        $parameters.ContentType = "application/json"
        $parameters.Body = $Body | ConvertTo-Json -Depth 8 -Compress
    }

    $response = Invoke-WebRequest @parameters
    if ([string]::IsNullOrWhiteSpace($response.Content)) {
        return $null
    }

    return $response.Content | ConvertFrom-Json
}

function Reset-Diagnostics {
    Invoke-JsonRequest -Method POST -Path "/api/archive/diagnostics/throughput/reset" | Out-Null
}

function Get-Diagnostics {
    return Invoke-JsonRequest -Method GET -Path "/api/archive/diagnostics/throughput"
}

function Measure-ActionMilliseconds {
    param([Parameter(Mandatory = $true)][scriptblock]$Action)

    $watch = [Diagnostics.Stopwatch]::StartNew()
    & $Action
    $watch.Stop()
    return [Math]::Round($watch.Elapsed.TotalMilliseconds, 3)
}

function Measure-DownloadMilliseconds {
    param([Parameter(Mandatory = $true)][string]$Path)

    $temporary = Join-Path ([IO.Path]::GetTempPath()) ("photoidentity-slideshow-{0}.bin" -f [Guid]::NewGuid().ToString("N"))
    try {
        return Measure-ActionMilliseconds {
            Invoke-WebRequest -Method GET -Uri "$base$Path" -UseBasicParsing -OutFile $temporary | Out-Null
        }
    }
    finally {
        Remove-Item -LiteralPath $temporary -Force -ErrorAction SilentlyContinue
    }
}

function Select-DiagnosticStage {
    param(
        [Parameter(Mandatory = $true)]$Diagnostics,
        [Parameter(Mandatory = $true)][string[]]$Names
    )

    return @(
        $Diagnostics.stages |
            Where-Object { $Names -contains $_.name } |
            Select-Object name, count, totalMilliseconds, averageMilliseconds, maxMilliseconds)
}

function Select-HashReads {
    param([Parameter(Mandatory = $true)]$Diagnostics)

    return @(
        $Diagnostics.hashReads |
            Where-Object { $_.kind -in @("original-status", "original-open") } |
            Select-Object kind, count, bytes, subjectCount, averageReadsPerSubject, maxReadsPerSubject)
}

$health = Invoke-JsonRequest -Method GET -Path "/health"
if ($null -eq $health -or $health.status -ne "ok") {
    throw "Photo Identity did not report a healthy API at '$base'."
}

Reset-Diagnostics
$library = $null
$libraryMilliseconds = Measure-ActionMilliseconds {
    $script:library = Invoke-JsonRequest -Method GET -Path "/api/slideshows/collections"
}
$libraryDiagnostics = Get-Diagnostics

Reset-Diagnostics
$snapshot = $null
$snapshotMilliseconds = Measure-ActionMilliseconds {
    $script:snapshot = Invoke-JsonRequest -Method POST -Path "/api/smart-collections/$($CollectionId.ToString('D'))/slideshow-snapshot"
}
$snapshotDiagnostics = Get-Diagnostics

if ($null -eq $snapshot) {
    throw "The slideshow snapshot response was empty."
}

$total = [int]$snapshot.total
$firstRevisionId = if ($total -gt 0) { [string]$snapshot.items[0].revisionId } else { $null }
$viewerPreviewMilliseconds = @()
$viewerPreviewDiagnostics = $null
if (-not [string]::IsNullOrWhiteSpace($firstRevisionId)) {
    Reset-Diagnostics
    $encodedRevision = [Uri]::EscapeDataString($firstRevisionId)
    for ($index = 0; $index -lt $RepeatCount; $index++) {
        $viewerPreviewMilliseconds += Measure-DownloadMilliseconds -Path "/api/collections/photos/$encodedRevision/viewer-preview"
    }
    $viewerPreviewDiagnostics = Get-Diagnostics
}

$preparedResult = $null
if ($IncludePreparedOriginals) {
    if ($total -gt $MaximumPreparationItems) {
        throw "The selected slideshow contains $total items, above -MaximumPreparationItems $MaximumPreparationItems. Choose a smaller collection or raise the cap explicitly."
    }

    $sessionId = $null
    try {
        Reset-Diagnostics
        $request = @{ revisionIds = @($snapshot.items | ForEach-Object { [string]$_.revisionId }) }
        $start = Invoke-JsonRequest -Method POST -Path "/api/slideshows/original-preparation" -Body $request
        $sessionId = [string]$start.sessionId
        if ([string]::IsNullOrWhiteSpace($sessionId)) {
            throw "The original-preparation response did not contain a session identifier."
        }

        $deadline = [DateTimeOffset]::UtcNow.AddSeconds($PreparationTimeoutSeconds)
        $status = $start
        while ($status.state -eq "preparing" -and [DateTimeOffset]::UtcNow -lt $deadline) {
            Start-Sleep -Milliseconds 500
            $status = Invoke-JsonRequest -Method GET -Path "/api/slideshows/original-preparation/$sessionId"
        }

        if ($status.state -eq "preparing") {
            throw "Original preparation did not reach a terminal/ready state within $PreparationTimeoutSeconds seconds."
        }

        $preparedMilliseconds = @()
        if ($status.state -eq "ready" -and -not [string]::IsNullOrWhiteSpace($firstRevisionId)) {
            $encodedRevision = [Uri]::EscapeDataString($firstRevisionId)
            for ($index = 0; $index -lt $RepeatCount; $index++) {
                $preparedMilliseconds += Measure-DownloadMilliseconds -Path "/api/slideshows/original-preparation/$sessionId/photos/$encodedRevision/original"
            }
        }

        $preparedDiagnostics = Get-Diagnostics
        $preparedResult = [ordered]@{
            state = [string]$status.state
            total = [int]$status.total
            ready = [int]$status.ready
            repeatedOriginalMilliseconds = $preparedMilliseconds
            stages = Select-DiagnosticStage -Diagnostics $preparedDiagnostics -Names @(
                "slideshow-preparation-start",
                "slideshow-preparation-status",
                "slideshow-prepared-original-open",
                "original-verification-hash")
            hashReads = Select-HashReads -Diagnostics $preparedDiagnostics
        }
    }
    finally {
        if (-not [string]::IsNullOrWhiteSpace($sessionId)) {
            try {
                Invoke-JsonRequest -Method DELETE -Path "/api/slideshows/original-preparation/$sessionId" | Out-Null
            }
            catch {
                Write-Warning "The slideshow preparation session could not be ended cleanly. It will still expire server-side."
            }
        }
    }
}

$report = [ordered]@{
    capturedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    catalogueProvider = [string]$health.catalogueProvider
    schemaVersion = $health.schemaVersion
    collectionCount = @($library).Count
    snapshotItemCount = $total
    libraryMilliseconds = $libraryMilliseconds
    snapshotMilliseconds = $snapshotMilliseconds
    repeatedViewerPreviewMilliseconds = $viewerPreviewMilliseconds
    libraryStages = Select-DiagnosticStage -Diagnostics $libraryDiagnostics -Names @("slideshow-library-load")
    snapshotStages = Select-DiagnosticStage -Diagnostics $snapshotDiagnostics -Names @("slideshow-snapshot-creation")
    viewerPreviewStages = if ($null -eq $viewerPreviewDiagnostics) { @() } else {
        Select-DiagnosticStage -Diagnostics $viewerPreviewDiagnostics -Names @(
            "collection-viewer-preview-open",
            "original-verification-hash")
    }
    viewerPreviewHashReads = if ($null -eq $viewerPreviewDiagnostics) { @() } else {
        Select-HashReads -Diagnostics $viewerPreviewDiagnostics
    }
    preparedOriginals = $preparedResult
    privacyNote = "Collection names, revision identifiers, filenames, source paths and credentials are intentionally omitted."
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $directory = Join-Path ([IO.Path]::GetTempPath()) "PhotoIdentity\slideshow-performance"
    [IO.Directory]::CreateDirectory($directory) | Out-Null
    $OutputPath = Join-Path $directory ("slideshow-performance-{0}.json" -f (Get-Date -Format "yyyyMMdd-HHmmss"))
}
else {
    $OutputPath = [IO.Path]::GetFullPath($OutputPath)
    $directory = Split-Path -Parent $OutputPath
    if (-not [string]::IsNullOrWhiteSpace($directory)) {
        [IO.Directory]::CreateDirectory($directory) | Out-Null
    }
}

$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutputPath -Encoding UTF8
$report | ConvertTo-Json -Depth 8
Write-Host "Slideshow performance report: $OutputPath"
