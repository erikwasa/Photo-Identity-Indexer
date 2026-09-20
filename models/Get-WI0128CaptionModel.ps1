[CmdletBinding()]
param(
    [Parameter()]
    [string] $Model = 'qwen2.5vl:3b',

    [Parameter()]
    [uri] $BaseUrl = 'http://127.0.0.1:11434'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $BaseUrl.IsAbsoluteUri -or
    -not $BaseUrl.IsLoopback -or
    ($BaseUrl.Scheme -ne 'http' -and $BaseUrl.Scheme -ne 'https')) {
    throw 'BaseUrl must be an absolute loopback HTTP(S) URL.'
}

$ollama = Get-Command ollama -ErrorAction SilentlyContinue
if ($null -eq $ollama) {
    throw 'Ollama was not found on PATH. Install Ollama for Windows, then rerun this helper.'
}

Write-Host "Preparing local WI-0128 vision model '$Model' ..."
& $ollama.Source pull $Model
if ($LASTEXITCODE -ne 0) {
    throw "ollama pull failed with exit code $LASTEXITCODE."
}

$tagsUri = [uri]::new(
    $BaseUrl.AbsoluteUri.TrimEnd('/') + '/api/tags')
$inventory = Invoke-RestMethod -Method Get -Uri $tagsUri

$modelInfo = @($inventory.models) |
    Where-Object {
        [string]::Equals(
            [string]$_.name,
            $Model,
            [System.StringComparison]::Ordinal) -or
        [string]::Equals(
            [string]$_.model,
            $Model,
            [System.StringComparison]::Ordinal)
    } |
    Select-Object -First 1

if ($null -eq $modelInfo) {
    throw "Ollama completed the pull but '$Model' was not found in the local model inventory."
}

$digest = ([string]$modelInfo.digest).Trim().ToLowerInvariant()
if ($digest.StartsWith('sha256:')) {
    $digest = $digest.Substring('sha256:'.Length)
}
if ($digest.Length -ne 64 -or $digest -notmatch '^[0-9a-f]{64}$') {
    throw "Ollama returned an invalid model digest '$digest'."
}

$sizeBytes = [int64]$modelInfo.size
$family = [string]$modelInfo.details.family
$parameterSize = [string]$modelInfo.details.parameter_size
$quantizationLevel = [string]$modelInfo.details.quantization_level

Write-Host ''
Write-Host 'WI-0128 local vision model is ready.'
Write-Host "Model            : $Model"
Write-Host "Digest           : $digest"
Write-Host "Package bytes    : $sizeBytes"
Write-Host "Family           : $family"
Write-Host "Parameter size   : $parameterSize"
Write-Host "Quantization     : $quantizationLevel"
Write-Host "Endpoint         : $($BaseUrl.AbsoluteUri.TrimEnd('/'))"

[pscustomobject]@{
    Model             = $Model
    Digest            = $digest
    SizeBytes         = $sizeBytes
    Family            = $family
    ParameterSize     = $parameterSize
    QuantizationLevel = $quantizationLevel
    BaseUrl           = $BaseUrl.AbsoluteUri.TrimEnd('/')
}
