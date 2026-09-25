$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$helper = Join-Path $repoRoot 'review-metadata-enrichment.ps1'
$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('photoidentity-wi0163-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tempRoot | Out-Null

try {
    $reportPath = Join-Path $tempRoot 'report.json'
    $rulesPath = Join-Path $tempRoot 'rules.json'
    $approvedPath = Join-Path $tempRoot 'approved.json'

    @'
{
  "mode": "dry-run",
  "summary": {
    "TotalPhotos": 3,
    "MissingLocation": 3,
    "Ambiguous": 0
  },
  "items": [
    {
      "sourceKey": "2024/03/a.jpg",
      "existingDate": "2024-03-23",
      "proposedPlace": "Sverige/Stockholm/Södermalm",
      "placeRule": "2024-03-23-sodermalm"
    },
    {
      "sourceKey": "fideli/a.jpg",
      "existingDate": "2024-03-23",
      "proposedPlace": "Sverige/Stockholm/Södermalm",
      "placeRule": "2024-03-23-sodermalm"
    },
    {
      "sourceKey": "2026/08/a.jpg",
      "existingDate": "2026-08-15",
      "proposedPlace": "Sverige/Värmdö/Svartsö",
      "placeRule": "2026-08-15-svartso"
    }
  ]
}
'@ | Set-Content -LiteralPath $reportPath -Encoding utf8

    @'
{
  "inferDateFromFilename": false,
  "inferDateFromDirectory": false,
  "excludedDateDirectories": ["1970"],
  "placeRules": [
    {
      "name": "2024-03-23-sodermalm",
      "from": "2024-03-23",
      "to": "2024-03-23",
      "place": "Sverige/Stockholm/Södermalm"
    },
    {
      "name": "2026-08-15-svartso",
      "from": "2026-08-15",
      "to": "2026-08-15",
      "place": "Sverige/Värmdö/Svartsö"
    }
  ]
}
'@ | Set-Content -LiteralPath $rulesPath -Encoding utf8

    & $helper `
        -Report $reportPath `
        -SamplesPerRule 0 `
        -Rules $rulesPath `
        -ApproveRule @('2024-03-23-sodermalm') `
        -ApprovedRulesOutput $approvedPath

    if (-not (Test-Path -LiteralPath $approvedPath -PathType Leaf)) {
        throw 'Approved rules file was not written.'
    }

    $approved = Get-Content -LiteralPath $approvedPath -Raw | ConvertFrom-Json
    if ($approved.inferDateFromFilename -ne $false -or $approved.inferDateFromDirectory -ne $false) {
        throw 'Approved rules unexpectedly enable date inference.'
    }

    if (@($approved.placeRules).Count -ne 1) {
        throw 'Approved rules did not contain exactly one selected Place rule.'
    }

    if ([string]$approved.placeRules[0].name -ne '2024-03-23-sodermalm') {
        throw 'Approved rules contained the wrong Place rule.'
    }

    Write-Host 'review-metadata-enrichment smoke test passed.'
}
finally {
    Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
}
