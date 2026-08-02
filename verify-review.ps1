<#
.SYNOPSIS
Prepares and verifies the local review application with synthetic data.

.DESCRIPTION
Builds the solution, creates a disposable SQLite catalogue with synthetic coloured
face crops, publishes and starts the review application, and performs privacy and
mutation smoke checks. Interactive mode prints local and LAN URLs for optional
browser inspection. The script never changes a real catalogue and never creates
firewall rules.

.EXAMPLE
./verify-review.ps1

.EXAMPLE
./verify-review.ps1 -Mode Smoke -Configuration Release

.EXAMPLE
./verify-review.ps1 -Mode Prepare -SkipBuild
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

foreach ($requiredFile in @($toolAssembly, $apiProject, $smokeScript)) {
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
    schemaVersion = 4
    result = "prepared"
    mode = $Mode
    generatedAtUtc = [DateTime]::UtcNow.ToString("O")
    databasePath = $manifest.DatabasePath
    artifactDirectory = $manifest.ArtifactDirectory
    faceCount = $manifest.FaceCount
    localUrl = "http://localhost:$Port"
    lanUrls = @(Get-LanUrls -SelectedPort $Port)
    smoke = $notRunSmoke
    usesDisposableCatalogue = $true
    createsFirewallRule = $false
}

if ($Mode -eq "Prepare") {
    $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportPath -Encoding UTF8
    Write-Host "Prepared synthetic review catalogue: $($manifest.DatabasePath)"
    Write-Host "Report: $reportPath"
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
    $report.result = "passed"
    $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportPath -Encoding UTF8

    Write-Host "`nReview application verification passed."
    Write-Host "Mode: $Mode"
    Write-Host "Windows/local URL: $($report.localUrl)"
    foreach ($url in $report.lanUrls) {
        Write-Host "Trusted-LAN URL: $url"
    }
    Write-Warning "No firewall rule was created. Permit TCP $Port only on the intended private network profile."
    Write-Warning "The review listener is unauthenticated HTTP; do not expose it to an untrusted network."
    Write-Host "The synthetic fixture and generated logs remain below .artifacts/review-verification."
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
