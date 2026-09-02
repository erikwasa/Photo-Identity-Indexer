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

function Test-PostgresProtocol {
    param(
        [Parameter(Mandatory = $true)][string]$HostName,
        [Parameter(Mandatory = $true)][int]$Port,
        [Parameter(Mandatory = $true)][string]$Username,
        [Parameter(Mandatory = $true)][string]$Database
    )

    $client = [System.Net.Sockets.TcpClient]::new()
    try {
        $connectTask = $client.ConnectAsync($HostName, $Port)
        if (-not $connectTask.Wait(2000) -or -not $client.Connected) {
            return $false
        }

        $separator = [char]0
        $payloadText = "user" + $separator + $Username + $separator +
            "database" + $separator + $Database + $separator + $separator
        $stream = $client.GetStream()
        $payload = [System.Text.Encoding]::UTF8.GetBytes($payloadText)
        $length = 8 + $payload.Length
        $lengthBytes = [System.BitConverter]::GetBytes(
            [System.Net.IPAddress]::HostToNetworkOrder([int]$length))
        $protocolBytes = [System.BitConverter]::GetBytes(
            [System.Net.IPAddress]::HostToNetworkOrder([int]196608))

        $stream.Write($lengthBytes, 0, $lengthBytes.Length)
        $stream.Write($protocolBytes, 0, $protocolBytes.Length)
        $stream.Write($payload, 0, $payload.Length)
        $stream.Flush()

        $first = New-Object byte[] 1
        $readTask = $stream.ReadAsync($first, 0, 1)
        if (-not $readTask.Wait(3000) -or $readTask.Result -ne 1) {
            return $false
        }

        return $first[0] -in @(
            [byte][char]'R',
            [byte][char]'E',
            [byte][char]'N'
        )
    }
    catch {
        return $false
    }
    finally {
        $client.Dispose()
    }
}

function Get-PodmanVersionInfo {
    param([Parameter(Mandatory = $true)]$PodmanCommand)

    $clientVersion = "unknown"
    $serverVersion = "unknown"

    try {
        $clientOutput = @(& $PodmanCommand.Source version --format "{{.Client.Version}}" 2>$null)
        if ($LASTEXITCODE -eq 0 -and $clientOutput.Count -gt 0) {
            $clientVersion = (($clientOutput -join " ").Trim())
        }
    }
    catch {
    }

    try {
        $serverOutput = @(& $PodmanCommand.Source version --format "{{.Server.Version}}" 2>$null)
        if ($LASTEXITCODE -eq 0 -and $serverOutput.Count -gt 0) {
            $serverVersion = (($serverOutput -join " ").Trim())
        }
    }
    catch {
    }

    return [pscustomobject]@{
        Client = $clientVersion
        Server = $serverVersion
    }
}

function Test-KnownPodman6WindowsForwardingRegression {
    param([Parameter(Mandatory = $true)]$VersionInfo)

    return (
        ([string]$VersionInfo.Client) -match '^6\.0\.' -or
        ([string]$VersionInfo.Server) -match '^6\.0\.')
}

function Get-PodmanUserModeNetworking {
    param([Parameter(Mandatory = $true)]$PodmanCommand)

    try {
        $output = @(& $PodmanCommand.Source machine inspect --format "{{.UserModeNetworking}}" 2>$null)
        if ($LASTEXITCODE -eq 0 -and $output.Count -gt 0) {
            return (($output -join " ").Trim().ToLowerInvariant())
        }
    }
    catch {
    }

    return "unknown"
}

function New-PostgresConnectionString {
    param(
        [Parameter(Mandatory = $true)][string]$HostName,
        [Parameter(Mandatory = $true)][int]$Port,
        [Parameter(Mandatory = $true)]$Settings
    )

    return (
        "Host=$HostName;" +
        "Port=$Port;" +
        "Database=$($Settings["PHOTOIDENTITY_POSTGRES_DATABASE"]);" +
        "Username=$($Settings["PHOTOIDENTITY_POSTGRES_USER"]);" +
        "Password=$($Settings["PHOTOIDENTITY_POSTGRES_PASSWORD"]);" +
        "SSL Mode=Disable;GSS Encryption Mode=Disable;" +
        "Pooling=false;Timeout=5;Command Timeout=10")
}

function Invoke-LivePostgresTest {
    param([Parameter(Mandatory = $true)][string]$ConnectionString)

    $previous = [Environment]::GetEnvironmentVariable(
        "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING",
        "Process")

    try {
        [Environment]::SetEnvironmentVariable(
            "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING",
            $ConnectionString,
            "Process")

        & dotnet test (Join-Path $PSScriptRoot "tests\PhotoIdentity.Persistence.Tests\PhotoIdentity.Persistence.Tests.csproj") --configuration Release --filter "FullyQualifiedName~PostgresCatalogueDatabaseTests.InitializeAsync_IsVersionedAndIdempotent_WhenLivePostgresIsConfigured" | Out-Host
        $testExitCode = $LASTEXITCODE
        return [int]$testExitCode
    }
    finally {
        [Environment]::SetEnvironmentVariable(
            "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING",
            $previous,
            "Process")
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
$podmanVersions = $null
if ($null -ne $podman) {
    $podmanVersions = Get-PodmanVersionInfo -PodmanCommand $podman
    Write-Host "Podman versions: client=$($podmanVersions.Client), server=$($podmanVersions.Server)"
}

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

        $authCheck = @(& $podman.Source exec $containerId sh -c 'PGPASSWORD="$POSTGRES_PASSWORD" psql -h 127.0.0.1 -U "$POSTGRES_USER" -d "$POSTGRES_DB" -v ON_ERROR_STOP=1 -tAc "SELECT 1"' 2>$null)
        if ($LASTEXITCODE -ne 0 -or (($authCheck -join " ").Trim()) -ne "1") {
            throw "PostgreSQL is ready, but an authenticated SELECT 1 failed inside the container. The persisted database credentials may not match deploy/postgres/.env. If the password was changed after the volume was initialized, either restore the original password or perform the documented destructive development reset before rerunning verification."
        }

        Write-Host "Authenticated PostgreSQL check inside container passed."

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

$hostPort = [int]$settings["PHOTOIDENTITY_POSTGRES_PORT"]
$databaseName = [string]$settings["PHOTOIDENTITY_POSTGRES_DATABASE"]
$userName = [string]$settings["PHOTOIDENTITY_POSTGRES_USER"]

$localhostProtocol = Test-PostgresProtocol -HostName "127.0.0.1" -Port $hostPort -Username $userName -Database $databaseName
$machineAddresses = if ($null -ne $podman) {
    Get-PodmanMachineIpv4Addresses -PodmanCommand $podman
}
else {
    @()
}
$directProtocolAddress = $null
foreach ($candidate in $machineAddresses) {
    if (Test-PostgresProtocol -HostName $candidate -Port $hostPort -Username $userName -Database $databaseName) {
        $directProtocolAddress = $candidate
        break
    }
}

if (-not $localhostProtocol) {
    $userModeNetworking = if ($null -ne $podman) {
        Get-PodmanUserModeNetworking -PodmanCommand $podman
    }
    else {
        "unknown"
    }

    Write-Host "Windows localhost TCP port is open, but a PostgreSQL startup packet did not receive a valid PostgreSQL response."
    Write-Host "Podman user-mode networking: $userModeNetworking"

    if ($null -ne $podmanVersions -and (Test-KnownPodman6WindowsForwardingRegression -VersionInfo $podmanVersions)) {
        throw "PostgreSQL is authenticated and healthy inside the container, but Windows localhost is not carrying the PostgreSQL protocol. Podman client/server version is $($podmanVersions.Client)/$($podmanVersions.Server). This matches the open Podman 6.0.x Windows/WSL port-forwarding regression (Podman issue #29377; Microsoft WSL issue #41204). Do not change Photo Identity database code for this failure. Use the known-good Podman 5.8.5 WSL baseline (Podman Desktop 1.28.3 shipped 5.8.5) or wait for an upstream Podman fix, recreate the disposable local PostgreSQL runtime if needed, and rerun verification."
    }

    if ($userModeNetworking -eq "false") {
        if ($null -ne $directProtocolAddress) {
            Write-Host "Direct Podman-machine PostgreSQL protocol check passed at $($directProtocolAddress):$hostPort."
        }

        throw "PostgreSQL is authenticated and healthy inside the container, but the default Windows/WSL network path is not carrying the PostgreSQL protocol correctly. Podman user-mode networking is disabled. Run: podman machine stop; podman machine set --user-mode-networking=true; podman machine start. Then rerun verify-postgres.ps1."
    }

    if ($null -ne $directProtocolAddress) {
        Write-Host "Direct Podman-machine PostgreSQL protocol check passed at $($directProtocolAddress):$hostPort."
        throw "The PostgreSQL server is reachable through the Podman-machine address but not through Windows localhost even though user-mode networking is enabled. Restart the Podman machine and rerun verification. If the problem persists, update Podman Desktop/Podman before continuing."
    }

    throw "The Windows localhost port accepts TCP, but no valid PostgreSQL protocol response was received. Podman user-mode networking is enabled or could not be determined. Restart/update Podman and check Windows/Hyper-V firewall policy before continuing."
}

Write-Host "Windows localhost PostgreSQL protocol check passed."

$hostConnectionString = New-PostgresConnectionString -HostName "127.0.0.1" -Port $hostPort -Settings $settings
$testExitCode = Invoke-LivePostgresTest -ConnectionString $hostConnectionString
if ($testExitCode -eq 0) {
    Write-Host "PostgreSQL runtime verification passed."
    exit 0
}

if ($null -ne $directProtocolAddress) {
    Write-Host "Localhost Npgsql verification failed. Retrying against Podman-machine address $($directProtocolAddress):$hostPort for diagnosis only."
    $directConnectionString = New-PostgresConnectionString -HostName $directProtocolAddress -Port $hostPort -Settings $settings
    $directExitCode = Invoke-LivePostgresTest -ConnectionString $directConnectionString

    if ($directExitCode -eq 0) {
        $userModeNetworking = if ($null -ne $podman) {
            Get-PodmanUserModeNetworking -PodmanCommand $podman
        }
        else {
            "unknown"
        }

        if ($userModeNetworking -eq "false") {
            throw "The PostgreSQL migration test succeeds through the Podman-machine address but fails through Windows localhost. Enable Podman WSL user-mode networking with: podman machine stop; podman machine set --user-mode-networking=true; podman machine start. Then rerun verify-postgres.ps1. Do not use the dynamic Podman-machine IP as the permanent application connection string."
        }

        throw "The PostgreSQL migration test succeeds through the Podman-machine address but fails through Windows localhost even though Podman reports user-mode networking=$userModeNetworking. Restart/update Podman and rerun verification; do not use the dynamic machine IP as permanent configuration."
    }
}

throw "Windows localhost PostgreSQL protocol verification passed, but the live PostgreSQL migration test failed. Review the dotnet test failure above; this is not classified as a Podman/WSL networking failure."
