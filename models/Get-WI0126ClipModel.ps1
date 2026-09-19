[CmdletBinding()]
param(
    [Parameter()]
    [string] $OutputDirectory = (Join-Path $env:LOCALAPPDATA 'PhotoIdentity\Models\WI-0126\clip-vit-base-patch32-b318363')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$sourceRepository = 'onnx-community/clip-vit-base-patch32-ONNX'
$sourceRevision = 'b318363e9c2fe791fd691d44862bb4cd4d7accc5'
$baseUrl = "https://huggingface.co/$sourceRepository/resolve/$sourceRevision"
$expectedModelSize = 605694110L
$expectedModelSha256 = '42b5dac8f0a73e45b1626ae7a69b41a4bc111c6fe1edafcceda8c2a5506dc6db'

$resolvedOutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
$modelPath = Join-Path $resolvedOutputDirectory 'model.onnx'
$vocabPath = Join-Path $resolvedOutputDirectory 'vocab.json'
$mergesPath = Join-Path $resolvedOutputDirectory 'merges.txt'

function Get-Sha256 {
    param(
        [Parameter(Mandatory)]
        [string] $Path
    )

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-PinnedFile {
    param(
        [Parameter(Mandatory)]
        [string] $RelativePath,
        [Parameter(Mandatory)]
        [string] $Destination
    )

    $uri = "$baseUrl/$RelativePath"
    $temporaryPath = "$Destination.download-$([guid]::NewGuid().ToString('N'))"
    try {
        Write-Host "Downloading $RelativePath ..."
        Invoke-WebRequest -Uri $uri -OutFile $temporaryPath -UseBasicParsing
        Move-Item -LiteralPath $temporaryPath -Destination $Destination -Force
    }
    finally {
        if (Test-Path -LiteralPath $temporaryPath) {
            Remove-Item -LiteralPath $temporaryPath -Force
        }
    }
}

New-Item -ItemType Directory -Force -Path $resolvedOutputDirectory | Out-Null

$modelReady = $false
if (Test-Path -LiteralPath $modelPath) {
    $modelInfo = Get-Item -LiteralPath $modelPath
    if ($modelInfo.Length -eq $expectedModelSize) {
        $existingModelSha256 = Get-Sha256 -Path $modelPath
        $modelReady = [string]::Equals(
            $existingModelSha256,
            $expectedModelSha256,
            [System.StringComparison]::OrdinalIgnoreCase)
    }
}

if (-not $modelReady) {
    Get-PinnedFile -RelativePath 'onnx/model.onnx' -Destination $modelPath
}

$modelInfo = Get-Item -LiteralPath $modelPath
if ($modelInfo.Length -ne $expectedModelSize) {
    throw "CLIP model byte-size mismatch. Expected $expectedModelSize, got $($modelInfo.Length)."
}

$modelSha256 = Get-Sha256 -Path $modelPath
if (-not [string]::Equals(
        $modelSha256,
        $expectedModelSha256,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "CLIP model SHA-256 mismatch. Expected $expectedModelSha256, got $modelSha256."
}

if (-not (Test-Path -LiteralPath $vocabPath)) {
    Get-PinnedFile -RelativePath 'vocab.json' -Destination $vocabPath
}
if (-not (Test-Path -LiteralPath $mergesPath)) {
    Get-PinnedFile -RelativePath 'merges.txt' -Destination $mergesPath
}

$vocabSha256 = Get-Sha256 -Path $vocabPath
$mergesSha256 = Get-Sha256 -Path $mergesPath

Write-Host ''
Write-Host 'WI-0126 CLIP assets are ready.'
Write-Host "Source          : https://huggingface.co/$sourceRepository"
Write-Host "Source revision : $sourceRevision"
Write-Host "Model SHA-256   : $modelSha256"
Write-Host "Vocab SHA-256   : $vocabSha256"
Write-Host "Merges SHA-256  : $mergesSha256"
Write-Host "Directory       : $resolvedOutputDirectory"

[pscustomobject]@{
    SourceRepository        = $sourceRepository
    SourceRevision          = $sourceRevision
    ModelPath               = $modelPath
    TokenizerVocabularyPath = $vocabPath
    TokenizerMergesPath     = $mergesPath
    ModelSha256             = $modelSha256
    VocabularySha256        = $vocabSha256
    MergesSha256            = $mergesSha256
}
