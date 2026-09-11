[CmdletBinding()]
param(
    [string]$ConnectionEnvironmentVariable = "PHOTOIDENTITY_POSTGRES_CONNECTION_STRING",
    [string]$EnvironmentPath = (Join-Path $PSScriptRoot "deploy\postgres\.env"),
    [string]$OutputPath,
    [ValidateRange(1, 200)]
    [int]$Limit = 40,
    [ValidateRange(5, 300)]
    [int]$StatementTimeoutSeconds = 120
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Read-DotEnv {
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "PostgreSQL environment file was not found."
    }

    $settings = @{}
    foreach ($line in Get-Content -LiteralPath $Path) {
        $trimmed = $line.Trim()
        if ([string]::IsNullOrWhiteSpace($trimmed) -or $trimmed.StartsWith("#")) {
            continue
        }

        $separator = $trimmed.IndexOf("=")
        if ($separator -le 0) {
            continue
        }

        $name = $trimmed.Substring(0, $separator).Trim()
        $value = $trimmed.Substring($separator + 1).Trim()
        if (($value.StartsWith('"') -and $value.EndsWith('"')) -or
            ($value.StartsWith("'") -and $value.EndsWith("'"))) {
            $value = $value.Substring(1, $value.Length - 2)
        }

        $settings[$name] = $value
    }

    return $settings
}

function Get-ConnectionValue {
    param(
        [Parameter(Mandatory)][System.Data.Common.DbConnectionStringBuilder]$Builder,
        [Parameter(Mandatory)][string[]]$Names
    )

    foreach ($key in $Builder.Keys) {
        $keyText = [string]$key
        foreach ($name in $Names) {
            if ([string]::Equals(
                    $keyText,
                    $name,
                    [System.StringComparison]::OrdinalIgnoreCase)) {
                return [string]$Builder[$keyText]
            }
        }
    }

    return $null
}

function Read-ConnectionIdentity {
    param([AllowNull()][string]$ConnectionString)

    if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
        return $null
    }

    $builder = [System.Data.Common.DbConnectionStringBuilder]::new()
    try {
        $builder.ConnectionString = $ConnectionString
    }
    catch {
        return [pscustomobject]@{
            DatabaseName = $null
            Username = $null
            ParsedKeys = @()
        }
    }

    return [pscustomobject]@{
        DatabaseName = Get-ConnectionValue -Builder $builder -Names @(
            "Database",
            "Initial Catalog",
            "Database Name",
            "Db")
        Username = Get-ConnectionValue -Builder $builder -Names @(
            "Username",
            "User Name",
            "User ID",
            "UserID",
            "User Id")
        ParsedKeys = @($builder.Keys | ForEach-Object { [string]$_ } | Sort-Object)
    }
}

$processConnectionString = [Environment]::GetEnvironmentVariable(
    $ConnectionEnvironmentVariable,
    "Process")
$userConnectionString = [Environment]::GetEnvironmentVariable(
    $ConnectionEnvironmentVariable,
    "User")

$processIdentity = Read-ConnectionIdentity -ConnectionString $processConnectionString
$userIdentity = Read-ConnectionIdentity -ConnectionString $userConnectionString

$connectionIdentity = $null
$connectionScope = $null
if ($null -ne $processIdentity -and
    -not [string]::IsNullOrWhiteSpace([string]$processIdentity.DatabaseName) -and
    -not [string]::IsNullOrWhiteSpace([string]$processIdentity.Username)) {
    $connectionIdentity = $processIdentity
    $connectionScope = "Process"
}
elseif ($null -ne $userIdentity -and
    -not [string]::IsNullOrWhiteSpace([string]$userIdentity.DatabaseName) -and
    -not [string]::IsNullOrWhiteSpace([string]$userIdentity.Username)) {
    $connectionIdentity = $userIdentity
    $connectionScope = "User"
}

if ($null -eq $connectionIdentity) {
    if ([string]::IsNullOrWhiteSpace($processConnectionString) -and
        [string]::IsNullOrWhiteSpace($userConnectionString)) {
        throw "The PostgreSQL connection environment variable '$ConnectionEnvironmentVariable' is not set at Process or User scope."
    }

    $parsedKeys = @()
    if ($null -ne $processIdentity) {
        $parsedKeys += $processIdentity.ParsedKeys
    }
    if ($null -ne $userIdentity) {
        $parsedKeys += $userIdentity.ParsedKeys
    }
    $parsedKeys = @($parsedKeys | Sort-Object -Unique)
    $keySummary = if ($parsedKeys.Count -eq 0) {
        "<none>"
    }
    else {
        $parsedKeys -join ", "
    }

    throw "The configured PostgreSQL connection string does not expose both a database and user name. Parsed key names: $keySummary. Values are intentionally omitted."
}

$databaseName = [string]$connectionIdentity.DatabaseName
$username = [string]$connectionIdentity.Username
if ($databaseName -notmatch '^[A-Za-z0-9_]+$') {
    throw "The configured PostgreSQL database name contains unsupported characters for this diagnostic."
}

$settings = Read-DotEnv -Path $EnvironmentPath
if (-not $settings.ContainsKey("PHOTOIDENTITY_POSTGRES_USER")) {
    throw "PHOTOIDENTITY_POSTGRES_USER is missing from the PostgreSQL environment file."
}
if ($username -cne [string]$settings["PHOTOIDENTITY_POSTGRES_USER"]) {
    throw "The configured runtime PostgreSQL user does not match deploy/postgres/.env. The plan probe refuses to pass credentials on the command line."
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
    "connection-environment-variable: $ConnectionEnvironmentVariable",
    "connection-environment-scope: $connectionScope",
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
    "-e", "TARGET_DATABASE=$databaseName",
    "postgres",
    "sh", "-lc",
    'PGPASSWORD="$POSTGRES_PASSWORD" psql -X -v ON_ERROR_STOP=1 -A -t -U "$POSTGRES_USER" -d "$TARGET_DATABASE" -v page_limit="$PAGE_LIMIT" -v statement_timeout_ms="$STATEMENT_TIMEOUT_MS"'
)

# Pass only non-secret diagnostic values through container environment variables.
$composeArgs = $composeArgs[0..8] + @(
    "-e", "PAGE_LIMIT=$Limit",
    "-e", "STATEMENT_TIMEOUT_MS=$($StatementTimeoutSeconds * 1000)"
) + $composeArgs[9..($composeArgs.Count - 1)]

$probeOutput = Get-Content -LiteralPath $sqlPath -Raw |
    & podman @composeArgs 2>&1
$exitCode = $LASTEXITCODE
$probeOutput | Add-Content -LiteralPath $OutputPath -Encoding UTF8

if ($exitCode -ne 0) {
    throw "PostgreSQL gallery plan capture failed. The partial privacy-safe report was retained at '$OutputPath'."
}

Write-Host "PostgreSQL gallery plan capture passed."
Write-Host "report: $OutputPath"
Write-Host "The report omits the connection string and credentials. Attach it for WI-0104 query-plan review before adding indexes."
