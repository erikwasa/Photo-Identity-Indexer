<#
.SYNOPSIS
Prepares and verifies the local review application with synthetic data.

.DESCRIPTION
Builds the solution, creates a disposable SQLite catalogue with synthetic coloured
face crops, publishes and starts the review application, performs privacy and
mutation smoke checks, and prints local/LAN URLs for Windows and Pixel verification.
It never changes a real catalogue and never creates firewall rules.

.EXAMPLE
./verify-review.ps1

.EXAMPLE
./verify-review.ps1 -Mode Smoke -Configuration Release
#>
[CmdletBinding()]
param(
    [ValidateSet("Interactive", "Smoke", "Prepare")]
    [string] $Mode = "Interactive",

    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Release",

    [ValidateRange(1024, 65535)]
    [int] $Port = 5080,

    [string] $ListenAddress = "0.0.0.0",

    [switch] $SkipBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$root = $PSScriptRoot
$artifactDirectory = Join-Path $root ".artifacts/review-verification"
$reportPath = Join-Path $artifactDirectory "verification-report.json"
$stdoutPath = Join-Path $artifactDirectory "api.stdout.log"
$stderrPath = Join-Path $artifactDirectory "api.stderr.log"
$toolAssembly = Join-Path $root "tools/PhotoIdentity.ReviewVerification/bin/$Configuration/net10.0/PhotoIdentity.ReviewVerification.dll"
$smokeScript = Join-Path $root "tools/PhotoIdentity.ReviewVerification/Invoke-PublishedReviewSmoke.ps1"
$sessionScript = Join-Path $root "record-review-session.ps1"
$manualGuide = Join-Path $root "docs/delivery/verification/WI-0033-manual-verification.md"
$apiProject = Join-Path $root "src/PhotoIdentity.Api/PhotoIdentity.Api.csproj"
$publishedApiDirectory = Join-Path $artifactDirectory "app"
$apiAssembly = Join-Path $publishedApiDirectory "PhotoIdentity.Api.dll"

function Invoke-CheckedNative {
    param(
        [Parameter(Mandatory)] [string] $FilePath,
        [Parameter(Mandatory)] [string[]] $ArgumentList
    )

    & $FilePath @ArgumentList
    if ($LASTEXITCODE -ne 0) {
        throw "$FilePath failed with exit code $LASTEXITCODE."
    }
}

function Get-LanUrls {
    param([int] $SelectedPort)

    $addresses = [System.Net.Dns]::GetHostAddresses([System.Net.Dns]::GetHostName()) |
        Where-Object {
            $_.AddressFamily -eq [System.Net.Sockets.AddressFamily]::InterNetwork -and
            -not [System.Net.IPAddress]::IsLoopback($_)
        } |
        ForEach-Object { $_.ToString() } |
        Sort-Object -Unique

    return @($addresses | ForEach-Object { "http://$($_):$SelectedPort" })
}

if (-not $SkipBuild) {
    Invoke-CheckedNative -FilePath "dotnet" -ArgumentList @(
        "build", (Join-Path $root "PhotoIdentity.slnx"),
        "--configuration", $Configuration
    )
}

foreach ($requiredFile in @($toolAssembly, $apiProject, $smokeScript, $sessionScript, $manualGuide)) {
    if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) {
        throw "Required review verification file was not found: $requiredFile"
    }
}

New-Item -ItemType Directory -Path $artifactDirectory -Force | Out-Null
$manifestText = & dotnet $toolAssembly --output $artifactDirectory
if ($LASTEXITCODE -ne 0) {
    throw "Review verification fixture preparation failed with exit code $LASTEXITCODE."
}
$manifest = ($manifestText -join [Environment]::NewLine) | ConvertFrom-Json

$notRunSmoke = [ordered]@{
    health = "not_run"
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
$report = [ordered]@{
    schemaVersion = 3
    result = "prepared"
    mode = $Mode
    generatedAtUtc = [DateTime]::UtcNow.ToString("O")
    databasePath = $manifest.DatabasePath
    artifactDirectory = $manifest.ArtifactDirectory
    faceCount = $manifest.FaceCount
    localUrl = "http://localhost:$Port"
    lanUrls = @(Get-LanUrls -SelectedPort $Port)
    smoke = $notRunSmoke
    manualVerificationGuide = "docs/delivery/verification/WI-0033-manual-verification.md"
    manualSessionReporter = "record-review-session.ps1"
    manualVerificationRequired = ($Mode -eq "Interactive")
}

if ($Mode -eq "Prepare") {
    $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportPath -Encoding UTF8
    Write-Host "Prepared synthetic review catalogue: $($manifest.DatabasePath)"
    Write-Host "Report: $reportPath"
    Write-Host "Manual guide: $manualGuide"
    exit 0
}

Invoke-CheckedNative -FilePath "dotnet" -ArgumentList @(
    "publish", $apiProject,
    "--configuration", $Configuration,
    "--no-build",
    "--output", $publishedApiDirectory
)
if (-not (Test-Path -LiteralPath $apiAssembly -PathType Leaf)) {
    throw "Published review API was not found: $apiAssembly"
}
if (-not (Test-Path -LiteralPath (Join-Path $publishedApiDirectory "wwwroot/index.html") -PathType Leaf)) {
    throw "Published review client was not found below $publishedApiDirectory."
}

$previousDatabasePath = $env:PhotoIdentity__DatabasePath
$process = $null
try {
    $env:PhotoIdentity__DatabasePath = $manifest.DatabasePath
    Remove-Item -LiteralPath $stdoutPath, $stderrPath -Force -ErrorAction SilentlyContinue
    $argumentString = ('"{0}" --urls "http://{1}:{2}"' -f $apiAssembly, $ListenAddress, $Port)
    $process = Start-Process -FilePath "dotnet" -ArgumentList $argumentString -PassThru `
        -WorkingDirectory $publishedApiDirectory `
        -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath

    $baseUrl = "http://127.0.0.1:$Port"
    $healthUrl = "$baseUrl/health"
    $ready = $false
    for ($attempt = 0; $attempt -lt 120; $attempt++) {
        if ($process.HasExited) {
            throw "Review API exited before it became ready. See $stderrPath"
        }
        try {
            $health = Invoke-WebRequest -Uri $healthUrl -UseBasicParsing -TimeoutSec 2
            if ($health.StatusCode -eq 200) {
                $ready = $true
                break
            }
        }
        catch {
            Start-Sleep -Milliseconds 500
        }
    }
    if (-not $ready) {
        throw "Review API did not become ready at $healthUrl."
    }

    $report.smoke = & $smokeScript -BaseUrl $baseUrl -Manifest $manifest

    if ($Mode -eq "Smoke") {
        $selfTestRelativeDirectory = ".artifacts/review-verification/session-reporter-self-test"
        $selfTestDirectory = Join-Path $root $selfTestRelativeDirectory
        Remove-Item -LiteralPath $selfTestDirectory -Recurse -Force -ErrorAction SilentlyContinue
        & $sessionScript -Device Windows -FacesReviewed 50 -ActiveMinutes 10 `
            -AcceptedSuggestions 40 -ExplicitActions 50 -GalleryReturns 0 -ImmediateUndos 1 `
            -Notes "Automated session reporter self-test." -OutputDirectory $selfTestRelativeDirectory
        & $sessionScript -Device Pixel -FacesReviewed 50 -ActiveMinutes 12.5 `
            -AcceptedSuggestions 40 -ExplicitActions 52 -GalleryReturns 0 -ImmediateUndos 1 `
            -Notes "Automated session reporter self-test." -OutputDirectory $selfTestRelativeDirectory
        $selfTestSummaryPath = Join-Path $selfTestDirectory "manual-verification-summary.json"
        if (-not (Test-Path -LiteralPath $selfTestSummaryPath -PathType Leaf)) {
            throw "The manual session reporter did not create a two-device summary."
        }
        $selfTestSummary = Get-Content -LiteralPath $selfTestSummaryPath -Raw | ConvertFrom-Json
        if ($selfTestSummary.result -ne "passed" -or @($selfTestSummary.devices).Count -ne 2) {
            throw "The manual session reporter self-test summary was invalid."
        }
        Remove-Item -LiteralPath $selfTestDirectory -Recurse -Force
    }

    $report.result = "passed"
    $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportPath -Encoding UTF8

    Write-Host "`nReview verification smoke checks passed."
    Write-Host "Windows URL: $($report.localUrl)"
    foreach ($url in $report.lanUrls) {
        Write-Host "Pixel/LAN URL: $url"
    }
    Write-Warning "No firewall rule was created. Permit TCP $Port only on the intended private network profile."
    Write-Warning "The review listener is unauthenticated HTTP; do not expose it to an untrusted network."
    Write-Host "`nBefore closing WI-0033:"
    Write-Host "  1. Follow $manualGuide on Windows and Pixel."
    Write-Host "  2. Use a like-for-like fresh 50-100-face queue for each device run."
    Write-Host "  3. Record each result with $sessionScript; reports stay below .artifacts."
    Write-Host "  4. Do not mark touch usability or throughput accepted from automated smoke alone."
    Write-Host "Report: $reportPath"

    if ($Mode -eq "Interactive") {
        Read-Host "Press Enter to stop the disposable review server" | Out-Null
    }
}
catch {
    $report.result = "failed"
    $report.failure = $_.Exception.Message
    New-Item -ItemType Directory -Path $artifactDirectory -Force | Out-Null
    $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportPath -Encoding UTF8
    throw
}
finally {
    if ($null -ne $process -and -not $process.HasExited) {
        Stop-Process -Id $process.Id -Force
        $process.WaitForExit()
    }
    $env:PhotoIdentity__DatabasePath = $previousDatabasePath
}
