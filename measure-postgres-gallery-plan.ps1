[CmdletBinding()]
param(
    [string]$ContainerName,
    [string]$DatabaseName,
    [string]$OutputPath,
    [ValidateRange(1, 200)]
    [int]$Limit = 40,
    [ValidateRange(5, 300)]
    [int]$StatementTimeoutSeconds = 120
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$sqlPath = Join-Path $PSScriptRoot "tools\postgres-gallery-plan.sql"
if (-not (Test-Path -LiteralPath $sqlPath -PathType Leaf)) {
    throw "PostgreSQL gallery plan SQL was not found."
}

$podman = Get-Command podman -ErrorAction SilentlyContinue
if ($null -eq $podman) {
    throw "Podman is required for the PostgreSQL gallery plan probe."
}

function Convert-ContainerRow {
    param([Parameter(Mandatory = $true)][string]$Row)

    $parts = $Row.Split('|', 2)
    if ($parts.Count -ne 2 -or
        [string]::IsNullOrWhiteSpace($parts[0]) -or
        [string]::IsNullOrWhiteSpace($parts[1])) {
        return $null
    }

    return [pscustomobject]@{
        Id = $parts[0].Trim()
        Name = $parts[1].Trim()
    }
}

function Get-RunningContainers {
    param([string]$LabelFilter)

    $arguments = @("ps")
    if (-not [string]::IsNullOrWhiteSpace($LabelFilter)) {
        $arguments += @("--filter", "label=$LabelFilter")
    }
    $arguments += @("--format", "{{.ID}}|{{.Names}}")

    $rows = @(& $podman.Source @arguments 2>$null)
    if ($LASTEXITCODE -ne 0) {
        return @()
    }

    return @(
        $rows |
            ForEach-Object { Convert-ContainerRow -Row ([string]$_) } |
            Where-Object { $null -ne $_ })
}

function Resolve-PostgresContainer {
    if (-not [string]::IsNullOrWhiteSpace($ContainerName)) {
        $namedMatches = @(
            Get-RunningContainers |
                Where-Object { $_.Name -eq $ContainerName })
        if ($namedMatches.Count -eq 1) {
            return $namedMatches[0]
        }
        if ($namedMatches.Count -gt 1) {
            throw "Multiple running containers matched -ContainerName '$ContainerName'."
        }
        throw "No running Podman container named '$ContainerName' was found."
    }

    foreach ($label in @(
        "com.docker.compose.service=postgres",
        "io.podman.compose.service=postgres")) {
        $matches = @(Get-RunningContainers -LabelFilter $label)
        if ($matches.Count -eq 1) {
            return $matches[0]
        }
        if ($matches.Count -gt 1) {
            $preferred = @($matches | Where-Object { $_.Name -eq "postgres-postgres-1" })
            if ($preferred.Count -eq 1) {
                return $preferred[0]
            }
            throw "Multiple running PostgreSQL service containers were found. Rerun with -ContainerName <name>."
        }
    }

    $fallbackMatches = @(
        Get-RunningContainers |
            Where-Object { $_.Name -eq "postgres-postgres-1" })
    if ($fallbackMatches.Count -eq 1) {
        return $fallbackMatches[0]
    }

    throw "The running PostgreSQL container could not be identified. Start the repository PostgreSQL service or rerun with -ContainerName <name>."
}

function Get-ServerDatabaseNames {
    param([Parameter(Mandatory = $true)]$Container)

    $rows = @(
        & $podman.Source exec $Container.Id sh -lc `
            'test -n "$POSTGRES_DB" && test -n "$POSTGRES_USER" && test -n "$POSTGRES_PASSWORD" && PGPASSWORD="$POSTGRES_PASSWORD" psql -X -A -t -U "$POSTGRES_USER" -d "$POSTGRES_DB" -v ON_ERROR_STOP=1 -c "SELECT datname FROM pg_database WHERE datallowconn AND NOT datistemplate ORDER BY datname"' `
            2>$null)
    if ($LASTEXITCODE -ne 0) {
        throw "Could not enumerate databases inside the running PostgreSQL container."
    }

    return @(
        $rows |
            ForEach-Object { ([string]$_).Trim() } |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            Select-Object -Unique)
}

function Test-PhotoIdentityCatalogueDatabase {
    param(
        [Parameter(Mandatory = $true)]$Container,
        [Parameter(Mandatory = $true)][string]$CandidateDatabaseName
    )

    $markerOutput = @(
        & $podman.Source exec `
            -e "TARGET_DATABASE=$CandidateDatabaseName" `
            $Container.Id sh -lc `
            'test -n "$POSTGRES_USER" && test -n "$POSTGRES_PASSWORD" && PGPASSWORD="$POSTGRES_PASSWORD" psql -X -A -t -U "$POSTGRES_USER" -d "$TARGET_DATABASE" -v ON_ERROR_STOP=1 -c "SELECT CASE WHEN to_regclass(''public.photo_identity_schema_migrations'') IS NOT NULL AND to_regclass(''public.face_occurrences'') IS NOT NULL AND to_regclass(''public.identity_suggestion_rankings'') IS NOT NULL THEN 1 ELSE 0 END"' `
            2>$null)
    if ($LASTEXITCODE -ne 0) {
        return $false
    }

    return (($markerOutput -join " ").Trim()) -eq "1"
}

function Resolve-CatalogueDatabase {
    param([Parameter(Mandatory = $true)]$Container)

    $serverDatabaseNames = @(Get-ServerDatabaseNames -Container $Container)
    if ($serverDatabaseNames.Count -eq 0) {
        throw "The running PostgreSQL server did not expose any connectable databases."
    }

    if (-not [string]::IsNullOrWhiteSpace($DatabaseName)) {
        if ($serverDatabaseNames -notcontains $DatabaseName) {
            throw "The requested -DatabaseName '$DatabaseName' was not found in the running PostgreSQL server."
        }
        if (-not (Test-PhotoIdentityCatalogueDatabase -Container $Container -CandidateDatabaseName $DatabaseName)) {
            throw "The requested -DatabaseName '$DatabaseName' does not contain the required Photo Identity catalogue tables."
        }

        return [pscustomobject]@{
            Name = $DatabaseName
            SelectionSource = "explicit"
        }
    }

    $catalogueMatches = @(
        foreach ($candidate in $serverDatabaseNames) {
            if (Test-PhotoIdentityCatalogueDatabase -Container $Container -CandidateDatabaseName $candidate) {
                $candidate
            }
        })

    if ($catalogueMatches.Count -eq 1) {
        return [pscustomobject]@{
            Name = $catalogueMatches[0]
            SelectionSource = "schema-markers"
        }
    }

    if ($catalogueMatches.Count -eq 0) {
        throw "No database in the running PostgreSQL server contains the required Photo Identity catalogue tables (photo_identity_schema_migrations, face_occurrences, identity_suggestion_rankings)."
    }

    throw "Multiple Photo Identity catalogue databases were found: $($catalogueMatches -join ', '). Rerun with -DatabaseName <name> to select the authoritative catalogue explicitly."
}

$container = Resolve-PostgresContainer
$catalogueDatabase = Resolve-CatalogueDatabase -Container $container

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $reportDirectory = Join-Path ([IO.Path]::GetTempPath()) "PhotoIdentity\postgres-gallery-plan"
    [IO.Directory]::CreateDirectory($reportDirectory) | Out-Null
    $OutputPath = Join-Path $reportDirectory ("gallery-plan-{0}.txt" -f (Get-Date -Format "yyyyMMdd-HHmmss"))
}
else {
    $OutputPath = [IO.Path]::GetFullPath($OutputPath)
    $reportDirectory = Split-Path -Parent $OutputPath
    if (-not [string]::IsNullOrWhiteSpace($reportDirectory)) {
        [IO.Directory]::CreateDirectory($reportDirectory) | Out-Null
    }
}

$header = @(
    "Photo Identity PostgreSQL gallery plan evidence",
    "captured-at-utc: $([DateTimeOffset]::UtcNow.ToString('O'))",
    "database-name: $($catalogueDatabase.Name)",
    "database-selection-source: $($catalogueDatabase.SelectionSource)",
    "container-name: $($container.Name)",
    "launcher-connection-secret: not-read",
    "compose-environment-file: not-read",
    "statement-timeout-seconds: $StatementTimeoutSeconds",
    "note: connection string and credentials are intentionally omitted",
    ""
)
Set-Content -LiteralPath $OutputPath -Value $header -Encoding UTF8

$execArgs = @(
    "exec",
    "-i",
    "-e", "TARGET_DATABASE=$($catalogueDatabase.Name)",
    "-e", "PAGE_LIMIT=$Limit",
    "-e", "STATEMENT_TIMEOUT_MS=$($StatementTimeoutSeconds * 1000)",
    $container.Id,
    "sh", "-lc",
    'test -n "$POSTGRES_USER" && test -n "$POSTGRES_PASSWORD" && PGPASSWORD="$POSTGRES_PASSWORD" psql -X -v ON_ERROR_STOP=1 -A -t -U "$POSTGRES_USER" -d "$TARGET_DATABASE" -v page_limit="$PAGE_LIMIT" -v statement_timeout_ms="$STATEMENT_TIMEOUT_MS"'
)

$probeOutput = Get-Content -LiteralPath $sqlPath -Raw |
    & $podman.Source @execArgs 2>&1
$exitCode = $LASTEXITCODE
$probeOutput | Add-Content -LiteralPath $OutputPath -Encoding UTF8

if ($exitCode -ne 0) {
    throw "PostgreSQL gallery plan capture failed. The partial privacy-safe report was retained at '$OutputPath'."
}

Write-Host "PostgreSQL gallery plan capture passed."
Write-Host "container: $($container.Name)"
Write-Host "database: $($catalogueDatabase.Name)"
Write-Host "report: $OutputPath"
Write-Host "The report uses the running PostgreSQL container credentials internally and omits the launcher connection string and credentials."
Write-Host "Attach it for WI-0104 query-plan review before adding indexes."
