[CmdletBinding()]
param(
    [string]$EnvironmentPath = (Join-Path $PSScriptRoot "deploy\postgres\.env"),
    [switch]$SkipContainerStart
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$composeDirectory = Join-Path $PSScriptRoot "deploy\postgres"
$composePath = Join-Path $composeDirectory "compose.yaml"
$environmentExamplePath = Join-Path $composeDirectory ".env.example"

function Test-TcpPortOpen {
    param(
        [Parameter(Mandatory = $true)][string]$HostName,
        [Parameter(Mandatory = $true)][int]$Port
    )

    $client = [System.Net.Sockets.TcpClient]::new()
    try {
        $task = $client.ConnectAsync($HostName, $Port)
        if (-not $task.Wait(2000)) {
            return $false
        }

        return $client.Connected
    }
    catch {
        return $false
    }
    finally {
        $client.Dispose()
    }
}

function Get-PodmanWslNetworkingMode {
    param([Parameter(Mandatory = $true)]$PodmanCommand)

    try {
        $output = @(& $PodmanCommand.Source machine ssh wslinfo --networking-mode 2>$null)
        if ($LASTEXITCODE -eq 0 -and $output.Count -gt 0) {
            return (($output -join " ").Trim().ToLowerInvariant())
        }
    }
    catch {
    }

    return "unknown"
}

function Get-WslConfigDiagnostics {
    $path = if ([string]::IsNullOrWhiteSpace($env:USERPROFILE)) {
        $null
    }
    else {
        Join-Path $env:USERPROFILE ".wslconfig"
    }

    $networkingMode = $null
    $localhostForwarding = $null

    if ($null -ne $path -and (Test-Path -LiteralPath $path -PathType Leaf)) {
        $raw = Get-Content -LiteralPath $path -Raw

        $networkMatch = [regex]::Match(
            $raw,
            "(?im)^\s*networkingMode\s*=\s*([^#;\r\n]+)")
        if ($networkMatch.Success) {
            $networkingMode = $networkMatch.Groups[1].Value.Trim().ToLowerInvariant()
        }

        $localhostMatch = [regex]::Match(
            $raw,
            "(?im)^\s*localhostForwarding\s*=\s*([^#;\r\n]+)")
        if ($localhostMatch.Success) {
            $localhostForwarding = $localhostMatch.Groups[1].Value.Trim().ToLowerInvariant()
        }
    }

    return [pscustomobject]@{
        Path = $path
        NetworkingMode = $networkingMode
        LocalhostForwarding = $localhostForwarding
    }
}

function Get-PodmanMachineIpv4Addresses {
    param([Parameter(Mandatory = $true)]$PodmanCommand)

    try {
        $output = @(& $PodmanCommand.Source machine ssh hostname -I 2>$null)
        if ($LASTEXITCODE -ne 0) {
            return @()
        }

        $tokens = (($output -join " ") -split "\s+") |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

        return @($tokens | Where-Object {
            [System.Net.IPAddress]$address = $null
            [System.Net.IPAddress]::TryParse($_, [ref]$address) -and
            $address.AddressFamily -eq [System.Net.Sockets.AddressFamily]::InterNetwork
        })
    }
    catch {
        return @()
    }
}

function Read-DotEnv {
    param([Parameter(Mandatory = $true)][string]$Path)

    $values = @{}
    foreach ($line in Get-Content -LiteralPath $Path) {
        $trimmed = $line.Trim()
        if ([string]::IsNullOrWhiteSpace($trimmed) -or $trimmed.StartsWith("#")) {
            continue
        }

        $separator = $trimmed.IndexOf("=")
        if ($separator -le 0) {
            throw "Invalid .env line in '$Path': $trimmed"
        }

        $name = $trimmed.Substring(0, $separator).Trim()
        $value = $trimmed.Substring($separator + 1).Trim()
        $values[$name] = $value
    }

    return $values
}

if (-not (Test-Path -LiteralPath $composePath -PathType Leaf)) {
    throw "PostgreSQL compose definition was not found: $composePath"
}

if (-not (Test-Path -LiteralPath $EnvironmentPath -PathType Leaf)) {
    throw "PostgreSQL private environment file was not found: $EnvironmentPath. Copy '$environmentExamplePath' to '.env' and replace the placeholder password."
}

$settings = Read-DotEnv -Path $EnvironmentPath
foreach ($required in @(
    "PHOTOIDENTITY_POSTGRES_DATABASE",
    "PHOTOIDENTITY_POSTGRES_USER",
    "PHOTOIDENTITY_POSTGRES_PASSWORD",
    "PHOTOIDENTITY_POSTGRES_PORT")) {
    if (-not $settings.ContainsKey($required) -or
        [string]::IsNullOrWhiteSpace([string]$settings[$required])) {
        throw "Required PostgreSQL setting '$required' is missing from $EnvironmentPath."
    }
}

if ([string]$settings["PHOTOIDENTITY_POSTGRES_PASSWORD"] -eq
    "replace-with-a-private-password") {
    throw "Replace the placeholder PostgreSQL password in $EnvironmentPath before starting the service."
}

$publishedPort = @()
$podman = Get-Command podman -ErrorAction SilentlyContinue

if (-not $SkipContainerStart) {
    if ($null -eq $podman) {
        throw "Podman was not found on PATH."
    }

    Push-Location $composeDirectory
    try {
        & $podman.Source compose up -d
        if ($LASTEXITCODE -ne 0) {
            throw "podman compose up failed with code $LASTEXITCODE."
        }

        $containerId = (& $podman.Source compose ps -q postgres).Trim()
        if ([string]::IsNullOrWhiteSpace($containerId)) {
            throw "Podman Compose did not return the PostgreSQL container id."
        }

        $ready = $false
        for ($attempt = 0; $attempt -lt 30; $attempt++) {
            & $podman.Source exec $containerId pg_isready -U ([string]$settings["PHOTOIDENTITY_POSTGRES_USER"]) -d ([string]$settings["PHOTOIDENTITY_POSTGRES_DATABASE"]) *> $null
            if ($LASTEXITCODE -eq 0) {
                $ready = $true
                break
            }

            Start-Sleep -Seconds 1
        }

        if (-not $ready) {
            throw "PostgreSQL did not report ready through pg_isready."
        }

        $publishedPort = @(& $podman.Source port $containerId "5432/tcp" 2>$null)
        if ($LASTEXITCODE -ne 0 -or $publishedPort.Count -eq 0) {
            throw "Podman did not report a published PostgreSQL port for container port 5432/tcp."
        }

        Write-Host "Podman published PostgreSQL port: $($publishedPort -join ', ')"
    }
    finally {
        Pop-Location
    }
}

$hostPort = [int]$settings["PHOTOIDENTITY_POSTGRES_PORT"]
if (-not (Test-TcpPortOpen -HostName "127.0.0.1" -Port $hostPort)) {
    $mappingDetail = if ($publishedPort.Count -gt 0) {
        $publishedPort -join ", "
    }
    else {
        "not available"
    }

    $networkingMode = if ($null -ne $podman) {
        Get-PodmanWslNetworkingMode -PodmanCommand $podman
    }
    else {
        "unknown"
    }
    $wslConfig = Get-WslConfigDiagnostics

    Write-Host "WSL networking mode reported by Podman machine: $networkingMode"
    if ($null -ne $wslConfig.Path -and (Test-Path -LiteralPath $wslConfig.Path -PathType Leaf)) {
        Write-Host "WSL config: $($wslConfig.Path)"
        Write-Host "WSL config networkingMode: $($wslConfig.NetworkingMode ?? '<not set>')"
        Write-Host "WSL config localhostForwarding: $($wslConfig.LocalhostForwarding ?? '<not set>')"
    }
    else {
        Write-Host "WSL config: <not present>"
    }

    $reachableMachineAddress = $null
    if ($null -ne $podman) {
        foreach ($candidate in (Get-PodmanMachineIpv4Addresses -PodmanCommand $podman)) {
            if (Test-TcpPortOpen -HostName $candidate -Port $hostPort) {
                $reachableMachineAddress = $candidate
                break
            }
        }
    }

    if ($null -ne $reachableMachineAddress) {
        Write-Host "Windows can reach the PostgreSQL port through Podman machine address ${reachableMachineAddress}:$hostPort, but that address is not used as application configuration because it can change after WSL restart."
    }

    if ($wslConfig.LocalhostForwarding -eq "false" -and $networkingMode -ne "mirrored") {
        throw "PostgreSQL is healthy, but WSL localhost forwarding is explicitly disabled. Podman reported mapping: $mappingDetail. Set localhostForwarding=true (or remove the false override) in $($wslConfig.Path), run 'wsl --shutdown', restart the Podman machine, and rerun this verification."
    }

    if ($networkingMode -eq "mirrored") {
        throw "PostgreSQL is healthy, but Windows localhost is not receiving the Podman-published port while WSL networking mode is mirrored. Podman reported mapping: $mappingDetail. Photo Identity requires a stable Windows-localhost database endpoint. Use WSL NAT networking with localhostForwarding=true for this environment, then run 'wsl --shutdown', restart the Podman machine, and rerun this verification."
    }

    throw "PostgreSQL is healthy inside Podman, but Windows cannot connect to 127.0.0.1:$hostPort. Podman reported mapping: $mappingDetail; WSL networking mode: $networkingMode. WSL normally forwards Linux-bound ports to Windows localhost. Run 'wsl --shutdown', restart the Podman machine, and rerun verification. If it still fails, check WSL Settings/.wslconfig for localhost forwarding and Windows/Hyper-V firewall policy."
}

$hostConnectionString =
    "Host=127.0.0.1;" +
    "Port=$($settings["PHOTOIDENTITY_POSTGRES_PORT"]);" +
    "Database=$($settings["PHOTOIDENTITY_POSTGRES_DATABASE"]);" +
    "Username=$($settings["PHOTOIDENTITY_POSTGRES_USER"]);" +
    "Password=$($settings["PHOTOIDENTITY_POSTGRES_PASSWORD"]);" +
    "Pooling=false;Timeout=5;Command Timeout=10"

$previous = [Environment]::GetEnvironmentVariable(
    "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING",
    "Process")

try {
    [Environment]::SetEnvironmentVariable(
        "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING",
        $hostConnectionString,
        "Process")

    & dotnet test (Join-Path $PSScriptRoot "tests\PhotoIdentity.Persistence.Tests\PhotoIdentity.Persistence.Tests.csproj") --configuration Release --filter "FullyQualifiedName~PostgresCatalogueDatabaseTests.InitializeAsync_IsVersionedAndIdempotent_WhenLivePostgresIsConfigured"
    if ($LASTEXITCODE -ne 0) {
        throw "PostgreSQL verification test failed with code $LASTEXITCODE."
    }
}
finally {
    [Environment]::SetEnvironmentVariable(
        "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING",
        $previous,
        "Process")
}

Write-Host "PostgreSQL runtime verification passed."
