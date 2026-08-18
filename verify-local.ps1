<#
.SYNOPSIS
Runs the local Windows verification checkpoint.

.DESCRIPTION
Builds and tests the solution, validates living documentation, optionally installs
and verifies pinned models, then checks private JPEG and PNG files without
modifying them. Multiple image paths must be supplied as one array value.

By default the repository build, automated tests and documentation validation all
run. Callers that already completed those checks for the same checkout can opt out
of repeating them with -SkipBuild, -SkipTests and -SkipDocumentation.

.EXAMPLE
./verify-local.ps1 -InstallModels

.EXAMPLE
./verify-local.ps1 `
  -Image "C:\PrivateVerification\normal.jpg","C:\PrivateVerification\pixel-rotated.jpg","C:\PrivateVerification\sample.png" `
  -UnsupportedImage "C:\PrivateVerification\sample.heic"

.EXAMPLE
./verify-local.ps1 `
  -Image @(
    "C:\PrivateVerification\normal.jpg"
    "C:\PrivateVerification\pixel-rotated.jpg"
    "C:\PrivateVerification\sample.png"
  ) `
  -UnsupportedImage "C:\PrivateVerification\sample.heic"

.EXAMPLE
./verify-local.ps1 `
  -SkipBuild `
  -SkipTests `
  -SkipDocumentation `
  -SkipModels `
  -Image ".artifacts/ci-verification/valid.png"
#>
[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Release",

    [switch] $SkipBuild,

    [switch] $SkipTests,

    [switch] $SkipDocumentation,

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
    build = if ($SkipBuild) { "skipped" } else { "pending" }
    tests = if ($SkipTests) { "skipped" } else { "pending" }
    documentation = if ($SkipDocumentation) { "skipped" } else { "pending" }
    modelsSkipped = [bool] $SkipModels
    models = @()
    manualImagesProvided = (($Image.Count + $UnsupportedImage.Count) -gt 0)
    decoderChecks = @()
    unsupportedChecks = @()
}

$exitCode = 0
$cliAssemblyPath = Join-Path $root "src/PhotoIdentity.Cli/bin/$Configuration/net10.0/PhotoIdentity.Cli.dll"

function Invoke-CommandCapture {
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

    $previousErrorActionPreference = $ErrorActionPreference
    $nativePreferenceExists = Test-Path variable:PSNativeCommandUseErrorActionPreference
    if ($nativePreferenceExists) {
        $previousNativePreference = $PSNativeCommandUseErrorActionPreference
    }

    $commandOutput = @()
    $commandSucceeded = $false
    $nativeExitCode = 1

    try {
        # Native tools may legitimately write diagnostics to stderr while returning a
        # structured exit code. Capture that output and decide from the exit code below.
        $ErrorActionPreference = "Continue"
        if ($nativePreferenceExists) {
            $PSNativeCommandUseErrorActionPreference = $false
        }

        $global:LASTEXITCODE = 0
        if ($PSCmdlet.ParameterSetName -eq "Parameters") {
            $commandOutput = & $FilePath @Parameters 2>&1
        }
        else {
            $commandOutput = & $FilePath @ArgumentList 2>&1
        }

        $commandSucceeded = $?
        $nativeExitCode = $global:LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
        if ($nativePreferenceExists) {
            $PSNativeCommandUseErrorActionPreference = $previousNativePreference
        }
    }

    $textOutput = @($commandOutput | ForEach-Object { $_.ToString() })
    $textOutput | ForEach-Object { Write-Host $_ }

    $isPowerShellScript = [IO.Path]::GetExtension($FilePath) -ieq ".ps1"
    $effectiveSuccess = if ($isPowerShellScript) {
        $commandSucceeded -and $nativeExitCode -eq 0
    }
    else {
        # Native tools can write recoverable warnings to stderr while returning zero.
        # The exit code is authoritative for executable success.
        $nativeExitCode -eq 0
    }

    return [pscustomobject]@{
        Succeeded = $effectiveSuccess
        ExitCode = $nativeExitCode
        Output = $textOutput
    }
}

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

    if ($PSCmdlet.ParameterSetName -eq "Parameters") {
        $result = Invoke-CommandCapture -Name $Name -FilePath $FilePath -Parameters $Parameters
    }
    else {
        $result = Invoke-CommandCapture -Name $Name -FilePath $FilePath -ArgumentList $ArgumentList
    }

    if (-not $result.Succeeded -or $result.ExitCode -ne 0) {
        throw "$Name failed with exit code $($result.ExitCode)."
    }

    return $result.Output
}

function Get-InputHash {
    param(
        [Parameter(Mandatory)]
        [string] $Path
    )

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
}

function Test-TextContainsOrdinal {
    param(
        [AllowEmptyString()]
        [string] $Text,

        [Parameter(Mandatory)]
        [string] $Value
    )

    return $Text.IndexOf($Value, [StringComparison]::Ordinal) -ge 0
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
    Remove-Item -LiteralPath $outputPath, $caseReportPath -Force -ErrorAction SilentlyContinue

    $arguments = @(
        $cliAssemblyPath,
        "decode",
        "--input", $InputPath,
        "--output", $outputPath,
        "--report", $caseReportPath
    )

    if ($MaxWidth -gt 0) {
        $arguments += @("--max-width", $MaxWidth.ToString(), "--max-height", $MaxHeight.ToString())
    }

    $hashBefore = Get-InputHash -Path $InputPath
    $command = Invoke-CommandCapture `
        -Name ("Decode check {0}" -f $Index) `
        -FilePath "dotnet" `
        -ArgumentList $arguments
    $hashAfter = Get-InputHash -Path $InputPath
    $inputUnchanged = [string]::Equals($hashBefore, $hashAfter, [StringComparison]::OrdinalIgnoreCase)

    if ($command.ExitCode -eq 0) {
        if (-not (Test-Path -LiteralPath $caseReportPath -PathType Leaf)) {
            return [ordered]@{
                case = ("image-{0:D3}" -f $Index)
                result = "failed"
                failure = "missing_case_report"
                exitCode = 0
                inputUnchanged = $inputUnchanged
            }
        }

        $caseReport = Get-Content -LiteralPath $caseReportPath -Raw | ConvertFrom-Json
        return [ordered]@{
            case = ("image-{0:D3}" -f $Index)
            result = $caseReport.result
            sourceType = $caseReport.sourceType
            width = $caseReport.width
            height = $caseReport.height
            pixelFormat = $caseReport.pixelFormat
            inputUnchanged = ($inputUnchanged -and $caseReport.inputUnchanged)
            outputFileName = $caseReport.outputFileName
            exitCode = 0
        }
    }

    $diagnostics = $command.Output -join "`n"
    $failure = if ($command.ExitCode -eq 3 -or (Test-TextContainsOrdinal -Text $diagnostics -Value "unsupported-format:")) {
        "unsupported_format"
    }
    elseif ($command.ExitCode -eq 4 -or (Test-TextContainsOrdinal -Text $diagnostics -Value "corrupt-media:")) {
        "corrupt_media"
    }
    else {
        "execution_error"
    }

    return [ordered]@{
        case = ("image-{0:D3}" -f $Index)
        result = "failed"
        failure = $failure
        exitCode = $command.ExitCode
        inputUnchanged = $inputUnchanged
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
    Remove-Item -LiteralPath $outputPath -Force -ErrorAction SilentlyContinue

    $hashBefore = Get-InputHash -Path $InputPath
    $command = Invoke-CommandCapture `
        -Name ("Unsupported-format check {0}" -f $Index) `
        -FilePath "dotnet" `
        -ArgumentList @(
            $cliAssemblyPath,
            "decode",
            "--input", $InputPath,
            "--output", $outputPath
        )
    $hashAfter = Get-InputHash -Path $InputPath
    $inputUnchanged = [string]::Equals($hashBefore, $hashAfter, [StringComparison]::OrdinalIgnoreCase)
    $diagnostics = $command.Output -join "`n"
    $unsupportedResult = $command.ExitCode -eq 3 -or (Test-TextContainsOrdinal -Text $diagnostics -Value "unsupported-format:")
    $passed = $unsupportedResult -and $inputUnchanged -and -not (Test-Path -LiteralPath $outputPath)

    return [ordered]@{
        case = ("unsupported-{0:D3}" -f $Index)
        result = if ($passed) { "passed" } else { "failed" }
        expectedExitCode = 3
        actualExitCode = $command.ExitCode
        inputUnchanged = $inputUnchanged
    }
}

try {
    $dotnetVersionOutput = Invoke-CheckedCommand -Name ".NET SDK" -FilePath "dotnet" -ArgumentList @("--version")
    $report.dotnetVersion = ($dotnetVersionOutput | Select-Object -Last 1).Trim()

    if ($SkipBuild) {
        Write-Host "`n== Restore and build =="
        Write-Host "Skipped; caller is reusing an existing build for this checkout."
    }
    else {
        Invoke-CheckedCommand `
            -Name "Restore and build" `
            -FilePath (Join-Path $root "build.ps1") `
            -Parameters @{ Configuration = $Configuration } | Out-Null
        $report.build = "passed"
    }

    if ($SkipTests) {
        Write-Host "`n== Automated tests =="
        Write-Host "Skipped; caller already ran the automated test suite for this checkout."
    }
    else {
        Invoke-CheckedCommand `
            -Name "Automated tests" `
            -FilePath (Join-Path $root "test.ps1") `
            -Parameters @{ Configuration = $Configuration } | Out-Null
        $report.tests = "passed"
    }

    if ($SkipDocumentation) {
        Write-Host "`n== Documentation validation =="
        Write-Host "Skipped; caller already validated living and generated documentation for this checkout."
    }
    else {
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
    }

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

    if (($Image.Count + $UnsupportedImage.Count) -gt 0 -and -not (Test-Path -LiteralPath $cliAssemblyPath -PathType Leaf)) {
        throw "Manual media checks require the built PhotoIdentity.Cli assembly at '$cliAssemblyPath'. Run the build first or omit -SkipBuild."
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

    $failedDecodeChecks = @($decodeChecks | Where-Object { $_.result -ne "passed" }).Count
    $failedUnsupportedChecks = @($unsupportedChecks | Where-Object { $_.result -ne "passed" }).Count

    if (($failedDecodeChecks + $failedUnsupportedChecks) -gt 0) {
        $exitCode = 1
        $report.result = "failed"
        $report.error = "One or more manual media checks failed. Review decoderChecks and unsupportedChecks."
    }
    elseif (($Image.Count + $UnsupportedImage.Count) -gt 0) {
        $report.result = "passed"
    }
    else {
        $report.result = "passed_automated_checks"
    }
}
catch {
    $exitCode = 1
    $report.result = "failed"
    $report.error = "A repository verification step failed. Review the console output."
    Write-Error $_
}
finally {
    $report.generatedAtUtc = [DateTime]::UtcNow.ToString("O")
    $report | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $reportPath -Encoding utf8
    Write-Host "`nVerification report: $reportPath"
}

exit $exitCode
