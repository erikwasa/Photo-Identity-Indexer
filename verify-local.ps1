[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Release",

    [switch] $InstallModels,

    [switch] $SkipModels,

    [Alias("Images")]
    [string[]] $Image = @(),

    [string[]] $UnsupportedImage = @(),

    [ValidateRange(0, 100000)]
    [int] $MaxWidth = 0,

    [ValidateRange(0, 100000)]
    [int] $MaxHeight = 0
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if (($MaxWidth -eq 0) -ne ($MaxHeight -eq 0)) {
    throw "MaxWidth and MaxHeight must both be zero or both be positive."
}

if ($InstallModels -and $SkipModels) {
    throw "InstallModels and SkipModels cannot be used together."
}

$root = $PSScriptRoot
$artifactDirectory = Join-Path $root ".artifacts/local-verification"
$reportPath = Join-Path $artifactDirectory "verification-report.json"
New-Item -ItemType Directory -Path $artifactDirectory -Force | Out-Null

$report = [ordered]@{
    schemaVersion = 1
    result = "running"
    generatedAtUtc = [DateTime]::UtcNow.ToString("O")
    dotnetVersion = $null
    operatingSystem = [System.Runtime.InteropServices.RuntimeInformation]::OSDescription
    configuration = $Configuration
    build = "pending"
    tests = "pending"
    documentation = "pending"
    modelsSkipped = [bool] $SkipModels
    models = @()
    manualImagesProvided = ($Image.Count -gt 0)
    decoderChecks = @()
    unsupportedChecks = @()
}

$exitCode = 0

function Invoke-CheckedCommand {
    [CmdletBinding(DefaultParameterSetName = "ArgumentList")]
    param(
        [Parameter(Mandatory)]
        [string] $Name,

        [Parameter(Mandatory)]
        [string] $FilePath,

        [Parameter(ParameterSetName = "ArgumentList")]
        [string[]] $ArgumentList = @(),

        [Parameter(ParameterSetName = "Parameters")]
        [hashtable] $Parameters = @{}
    )

    Write-Host "`n== $Name =="

    $LASTEXITCODE = 0
    if ($PSCmdlet.ParameterSetName -eq "Parameters") {
        $commandOutput = & $FilePath @Parameters 2>&1
    }
    else {
        $commandOutput = & $FilePath @ArgumentList 2>&1
    }

    $commandSucceeded = $?
    $nativeExitCode = $LASTEXITCODE
    $commandOutput | ForEach-Object { Write-Host $_ }

    if (-not $commandSucceeded -or $nativeExitCode -ne 0) {
        throw "$Name failed with exit code $nativeExitCode."
    }

    return @($commandOutput | ForEach-Object { $_.ToString() })
}

function Invoke-DecodeCheck {
    param(
        [Parameter(Mandatory)]
        [string] $InputPath,

        [Parameter(Mandatory)]
        [int] $Index
    )

    if (-not (Test-Path -LiteralPath $InputPath -PathType Leaf)) {
        throw "Decode check $Index does not reference an existing file."
    }

    $outputPath = Join-Path $artifactDirectory ("decoded-{0:D3}.png" -f $Index)
    $caseReportPath = Join-Path $artifactDirectory ("decoded-{0:D3}.json" -f $Index)
    $arguments = @(
        "run",
        "--project", (Join-Path $root "src/PhotoIdentity.Cli"),
        "--configuration", $Configuration,
        "--no-build",
        "--",
        "decode",
        "--input", $InputPath,
        "--output", $outputPath,
        "--report", $caseReportPath
    )

    if ($MaxWidth -gt 0) {
        $arguments += @("--max-width", $MaxWidth.ToString(), "--max-height", $MaxHeight.ToString())
    }

    Invoke-CheckedCommand -Name ("Decode check {0}" -f $Index) -FilePath "dotnet" -ArgumentList $arguments | Out-Null
    $caseReport = Get-Content -LiteralPath $caseReportPath -Raw | ConvertFrom-Json

    return [ordered]@{
        case = ("image-{0:D3}" -f $Index)
        result = $caseReport.result
        sourceType = $caseReport.sourceType
        width = $caseReport.width
        height = $caseReport.height
        pixelFormat = $caseReport.pixelFormat
        inputUnchanged = $caseReport.inputUnchanged
        outputFileName = $caseReport.outputFileName
    }
}

function Invoke-UnsupportedCheck {
    param(
        [Parameter(Mandatory)]
        [string] $InputPath,

        [Parameter(Mandatory)]
        [int] $Index
    )

    if (-not (Test-Path -LiteralPath $InputPath -PathType Leaf)) {
        throw "Unsupported-format check $Index does not reference an existing file."
    }

    $outputPath = Join-Path $artifactDirectory ("unsupported-{0:D3}.png" -f $Index)
    Write-Host "`n== Unsupported-format check $Index =="
    & dotnet run `
        --project (Join-Path $root "src/PhotoIdentity.Cli") `
        --configuration $Configuration `
        --no-build `
        -- `
        decode `
        --input $InputPath `
        --output $outputPath
    $nativeExitCode = $LASTEXITCODE

    if ($nativeExitCode -ne 3) {
        throw "Unsupported-format check $Index returned exit code $nativeExitCode instead of 3."
    }

    return [ordered]@{
        case = ("unsupported-{0:D3}" -f $Index)
        result = "passed"
        expectedExitCode = 3
        actualExitCode = $nativeExitCode
    }
}

try {
    $dotnetVersionOutput = Invoke-CheckedCommand -Name ".NET SDK" -FilePath "dotnet" -ArgumentList @("--version")
    $report.dotnetVersion = ($dotnetVersionOutput | Select-Object -Last 1).Trim()

    Invoke-CheckedCommand `
        -Name "Restore and build" `
        -FilePath (Join-Path $root "build.ps1") `
        -Parameters @{ Configuration = $Configuration } | Out-Null
    $report.build = "passed"

    Invoke-CheckedCommand `
        -Name "Automated tests" `
        -FilePath (Join-Path $root "test.ps1") `
        -Parameters @{ Configuration = $Configuration } | Out-Null
    $report.tests = "passed"

    Invoke-CheckedCommand `
        -Name "Living-document validation" `
        -FilePath "dotnet" `
        -ArgumentList @(
            "run", "--project", (Join-Path $root "tools/PhotoIdentity.Docs"),
            "--configuration", $Configuration,
            "--no-build", "--", "validate"
        ) | Out-Null

    Invoke-CheckedCommand `
        -Name "Generated-document consistency" `
        -FilePath "dotnet" `
        -ArgumentList @(
            "run", "--project", (Join-Path $root "tools/PhotoIdentity.Docs"),
            "--configuration", $Configuration,
            "--no-build", "--", "generate", "--check"
        ) | Out-Null
    $report.documentation = "passed"

    if (-not $SkipModels) {
        $modelList = Invoke-CheckedCommand `
            -Name "Model manifests" `
            -FilePath "dotnet" `
            -ArgumentList @(
                "run", "--project", (Join-Path $root "tools/PhotoIdentity.Models"),
                "--configuration", $Configuration,
                "--no-build", "--", "list", "--root", $root
            )

        if ($InstallModels) {
            Invoke-CheckedCommand `
                -Name "Model installation" `
                -FilePath (Join-Path $root "models/install-models.ps1") | Out-Null
        }

        Invoke-CheckedCommand `
            -Name "Model verification" `
            -FilePath "dotnet" `
            -ArgumentList @(
                "run", "--project", (Join-Path $root "tools/PhotoIdentity.Models"),
                "--configuration", $Configuration,
                "--no-build", "--", "verify", "--root", $root
            ) | Out-Null

        $report.models = @(
            $modelList |
                Where-Object { $_ -match "\t" } |
                ForEach-Object {
                    $modelId = ($_ -split "\t")[0]
                    [ordered]@{
                        id = $modelId
                        installed = $true
                        verified = $true
                    }
                }
        )
    }

    $decodeChecks = @()
    for ($index = 0; $index -lt $Image.Count; $index++) {
        $decodeChecks += Invoke-DecodeCheck -InputPath $Image[$index] -Index ($index + 1)
    }
    $report.decoderChecks = $decodeChecks

    $unsupportedChecks = @()
    for ($index = 0; $index -lt $UnsupportedImage.Count; $index++) {
        $unsupportedChecks += Invoke-UnsupportedCheck -InputPath $UnsupportedImage[$index] -Index ($index + 1)
    }
    $report.unsupportedChecks = $unsupportedChecks

    $report.result = if ($Image.Count -gt 0) {
        "passed"
    }
    else {
        "passed_automated_checks"
    }
}
catch {
    $exitCode = 1
    $report.result = "failed"
    $report.error = "A verification step failed. Review the console output."
    Write-Error $_
}
finally {
    $report.generatedAtUtc = [DateTime]::UtcNow.ToString("O")
    $report | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $reportPath -Encoding utf8
    Write-Host "`nVerification report: $reportPath"
}

exit $exitCode
