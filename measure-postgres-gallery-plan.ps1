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

function Find-ConnectionKey {
    param(
        [Parameter(Mandatory)][System.Data.Common.DbConnectionStringBuilder]$Builder,
        [Parameter(Mandatory)][string]$Name
    )

    foreach ($key in $Builder.Keys) {
        $keyText = [string]$key
        if ([string]::Equals(
                $keyText,
                $Name,
                [System.StringComparison]::OrdinalIgnoreCase)) {
            return $keyText
        }
    }

    return $null
}

function Read-ConnectionIdentity {
    param([AllowNull()][string]$ConnectionString)

    if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
        return $null
    }

    $databaseNames = @(
        "Database",
        "Initial Catalog",
        "Database Name",
        "Db")
    $usernameNames = @(
        "Username",
        "User Name",
        "User ID",
        "UserID",
        "User Id")
    $maxWrapperDepth = 8
    $parsedKeyLayers = @()
    $currentConnectionString = $ConnectionString
    $wrapperDepth = 0

    for ($depth = 0; $depth -le $maxWrapperDepth; $depth++) {
        $builder = [System.Data.Common.DbConnectionStringBuilder]::new()
        try {
            $builder.ConnectionString = $currentConnectionString
        }
        catch {
            return [pscustomobject]@{
                DatabaseName = $null
                Username = $null
                ParsedKeyLayers = $parsedKeyLayers
                Format = if ($wrapperDepth -eq 0) { "direct" } else { "wrapped-connection-string" }
                WrapperDepth = $wrapperDepth
                WrapperLimitReached = $false
            }
        }

        $keys = @($builder.Keys | ForEach-Object { [string]$_ } | Sort-Object)
        $parsedKeyLayers += [pscustomobject]@{
            Depth = $depth
            Keys = $keys
        }

        $databaseName = Get-ConnectionValue -Builder $builder -Names $databaseNames
        $username = Get-ConnectionValue -Builder $builder -Names $usernameNames
        if (-not [string]::IsNullOrWhiteSpace($databaseName) -and
            -not [string]::IsNullOrWhiteSpace($username)) {
            return [pscustomobject]@{
                DatabaseName = $databaseName
                Username = $username
                ParsedKeyLayers = $parsedKeyLayers
                Format = if ($wrapperDepth -eq 0) { "direct" } else { "wrapped-connection-string" }
                WrapperDepth = $wrapperDepth
                WrapperLimitReached = $false
            }
        }

        # Operator/launcher values can be wrapped more than once as
        # ConnectionString="ConnectionString=...". Follow only that explicit
        # key, never arbitrary nested values, and stop after a bounded depth.
        $wrapperKey = Find-ConnectionKey -Builder $builder -Name "ConnectionString"
        if ([string]::IsNullOrWhiteSpace($wrapperKey)) {
            return [pscustomobject]@{
                DatabaseName = $databaseName
                Username = $username
                ParsedKeyLayers = $parsedKeyLayers
                Format = if ($wrapperDepth -eq 0) { "direct" } else { "wrapped-connection-string" }
                WrapperDepth = $wrapperDepth
                WrapperLimitReached = $false
            }
        }

        $nestedConnectionString = [string]$builder[$wrapperKey]
        if ([string]::IsNullOrWhiteSpace($nestedConnectionString)) {
            return [pscustomobject]@{
                DatabaseName = $databaseName
                Username = $username
                ParsedKeyLayers = $parsedKeyLayers
                Format = if ($wrapperDepth -eq 0) { "direct" } else { "wrapped-connection-string" }
                WrapperDepth = $wrapperDepth
                WrapperLimitReached = $false
            }
        }

        if ($depth -ge $maxWrapperDepth) {
            return [pscustomobject]@{
                DatabaseName = $databaseName
                Username = $username
                ParsedKeyLayers = $parsedKeyLayers
                Format = "wrapped-connection-string"
                WrapperDepth = $wrapperDepth
                WrapperLimitReached = $true
            }
        }

        $currentConnectionString = $nestedConnectionString
        $wrapperDepth = $depth + 1
    }

    throw "Unexpected gallery probe connection parsing state."
}

$scopeCandidates = @()
$connectionIdentity = $null
$connectionScope = $null
foreach ($scope in @("Process", "User", "Machine")) {
    $candidateConnectionString = [Environment]::GetEnvironmentVariable(
        $ConnectionEnvironmentVariable,
        $scope)
    if ([string]::IsNullOrWhiteSpace($candidateConnectionString)) {
        continue
    }

    $candidateIdentity = Read-ConnectionIdentity -ConnectionString $candidateConnectionString
    $scopeCandidates += [pscustomobject]@{
        Scope = $scope
        Identity = $candidateIdentity
    }

    if ($null -ne $candidateIdentity -and
        -not [string]::IsNullOrWhiteSpace([string]$candidateIdentity.DatabaseName) -and
        -not [string]::IsNullOrWhiteSpace([string]$candidateIdentity.Username)) {
        $connectionIdentity = $candidateIdentity
        $connectionScope = $scope
        break
    }
}

if ($null -eq $connectionIdentity) {
    if ($scopeCandidates.Count -eq 0) {
        throw "The PostgreSQL connection environment variable '$ConnectionEnvironmentVariable' is not set at Process, User, or Machine scope."
    }

    $layerSummaries = @(
        foreach ($candidate in $scopeCandidates) {
            foreach ($layer in $candidate.Identity.ParsedKeyLayers) {
                $keySummary = if ($layer.Keys.Count -eq 0) {
                    "<none>"
                }
                else {
                    $layer.Keys -join ", "
                }
                "$($candidate.Scope) depth $($layer.Depth): $keySummary"
            }
        })
    $layerSummary = if ($layerSummaries.Count -eq 0) {
        "<none>"
    }
    else {
        $layerSummaries -join " | "
    }
    $limitReachedScopes = @(
        $scopeCandidates |
            Where-Object { $_.Identity.WrapperLimitReached } |
            ForEach-Object { $_.Scope })
    $limitSummary = if ($limitReachedScopes.Count -eq 0) {
        ""
    }
    else {
        " Wrapper depth limit 8 was reached at scope(s): $($limitReachedScopes -join ', ')."
    }

    throw "The configured PostgreSQL connection string does not expose both a database and user name. Parsed key layers: $layerSummary.$limitSummary Values are intentionally omitted."
}

$databaseName = [string]$connectionIdentity.DatabaseName
$username = [string]$connectionIdentity.Username
$connectionFormat = [string]$connectionIdentity.Format
$connectionWrapperDepth = [int]$connectionIdentity.WrapperDepth
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
    "connection-environment-format: $connectionFormat",
    "connection-environment-wrapper-depth: $connectionWrapperDepth",
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
