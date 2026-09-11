[CmdletBinding()]
param(
    [string]$OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$composeDirectory = Join-Path $PSScriptRoot "deploy\postgres"
$podman = Get-Command podman -ErrorAction SilentlyContinue
if ($null -eq $podman) {
    throw "Podman is required for PostgreSQL catalogue backup."
}

Push-Location $composeDirectory
try {
    $containerId = (& $podman.Source compose ps -q postgres).Trim()
    if ([string]::IsNullOrWhiteSpace($containerId)) {
        throw "The repository PostgreSQL service is not running. Run ./verify-postgres.ps1 first."
    }

    if ([string]::IsNullOrWhiteSpace($OutputPath)) {
        $root = if ([string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
            Join-Path ([IO.Path]::GetTempPath()) "PhotoIdentity\backups\postgresql"
        }
        else {
            Join-Path $env:LOCALAPPDATA "PhotoIdentity\backups\postgresql"
        }
        [IO.Directory]::CreateDirectory($root) | Out-Null
        $OutputPath = Join-Path $root ("photoidentity-postgresql-{0}.sql" -f (Get-Date -Format "yyyyMMdd-HHmmss"))
    }
    else {
        $OutputPath = [IO.Path]::GetFullPath($OutputPath)
        $directory = Split-Path -Parent $OutputPath
        if (-not [string]::IsNullOrWhiteSpace($directory)) {
            [IO.Directory]::CreateDirectory($directory) | Out-Null
        }
    }

    if (Test-Path -LiteralPath $OutputPath) {
        throw "Backup output already exists. Existing backups are never overwritten: $OutputPath"
    }

    $output = @(& $podman.Source exec $containerId sh -lc 'pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" --no-owner --no-acl' 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "PostgreSQL backup failed inside the container."
    }

    $output | Set-Content -LiteralPath $OutputPath -Encoding UTF8
    $backupInfo = Get-Item -LiteralPath $OutputPath
    if ($backupInfo.Length -le 0) {
        throw "PostgreSQL backup output is empty."
    }

    $sha256 = (Get-FileHash -LiteralPath $OutputPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $report = [ordered]@{
        capturedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
        backupPath = $OutputPath
        backupBytes = [long]$backupInfo.Length
        backupSha256 = $sha256
        format = "plain-sql"
        privacyNote = "Credentials and connection strings are omitted. The backup contains the private catalogue and must be protected as sensitive local data."
    }
    $reportPath = "$OutputPath.json"
    $report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $reportPath -Encoding UTF8

    Write-Host "PostgreSQL catalogue backup completed."
    Write-Host "backup: $OutputPath"
    Write-Host "sha256: $sha256"
    Write-Host "report: $reportPath"
}
finally {
    Pop-Location
}
