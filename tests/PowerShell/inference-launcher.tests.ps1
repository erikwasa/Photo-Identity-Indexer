# No PostgreSQL, media, Ollama, Azure or model downloads.
# Windows PowerShell 5.1: .\tests\PowerShell\inference-launcher.tests.ps1
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$repo = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$launcher = Join-Path $repo "Start-PhotoIdentity.ps1"
$reader = Join-Path $repo "Get-PhotoIdentityApprovedInference.ps1"
$root = Join-Path ([IO.Path]::GetTempPath()) ("pi-inference-settings-" + [guid]::NewGuid().ToString("N"))
$configPath = Join-Path $root "launcher.json"
$envName = "PHOTOIDENTITY_INFERENCE_CONFIG_TEST_CONNECTION"
$before = [Environment]::GetEnvironmentVariable($envName, "Process")

function Write-Fixture {
    param([hashtable]$Settings, [string]$Upstream = "https://approved.example.test")
    $config = [ordered]@{
        postgresConnectionEnvironmentVariable = $envName
        settings = $Settings
        operator = [ordered]@{ azureOllamaUpstreamUrl = $Upstream }
    }
    $config | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $configPath -Encoding UTF8
}

function Assert-Launcher {
    param([bool]$ShouldPass, [string]$Message)
    $output = @(& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $launcher `
        -ConfigurationPath $configPath -ValidateConfigurationOnly 2>&1)
    $code = $LASTEXITCODE
    if (($code -eq 0) -ne $ShouldPass) {
        throw "$Message; exit $code; output $($output -join ' ')"
    }
}

try {
    New-Item -ItemType Directory -Path $root | Out-Null
    $modelDir = Join-Path $root "approved-model"
    New-Item -ItemType Directory -Path $modelDir | Out-Null
    foreach ($name in @("model.onnx", "vocab.json", "merges.txt")) {
        [IO.File]::WriteAllText((Join-Path $modelDir $name), "synthetic-$name")
    }
    $modelHash = (Get-FileHash -LiteralPath (Join-Path $modelDir "model.onnx") -Algorithm SHA256).Hash.ToLowerInvariant()
    $vocabHash = (Get-FileHash -LiteralPath (Join-Path $modelDir "vocab.json") -Algorithm SHA256).Hash.ToLowerInvariant()
    $mergesHash = (Get-FileHash -LiteralPath (Join-Path $modelDir "merges.txt") -Algorithm SHA256).Hash.ToLowerInvariant()
    $settings = @{
        PhotoIdentity__SemanticSearch__ModelDirectory = $modelDir
        PhotoIdentity__SemanticSearch__ModelSha256 = $modelHash
        PhotoIdentity__SemanticSearch__VocabularySha256 = $vocabHash
        PhotoIdentity__SemanticSearch__MergesSha256 = $mergesHash
        PhotoIdentity__CaptionEnrichment__Model = "synthetic-vision:1b"
        PhotoIdentity__CaptionEnrichment__ApprovedModelSha256 = ("a" * 64)
        PhotoIdentity__CaptionEnrichment__OllamaBaseUrl = "http://127.0.0.1:11435/"
        PhotoIdentity__CaptionEnrichment__ContextTokens = "1024"
        PhotoIdentity__CaptionEnrichment__TimeoutSeconds = "600"
    }
    [Environment]::SetEnvironmentVariable($envName,
        "Host=127.0.0.1;Database=synthetic;Username=synthetic;Password=test", "Process")
    Write-Fixture -Settings $settings
    Assert-Launcher -ShouldPass $true -Message "Valid inference settings denied"
    $approved = & $reader -ConfigurationPath $configPath
    if ($approved.OllamaVerified -or $approved.ClipModelSha256 -cne $modelHash -or
        $approved.OllamaSha256 -cne ("a" * 64) -or
        $approved.AzureOllamaUpstreamUrl -ne "https://approved.example.test" -or
        $approved.OllamaBaseUrl -ne "http://127.0.0.1:11435") {
        throw "Inference reader did not return the approved configuration."
    }
    # A valid hex pin must still match the actual bytes.
    $settings.PhotoIdentity__SemanticSearch__ModelSha256 = ("b" * 64)
    Write-Fixture -Settings $settings
    Assert-Launcher -ShouldPass $true -Message "Launcher should validate pin syntax"
    $rejected = $false
    try { $null = & $reader -ConfigurationPath $configPath } catch { $rejected = $true }
    if (-not $rejected) { throw "Reader accepted a CLIP digest mismatch." }
    $settings.PhotoIdentity__SemanticSearch__ModelSha256 = $modelHash

    $settings.PhotoIdentity__CaptionEnrichment__OllamaBaseUrl = "https://unvetted.example.test/"
    Write-Fixture -Settings $settings
    Assert-Launcher -ShouldPass $false -Message "Non-loopback model origin accepted"
    $settings.PhotoIdentity__CaptionEnrichment__OllamaBaseUrl = "http://127.0.0.1:11435/"

    Write-Fixture -Settings $settings -Upstream "https://example.test/path?secret=test"
    Assert-Launcher -ShouldPass $false -Message "Azure upstream with path/query accepted"
    Write-Fixture -Settings $settings -Upstream "https://approved.example.test"
    $settings.PhotoIdentity__CaptionEnrichment__ApprovedModelSha256 = "not-a-sha"
    Write-Fixture -Settings $settings
    Assert-Launcher -ShouldPass $false -Message "Malformed model digest accepted"
    Write-Host "Inference launcher configuration synthetic tests passed."
}
finally {
    [Environment]::SetEnvironmentVariable($envName, $before, "Process")
    if (Test-Path -LiteralPath $root) {
        Remove-Item -LiteralPath $root -Force -Recurse
    }
}
