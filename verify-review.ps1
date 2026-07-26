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

if (-not (Test-Path -LiteralPath $toolAssembly -PathType Leaf)) {
    throw "Review verification tool was not built: $toolAssembly"
}
if (-not (Test-Path -LiteralPath $apiProject -PathType Leaf)) {
    throw "Review API project was not found: $apiProject"
}

New-Item -ItemType Directory -Path $artifactDirectory -Force | Out-Null
$manifestText = & dotnet $toolAssembly --output $artifactDirectory
if ($LASTEXITCODE -ne 0) {
    throw "Review verification fixture preparation failed with exit code $LASTEXITCODE."
}
$manifest = ($manifestText -join [Environment]::NewLine) | ConvertFrom-Json

$report = [ordered]@{
    schemaVersion = 1
    result = "prepared"
    mode = $Mode
    generatedAtUtc = [DateTime]::UtcNow.ToString("O")
    databasePath = $manifest.DatabasePath
    artifactDirectory = $manifest.ArtifactDirectory
    faceCount = $manifest.FaceCount
    localUrl = "http://localhost:$Port"
    lanUrls = @(Get-LanUrls -SelectedPort $Port)
    smoke = [ordered]@{
        health = "not_run"
        gallery = "not_run"
        hostedClient = "not_run"
        image = "not_run"
        mutation = "not_run"
        cacheControl = "not_run"
    }
    manualVerificationRequired = ($Mode -eq "Interactive")
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
    $report.smoke.health = "passed"

    $clientResponse = Invoke-WebRequest -Uri "$baseUrl/" -UseBasicParsing -TimeoutSec 10
    if ($clientResponse.StatusCode -ne 200 -or
        $clientResponse.Content.IndexOf("blazor.webassembly.js", [StringComparison]::OrdinalIgnoreCase) -lt 0) {
        throw "Hosted Blazor client was not served from the published application."
    }
    $report.smoke.hostedClient = "passed"

    $galleryResponse = Invoke-WebRequest -Uri "$baseUrl/api/review/faces?state=all" `
        -UseBasicParsing -TimeoutSec 10
    $gallery = $galleryResponse.Content | ConvertFrom-Json
    if ($gallery.Total -ne $manifest.FaceCount -or @($gallery.Items).Count -ne $manifest.FaceCount) {
        throw "Review gallery did not return the prepared synthetic faces."
    }
    $report.smoke.gallery = "passed"

    $galleryCache = [string]$galleryResponse.Headers["Cache-Control"]
    if ($galleryCache.IndexOf("no-store", [StringComparison]::OrdinalIgnoreCase) -lt 0) {
        throw "Review gallery response did not include Cache-Control: no-store."
    }

    $unreviewedFaces = @($gallery.Items | Where-Object { $_.state -eq "unreviewed" })
    if ($unreviewedFaces.Count -eq 0) {
        throw "Review verification catalogue did not contain an unreviewed face."
    }
    $firstFace = $unreviewedFaces[0]

    $imageResponse = Invoke-WebRequest -Uri "$baseUrl$($firstFace.ImageUrl)" `
        -UseBasicParsing -TimeoutSec 10
    if ($imageResponse.StatusCode -ne 200 -or $imageResponse.RawContentLength -le 0) {
        throw "Review face image did not return content."
    }
    $imageCache = [string]$imageResponse.Headers["Cache-Control"]
    if ($imageCache.IndexOf("no-store", [StringComparison]::OrdinalIgnoreCase) -lt 0) {
        throw "Review image response did not include Cache-Control: no-store."
    }
    $report.smoke.image = "passed"
    $report.smoke.cacheControl = "passed"

    $personBody = @{ displayName = "Verification Person" } | ConvertTo-Json
    $person = Invoke-RestMethod -Method Post -Uri "$baseUrl/api/review/people" `
        -ContentType "application/json" -Body $personBody -TimeoutSec 10
    $assignBody = @{
        personId = $person.id
        actor = "verification:smoke"
        note = "Automated assignment followed by undo."
    } | ConvertTo-Json
    Invoke-RestMethod -Method Post -Uri "$baseUrl/api/review/faces/$($firstFace.id)/assign" `
        -ContentType "application/json" -Body $assignBody -TimeoutSec 10 | Out-Null
    $undoBody = @{
        actor = "verification:smoke"
        note = "Automated undo confirms reversibility."
    } | ConvertTo-Json
    Invoke-RestMethod -Method Post -Uri "$baseUrl/api/review/faces/$($firstFace.id)/undo" `
        -ContentType "application/json" -Body $undoBody -TimeoutSec 10 | Out-Null
    $details = Invoke-RestMethod -Uri "$baseUrl/api/review/faces/$($firstFace.id)" -TimeoutSec 10
    if (@($details.actions).Count -lt 2 -or $details.face.state -ne "unreviewed") {
        throw "Review assignment and undo did not restore the unreviewed state with audit history."
    }
    $report.smoke.mutation = "passed"
    $report.result = "passed"

    $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportPath -Encoding UTF8

    Write-Host "`nReview verification smoke checks passed."
    Write-Host "Windows URL: $($report.localUrl)"
    foreach ($url in $report.lanUrls) {
        Write-Host "Pixel/LAN URL: $url"
    }
    Write-Warning "No firewall rule was created. Permit TCP $Port only on the intended private network profile."
    Write-Warning "The review listener is unauthenticated HTTP; do not expose it to an untrusted network."
    Write-Host "`nManual checklist:"
    Write-Host "  1. Confirm the gallery has no horizontal page scrolling."
    Write-Host "  2. Create a person, assign a face, reject another, and undo one action."
    Write-Host "  3. Restart the real catalogue host and confirm decisions persist."
    Write-Host "  4. Open details and confirm no local filesystem path is displayed."
    Write-Host "  5. On Pixel, confirm Assign, Reject, Undo and Back are comfortable to tap."
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
