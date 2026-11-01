[CmdletBinding()]
param(
    [string]$ConfigurationPath,
    [switch]$VerifyOllama,
    [switch]$AzureUpstreamOnly
)

# Read-only operator bridge from launcher.json to ad hoc scripts and explicit
# CLI flags. No model download, app startup, file mutation or caption request.
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($ConfigurationPath)) {
    if (-not [string]::IsNullOrWhiteSpace($env:PHOTOIDENTITY_LAUNCHER_CONFIG)) {
        $ConfigurationPath = $env:PHOTOIDENTITY_LAUNCHER_CONFIG
    }
    elseif (Test-Path -LiteralPath (Join-Path $PSScriptRoot "PhotoIdentity.launcher.json")) {
        $ConfigurationPath = Join-Path $PSScriptRoot "PhotoIdentity.launcher.json"
    }
    elseif (-not [string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
        $ConfigurationPath = Join-Path $env:LOCALAPPDATA "PhotoIdentity\launcher.json"
    }
    else {
        throw "Set -ConfigurationPath or PHOTOIDENTITY_LAUNCHER_CONFIG."
    }
}
$ConfigurationPath = [Environment]::ExpandEnvironmentVariables($ConfigurationPath)
if (-not (Test-Path -LiteralPath $ConfigurationPath -PathType Leaf)) {
    throw "Photo Identity launcher configuration was not found."
}
$config = Get-Content -LiteralPath $ConfigurationPath -Raw | ConvertFrom-Json
if ($null -eq $config.settings -or
    $config.settings -isnot [pscustomobject]) {
    throw "The launcher settings object is missing."
}
$settings = $config.settings

function Read-Setting {
    param([string]$Name, [string]$Default = $null)
    $property = $settings.PSObject.Properties[$Name]
    if ($null -eq $property -or
        [string]::IsNullOrWhiteSpace([string]$property.Value)) {
        if ($null -ne $Default) { return $Default }
        throw "Required approved inference launcher setting is missing: $Name"
    }
    return [Environment]::ExpandEnvironmentVariables(([string]$property.Value).Trim())
}

function Assert-Sha256 {
    param([string]$Name, [string]$Value)
    if ($Value -cnotmatch '^[a-f0-9]{64}$') {
        throw "$Name must be an approved lowercase SHA-256 digest."
    }
}

function Assert-LocalPhysicalFile {
    param([string]$Path)
    if ($Path -notmatch '^[A-Za-z]:[\\/]' -or
        -not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Approved CLIP asset must be an existing local absolute file."
    }
    $fullPath = [IO.Path]::GetFullPath($Path)
    $cursor = $fullPath
    while (-not [string]::IsNullOrWhiteSpace($cursor)) {
        if (((Get-Item -LiteralPath $cursor -Force).Attributes -band
            [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Approved CLIP asset must not use symlinks or junctions."
        }
        $parent = Split-Path -Parent $cursor
        if ([string]::IsNullOrWhiteSpace($parent) -or $parent -eq $cursor) { break }
        $cursor = $parent
    }
    return $fullPath
}

function Assert-ApprovedFile {
    param([string]$Path, [string]$Expected, [string]$Label)
    Assert-Sha256 -Name $Label -Value $Expected
    $resolved = Assert-LocalPhysicalFile -Path $Path
    $actual = (Get-FileHash -LiteralPath $resolved -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -cne $Expected) {
        throw "$Label SHA-256 mismatch; explicit approval is required before inference."
    }
    return $resolved
}

$azureUpstream = $null
if ($null -ne $config.PSObject.Properties["operator"] -and $null -ne $config.operator) {
    if ($config.operator -isnot [pscustomobject]) {
        throw "operator must be a JSON object."
    }
    $property = $config.operator.PSObject.Properties["azureOllamaUpstreamUrl"]
    if ($null -ne $property -and -not [string]::IsNullOrWhiteSpace([string]$property.Value)) {
        [uri]$upstreamUri = $null
        if (-not [uri]::TryCreate(([string]$property.Value).Trim(),
            [UriKind]::Absolute, [ref]$upstreamUri) -or
            $upstreamUri.Scheme -ne "https" -or $upstreamUri.IsLoopback -or
            $upstreamUri.UserInfo -or $upstreamUri.Query -or
            $upstreamUri.Fragment -or $upstreamUri.AbsolutePath -ne "/") {
            throw "operator.azureOllamaUpstreamUrl must be a credential-free remote HTTPS origin."
        }
        $azureUpstream = $upstreamUri.AbsoluteUri.TrimEnd('/')
    }
}

if ($AzureUpstreamOnly) {
    if ($VerifyOllama) {
        throw "AzureUpstreamOnly cannot be combined with VerifyOllama."
    }
    if ([string]::IsNullOrWhiteSpace($azureUpstream)) {
        throw "An operator-approved Azure upstream origin is not configured."
    }
    return [pscustomobject]@{ AzureOllamaUpstreamUrl = $azureUpstream }
}

$modelDirectory = Read-Setting "PhotoIdentity__SemanticSearch__ModelDirectory"
$modelSha = Read-Setting "PhotoIdentity__SemanticSearch__ModelSha256"
$vocabSha = Read-Setting "PhotoIdentity__SemanticSearch__VocabularySha256"
$mergesSha = Read-Setting "PhotoIdentity__SemanticSearch__MergesSha256"
$clipModel = Assert-ApprovedFile (Join-Path $modelDirectory "model.onnx") $modelSha "CLIP model"
$clipVocab = Assert-ApprovedFile (Join-Path $modelDirectory "vocab.json") $vocabSha "CLIP vocabulary"
$clipMerges = Assert-ApprovedFile (Join-Path $modelDirectory "merges.txt") $mergesSha "CLIP merges"

$ollamaModel = Read-Setting "PhotoIdentity__CaptionEnrichment__Model"
if ($ollamaModel.Length -gt 100 -or
    $ollamaModel -notmatch '^[A-Za-z0-9][A-Za-z0-9._:/-]*$') {
    throw "Invalid approved Ollama model identifier."
}
$ollamaSha = Read-Setting "PhotoIdentity__CaptionEnrichment__ApprovedModelSha256"
Assert-Sha256 -Name "Ollama model" -Value $ollamaSha
$baseUrl = Read-Setting "PhotoIdentity__CaptionEnrichment__OllamaBaseUrl" "http://127.0.0.1:11434/"
[uri]$uri = $null
if (-not [uri]::TryCreate($baseUrl, [UriKind]::Absolute, [ref]$uri) -or
    -not $uri.IsLoopback -or $uri.Scheme -notin @("http", "https") -or
    $uri.AbsolutePath -ne "/" -or $uri.UserInfo -or $uri.Query -or $uri.Fragment) {
    throw "Caption OllamaBaseUrl must be a credential-free loopback HTTP(S) origin."
}
$contextTokens = [int](Read-Setting "PhotoIdentity__CaptionEnrichment__ContextTokens" "1024")
$timeoutSeconds = [int](Read-Setting "PhotoIdentity__CaptionEnrichment__TimeoutSeconds" "600")
if ($contextTokens -lt 256 -or $contextTokens -gt 32768 -or
    $timeoutSeconds -lt 30 -or $timeoutSeconds -gt 1800) {
    throw "Caption inference limits are outside approved ranges."
}

$verified = $false
if ($VerifyOllama) {
    # GET /api/tags is a model-inventory check, never a thumbnail disclosure.
    $tagsUri = $uri.AbsoluteUri.TrimEnd('/') + '/api/tags'
    $inventory = Invoke-RestMethod -Uri $tagsUri -Method Get -TimeoutSec 15
    $found = @($inventory.models) | Where-Object {
        [string]::Equals([string]$_.name, $ollamaModel, [StringComparison]::Ordinal) -or
        [string]::Equals([string]$_.model, $ollamaModel, [StringComparison]::Ordinal)
    } | Select-Object -First 1
    if ($null -eq $found) {
        throw "Approved Ollama model is not available at the configured loopback endpoint."
    }
    $digest = ([string]$found.digest -replace '^sha256:', '').ToLowerInvariant()
    if ($digest -cne $ollamaSha) {
        throw "Installed Ollama model SHA-256 does not match the launcher approval."
    }
    $verified = $true
}

[pscustomobject]@{
    ClipModelPath = $clipModel
    ClipModelSha256 = $modelSha
    ClipVocabularyPath = $clipVocab
    ClipVocabularySha256 = $vocabSha
    ClipMergesPath = $clipMerges
    ClipMergesSha256 = $mergesSha
    OllamaBaseUrl = $uri.AbsoluteUri.TrimEnd('/')
    OllamaModel = $ollamaModel
    OllamaSha256 = $ollamaSha
    OllamaVerified = $verified
    CaptionContextTokens = $contextTokens
    CaptionTimeoutSeconds = $timeoutSeconds
    AzureOllamaUpstreamUrl = $azureUpstream
}
