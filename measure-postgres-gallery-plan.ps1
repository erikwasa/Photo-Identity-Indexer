[CmdletBinding()]
param(
    [string]$EnvironmentPath = (Join-Path $PSScriptRoot "deploy\postgres\.env"),
    [string]$OutputPath,
    [ValidateRange(1, 200)]
    [int]$Limit = 40,
    [ValidateRange(5, 300)]
    [int]$StatementTimeoutSeconds = 120
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

if (-not (Test-Path -LiteralPath $EnvironmentPath -PathType Leaf)) {
    throw "PostgreSQL environment file was not found."
}

$composePath = Join-Path $PSScriptRoot "deploy\postgres\compose.yaml"
$sqlPath = Join-Path $PSScriptRoot "tools\postgres-gallery-plan.sql"
if (-not (Test-Path -LiteralPath $composePath -PathType Leaf)) {
    throw "PostgreSQL compose.yaml was not found."
}
if (-not (Test-Path -LiteralPath $sqlPath -PathType Leaf)) {
    throw "PostgreSQL gallery plan SQL was not found."
}
if (-not (Get-Command podman -ErrorAction SilentlyContinue)) {
    throw "Podman is required for the PostgreSQL gallery plan probe."
}

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
    "database-identity-source: running compose container POSTGRES_DB/POSTGRES_USER",
    "launcher-connection-secret: not-read",
    "statement-timeout-seconds: $StatementTimeoutSeconds",
    "note: connection string and credentials are intentionally omitted",
    ""
)
Set-Content -LiteralPath $OutputPath -Value $header -Encoding UTF8

$composeArgs = @(
    "compose",
    "--env-file", $EnvironmentPath,
    "-f", $composePath,
    "exec", "-T",
    "-e", "PAGE_LIMIT=$Limit",
    "-e", "STATEMENT_TIMEOUT_MS=$($StatementTimeoutSeconds * 1000)",
    "postgres",
    "sh", "-lc",
    'test -n "$POSTGRES_DB" && test -n "$POSTGRES_USER" && test -n "$POSTGRES_PASSWORD" && PGPASSWORD="$POSTGRES_PASSWORD" psql -X -v ON_ERROR_STOP=1 -A -t -U "$POSTGRES_USER" -d "$POSTGRES_DB" -v page_limit="$PAGE_LIMIT" -v statement_timeout_ms="$STATEMENT_TIMEOUT_MS"'
)

$probeOutput = Get-Content -LiteralPath $sqlPath -Raw |
    & podman @composeArgs 2>&1
$exitCode = $LASTEXITCODE
$probeOutput | Add-Content -LiteralPath $OutputPath -Encoding UTF8

if ($exitCode -ne 0) {
    throw "PostgreSQL gallery plan capture failed. The partial privacy-safe report was retained at '$OutputPath'."
}

Write-Host "PostgreSQL gallery plan capture passed."
Write-Host "report: $OutputPath"
Write-Host "The report uses the running PostgreSQL container's own database/user identity and omits the launcher connection string and credentials."
Write-Host "Attach it for WI-0104 query-plan review before adding indexes."
