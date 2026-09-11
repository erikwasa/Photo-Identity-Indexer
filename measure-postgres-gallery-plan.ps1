[CmdletBinding()]
param(
    [string]$ContainerName,
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

$container = Resolve-PostgresContainer

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
    "database-identity-source: running container POSTGRES_DB/POSTGRES_USER",
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
    "-e", "PAGE_LIMIT=$Limit",
    "-e", "STATEMENT_TIMEOUT_MS=$($StatementTimeoutSeconds * 1000)",
    $container.Id,
    "sh", "-lc",
    'test -n "$POSTGRES_DB" && test -n "$POSTGRES_USER" && test -n "$POSTGRES_PASSWORD" && PGPASSWORD="$POSTGRES_PASSWORD" psql -X -v ON_ERROR_STOP=1 -A -t -U "$POSTGRES_USER" -d "$POSTGRES_DB" -v page_limit="$PAGE_LIMIT" -v statement_timeout_ms="$STATEMENT_TIMEOUT_MS"'
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
Write-Host "report: $OutputPath"
Write-Host "The report uses the running PostgreSQL container's own database/user identity and omits the launcher connection string and credentials."
Write-Host "Attach it for WI-0104 query-plan review before adding indexes."
