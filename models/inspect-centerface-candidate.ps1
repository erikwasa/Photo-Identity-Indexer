[CmdletBinding()]
param(
    [Parameter()]
    [string] $OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$sourceRevision = 'b82ec0c4844e89fd5a0305986aed9bdf33c72585'
$sourceUrl = "https://raw.githubusercontent.com/Star-Clouds/CenterFace/$sourceRevision/models/onnx/centerface.onnx"
$expectedSize = 7532772L
$expectedGitBlobSha1 = '1487d5fe214feb569865b225216b24c8f4ef1050'
$expectedSha256 = '77e394b51108381b4c4f7b4baf1c64ca9f4aba73e5e803b2636419578913b5fe'
$tempPath = Join-Path ([System.IO.Path]::GetTempPath()) ("centerface-{0}.onnx" -f [guid]::NewGuid())

function ConvertTo-HexString {
    param(
        [Parameter(Mandatory)]
        [byte[]] $Bytes
    )

    return -join ($Bytes | ForEach-Object { $_.ToString('x2') })
}

try {
    Write-Host "Downloading immutable CenterFace candidate..."
    Invoke-WebRequest -Uri $sourceUrl -OutFile $tempPath -UseBasicParsing

    [byte[]] $modelBytes = [System.IO.File]::ReadAllBytes($tempPath)
    if ($modelBytes.LongLength -ne $expectedSize) {
        throw "CenterFace byte-size mismatch. Expected $expectedSize, got $($modelBytes.LongLength)."
    }

    $sha256Algorithm = [System.Security.Cryptography.SHA256]::Create()
    try {
        $sha256 = ConvertTo-HexString -Bytes ($sha256Algorithm.ComputeHash($modelBytes))
    }
    finally {
        $sha256Algorithm.Dispose()
    }

    if (-not [string]::Equals($sha256, $expectedSha256, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "CenterFace SHA-256 mismatch. Expected $expectedSha256, got $sha256."
    }

    [byte[]] $gitHeader = [System.Text.Encoding]::UTF8.GetBytes("blob $($modelBytes.LongLength)`0")
    [byte[]] $gitObjectBytes = New-Object byte[] ($gitHeader.Length + $modelBytes.Length)
    [System.Array]::Copy($gitHeader, 0, $gitObjectBytes, 0, $gitHeader.Length)
    [System.Array]::Copy($modelBytes, 0, $gitObjectBytes, $gitHeader.Length, $modelBytes.Length)

    $sha1Algorithm = [System.Security.Cryptography.SHA1]::Create()
    try {
        $gitBlobSha1 = ConvertTo-HexString -Bytes ($sha1Algorithm.ComputeHash($gitObjectBytes))
    }
    finally {
        $sha1Algorithm.Dispose()
    }

    if (-not [string]::Equals($gitBlobSha1, $expectedGitBlobSha1, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "CenterFace Git blob mismatch. Expected $expectedGitBlobSha1, got $gitBlobSha1."
    }

    Write-Host "CenterFace candidate verified."
    Write-Host "Source revision : $sourceRevision"
    Write-Host "Byte size       : $($modelBytes.LongLength)"
    Write-Host "Git blob SHA-1  : $gitBlobSha1"
    Write-Host "SHA-256         : $sha256"

    if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
        $resolvedOutputPath = [System.IO.Path]::GetFullPath($OutputPath)
        $outputDirectory = Split-Path -Parent $resolvedOutputPath
        if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
            New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
        }

        Copy-Item -LiteralPath $tempPath -Destination $resolvedOutputPath -Force
        Set-Content -LiteralPath "$resolvedOutputPath.sha256" -Value "$sha256  $([System.IO.Path]::GetFileName($resolvedOutputPath))" -Encoding Ascii
        Write-Host "Verified candidate written to: $resolvedOutputPath"
    }
}
finally {
    if (Test-Path -LiteralPath $tempPath) {
        Remove-Item -LiteralPath $tempPath -Force
    }
}
