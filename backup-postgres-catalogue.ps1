[CmdletBinding()]
param(
    [string]$DatabaseName,
    [string]$OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$composeDirectory = Join-Path $PSScriptRoot "deploy\postgres"
$podman = Get-Command podman -ErrorAction SilentlyContinue
if ($null -eq $podman) {
    throw "Podman is required for PostgreSQL catalogue backup."
}

function Get-ServerDatabaseNames {
    param([Parameter(Mandatory = $true)][string]$ContainerId)

    $rows = @(
        & $podman.Source exec $ContainerId sh -lc `
            'psql -X -A -t -U "$POSTGRES_USER" -d "$POSTGRES_DB" -v ON_ERROR_STOP=1 -c "SELECT datname FROM pg_database WHERE datallowconn AND NOT datistemplate ORDER BY datname"' `
            2>$null)
    if ($LASTEXITCODE -ne 0) {
        throw "Could not enumerate PostgreSQL databases inside the running container."
    }

    @(
        $rows |
            ForEach-Object { ([string]$_).Trim() } |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            Select-Object -Unique)
}

function Test-PhotoIdentityDatabase {
    param(
        [Parameter(Mandatory = $true)][string]$ContainerId,
        [Parameter(Mandatory = $true)][string]$Candidate
    )

    $rows = @(
        & $podman.Source exec `
            -e "TARGET_DATABASE=$Candidate" `
            $ContainerId sh -lc `
            'psql -X -A -t -U "$POSTGRES_USER" -d "$TARGET_DATABASE" -v ON_ERROR_STOP=1 -c "SELECT CASE WHEN to_regclass(''public.photo_identity_schema_migrations'') IS NOT NULL AND to_regclass(''public.asset_revisions'') IS NOT NULL AND to_regclass(''public.review_actions'') IS NOT NULL THEN 1 ELSE 0 END"' `
            2>$null)

    return $LASTEXITCODE -eq 0 -and (($rows -join " ").Trim()) -eq "1"
}

function Resolve-CatalogueDatabase {
    param([Parameter(Mandatory = $true)][string]$ContainerId)

    $serverDatabases = @(Get-ServerDatabaseNames -ContainerId $ContainerId)
    if (-not [string]::IsNullOrWhiteSpace($DatabaseName)) {
        if ($serverDatabases -notcontains $DatabaseName) {
            throw "The requested -DatabaseName '$DatabaseName' was not found."
        }
        if (-not (Test-PhotoIdentityDatabase -ContainerId $ContainerId -Candidate $DatabaseName)) {
            throw "The requested -DatabaseName '$DatabaseName' does not contain the Photo Identity catalogue markers."
        }
        return $DatabaseName
    }

    $matches = @(
        foreach ($candidate in $serverDatabases) {
            if (Test-PhotoIdentityDatabase -ContainerId $containerId -Candidate $candidate) {
                $candidate
            }
        })

    if ($matches.Count -eq 1) {
        return [string]$matches[0]
    }
    if ($matches.Count -eq 0) {
        throw "No Photo Identity PostgreSQL catalogue database was found in the running service."
    }

    throw "Multiple Photo Identity PostgreSQL catalogue databases were found: $($matches -join ', '). Rerun with -DatabaseName <name> to choose the production authority explicitly."
}

Push-Location $composeDirectory
$containerDumpPath = $null
try {
    $containerId = (& $podman.Source compose ps -q postgres).Trim()
    if ([string]::IsNullOrWhiteSpace($containerId)) {
        throw "The repository PostgreSQL service is not running. Run ./verify-postgres.ps1 first."
    }

    $catalogueDatabase = Resolve-CatalogueDatabase -ContainerId $containerId

    if ([string]::IsNullOrWhiteSpace($OutputPath)) {
        $root = if ([string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
            Join-Path ([IO.Path]::GetTempPath()) "PhotoIdentity\backups\postgresql"
        }
        else {
            Join-Path $env:LOCALAPPDATA "PhotoIdentity\backups\postgresql"
        }
        [IO.Directory]::CreateDirectory($root) | Out-Null
        $OutputPath = Join-Path $root ("photoidentity-postgresql-{0}.dump" -f (Get-Date -Format "yyyyMMdd-HHmmss"))
    }
    else {
        $OutputPath = [IO.Path]::GetFullPath($OutputPath)
        $directory = Split-Path -Parent $OutputPath
        if (-not [string]::IsNullOrWhiteSpace($directory)) {
            [IO.Directory]::CreateDirectory($directory) | Out-Null
        }
    }

    $reportPath = "$OutputPath.json"
    if ((Test-Path -LiteralPath $OutputPath) -or (Test-Path -LiteralPath $reportPath)) {
        throw "Backup output already exists. Existing backups and reports are never overwritten."
    }

    $containerDumpPath = "/tmp/photoidentity-$([Guid]::NewGuid().ToString('N')).dump"
    Write-Host "Creating PostgreSQL logical backup from '$catalogueDatabase'..."
    & $podman.Source exec `
        -e "TARGET_DATABASE=$catalogueDatabase" `
        -e "DUMP_PATH=$containerDumpPath" `
        $containerId sh -lc `
        'pg_dump -U "$POSTGRES_USER" -d "$TARGET_DATABASE" --format=custom --compress=6 --no-owner --no-acl --file="$DUMP_PATH"' `
        2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "PostgreSQL backup failed inside the container."
    }

    $containerSource = "$containerId`:$containerDumpPath"
    & $podman.Source cp $containerSource $OutputPath | Out-Null
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $OutputPath -PathType Leaf)) {
        throw "Podman could not copy the completed PostgreSQL backup to the host."
    }

    $backupInfo = Get-Item -LiteralPath $OutputPath
    if ($backupInfo.Length -le 0) {
        throw "PostgreSQL backup output is empty."
    }

    $sha256 = (Get-FileHash -LiteralPath $OutputPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $report = [ordered]@{
        capturedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
        sourceDatabase = $catalogueDatabase
        backupPath = $OutputPath
        backupBytes = [long]$backupInfo.Length
        backupSha256 = $sha256
        format = "pg_dump-custom"
        privacyNote = "Credentials and connection strings are omitted. The backup contains the private catalogue and must be protected as sensitive local data."
    }
    $report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $reportPath -Encoding UTF8

    Write-Host "PostgreSQL catalogue backup completed."
    Write-Host "database: $catalogueDatabase"
    Write-Host "backup: $OutputPath"
    Write-Host "sha256: $sha256"
    Write-Host "report: $reportPath"
}
finally {
    if (-not [string]::IsNullOrWhiteSpace($containerDumpPath)) {
        & $podman.Source exec -e "DUMP_PATH=$containerDumpPath" $containerId sh -lc 'rm -f "$DUMP_PATH"' *> $null
    }
    Pop-Location
}
