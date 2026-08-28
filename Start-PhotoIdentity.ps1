[CmdletBinding()]
param(
    [string]$ConfigurationPath,
    [string]$PublishPathOverride,
    [switch]$NoBrowser,
    [ValidateRange(1, 300)]
    [int]$StartupTimeoutSeconds = 45
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$SupportedSettings = @(
    "PhotoIdentity__DatabasePath",
    "PhotoIdentity__ArchiveAnalysisOutputRoot",
    "PhotoIdentity__ReviewProxyRoot",
    "PhotoIdentity__ReviewProxyProfileId",
    "PhotoIdentity__ReviewProxyMaximumLongEdge",
    "PhotoIdentity__ReviewProxyJpegQuality",
    "PhotoIdentity__ArchiveHydration__MinimumFreeSpaceReserveBytes",
    "PhotoIdentity__ArchiveHydration__MaximumManagedHydrationBytes",
    "PhotoIdentity__ArchiveHydration__MaximumConcurrentOperations",
    "PhotoIdentity__GeoNames__Username",
    "PhotoIdentity__GeoNames__BaseUrl",
    "PhotoIdentity__GeoNames__Language",
    "PhotoIdentity__GeoNames__MinimumRequestIntervalMilliseconds",
    "PhotoIdentity__GeoNames__AutomaticEnrichmentEnabled",
    "PhotoIdentity__GeoNames__AutomaticMinimumRequestIntervalMilliseconds",
    "PhotoIdentity__GeoNames__AutomaticIdlePollIntervalMilliseconds",
    "PhotoIdentity__RepositoryRoot",
    "PhotoIdentity__ModelDirectory"
)

function Assert-LauncherSettingValue {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,
        [Parameter(Mandatory = $true)]
        [string]$Value
    )

    switch ($Name) {
        "PhotoIdentity__GeoNames__AutomaticEnrichmentEnabled" {
            [bool]$parsed = $false
            if (-not [bool]::TryParse($Value, [ref]$parsed)) {
                throw "$Name must be true or false."
            }
        }
        "PhotoIdentity__GeoNames__AutomaticMinimumRequestIntervalMilliseconds" {
            [int]$parsed = 0
            if (-not [int]::TryParse($Value, [ref]$parsed)) {
                throw "$Name must be an integer millisecond value."
            }
            if ($parsed -lt 0 -or $parsed -gt 600000) {
                throw "$Name must be between 0 and 600000 milliseconds (0 to 10 minutes)."
            }
        }
        "PhotoIdentity__GeoNames__AutomaticIdlePollIntervalMilliseconds" {
            [int]$parsed = 0
            if (-not [int]::TryParse($Value, [ref]$parsed)) {
                throw "$Name must be an integer millisecond value."
            }
            if ($parsed -lt 1000 -or $parsed -gt 600000) {
                throw "$Name must be between 1000 and 600000 milliseconds (1 second to 10 minutes)."
            }
        }
    }
}

function Resolve-LauncherConfigurationPath {
    if (-not [string]::IsNullOrWhiteSpace($ConfigurationPath)) {
        $candidate = [Environment]::ExpandEnvironmentVariables($ConfigurationPath)
        if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            throw "Launcher configuration does not exist: $candidate"
        }

        return (Resolve-Path -LiteralPath $candidate).Path
    }

    if (-not [string]::IsNullOrWhiteSpace($env:PHOTOIDENTITY_LAUNCHER_CONFIG)) {
        $candidate = [Environment]::ExpandEnvironmentVariables($env:PHOTOIDENTITY_LAUNCHER_CONFIG)
        if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            throw "PHOTOIDENTITY_LAUNCHER_CONFIG points to a missing file: $candidate"
        }

        return (Resolve-Path -LiteralPath $candidate).Path
    }

    $adjacent = Join-Path $PSScriptRoot "PhotoIdentity.launcher.json"
    if (Test-Path -LiteralPath $adjacent -PathType Leaf) {
        return (Resolve-Path -LiteralPath $adjacent).Path
    }

    if (-not [string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
        $local = Join-Path $env:LOCALAPPDATA "PhotoIdentity\launcher.json"
        if (Test-Path -LiteralPath $local -PathType Leaf) {
            return (Resolve-Path -LiteralPath $local).Path
        }
    }

    return $null
}

function Resolve-ConfiguredPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Value,
        [string]$BaseDirectory
    )

    $expanded = [Environment]::ExpandEnvironmentVariables($Value.Trim())
    if ([string]::IsNullOrWhiteSpace($expanded)) {
        throw "Configured path may not be empty."
    }

    if ([IO.Path]::IsPathRooted($expanded)) {
        return [IO.Path]::GetFullPath($expanded)
    }

    if ([string]::IsNullOrWhiteSpace($BaseDirectory)) {
        return [IO.Path]::GetFullPath((Join-Path $PSScriptRoot $expanded))
    }

    return [IO.Path]::GetFullPath((Join-Path $BaseDirectory $expanded))
}


function Read-MobileAccessConfiguration {
    param(
        $ParsedConfiguration,
        [string]$ConfigurationDirectory
    )

    $disabled = [pscustomobject]@{
        Enabled = $false
        ListenUri = $null
        PhoneUri = $null
        CertificatePath = $null
        CertificatePasswordEnvironmentVariable = $null
    }

    if ($null -eq $ParsedConfiguration.PSObject.Properties["mobileAccess"] -or
        $null -eq $ParsedConfiguration.mobileAccess) {
        return $disabled
    }

    $mobile = $ParsedConfiguration.mobileAccess
    [bool]$enabled = $false
    if ($null -eq $mobile.PSObject.Properties["enabled"] -or
        -not [bool]::TryParse(([string]$mobile.enabled), [ref]$enabled)) {
        throw "mobileAccess.enabled must be true or false."
    }

    if (-not $enabled) {
        return $disabled
    }

    if ($null -eq $mobile.PSObject.Properties["listenUrl"] -or
        [string]::IsNullOrWhiteSpace([string]$mobile.listenUrl)) {
        throw "mobileAccess.listenUrl is required when mobile access is enabled."
    }

    $listenUrl = ([string]$mobile.listenUrl).Trim()
    try {
        $listenUri = [Uri]$listenUrl
    }
    catch {
        throw "mobileAccess.listenUrl is invalid: $listenUrl"
    }

    if (-not $listenUri.IsAbsoluteUri -or $listenUri.Scheme -ne "https") {
        throw "mobileAccess.listenUrl must be an absolute HTTPS URL."
    }
    if (-not [string]::IsNullOrEmpty($listenUri.UserInfo) -or
        -not [string]::IsNullOrEmpty($listenUri.Query) -or
        -not [string]::IsNullOrEmpty($listenUri.Fragment) -or
        $listenUri.AbsolutePath -ne "/") {
        throw "mobileAccess.listenUrl may contain only scheme, host and port."
    }

    [Net.IPAddress]$listenAddress = $null
    if (-not [Net.IPAddress]::TryParse($listenUri.Host, [ref]$listenAddress) -or
        [Net.IPAddress]::IsLoopback($listenAddress) -or
        $listenAddress.Equals([Net.IPAddress]::Any) -or
        $listenAddress.Equals([Net.IPAddress]::IPv6Any)) {
        throw "mobileAccess.listenUrl must use a specific non-loopback IP address assigned for trusted-LAN access. Use mobileAccess.phoneUrl for a DNS hostname."
    }

    if ($null -eq $mobile.PSObject.Properties["certificatePath"] -or
        [string]::IsNullOrWhiteSpace([string]$mobile.certificatePath)) {
        throw "mobileAccess.certificatePath is required when mobile access is enabled."
    }

    $certificatePath = Resolve-ConfiguredPath -Value ([string]$mobile.certificatePath) -BaseDirectory $ConfigurationDirectory
    if (-not (Test-Path -LiteralPath $certificatePath -PathType Leaf)) {
        throw "mobileAccess.certificatePath does not exist. Provide a PFX certificate file outside the Photo Identity package."
    }

    $certificatePasswordEnvironmentVariable = $null
    if ($null -ne $mobile.PSObject.Properties["certificatePasswordEnvironmentVariable"] -and
        -not [string]::IsNullOrWhiteSpace([string]$mobile.certificatePasswordEnvironmentVariable)) {
        $certificatePasswordEnvironmentVariable = ([string]$mobile.certificatePasswordEnvironmentVariable).Trim()
        if ($certificatePasswordEnvironmentVariable -notmatch "^[A-Za-z_][A-Za-z0-9_]*$") {
            throw "mobileAccess.certificatePasswordEnvironmentVariable must be an environment-variable name."
        }

        $certificatePassword = [Environment]::GetEnvironmentVariable($certificatePasswordEnvironmentVariable, "Process")
        if ([string]::IsNullOrEmpty($certificatePassword)) {
            throw "The environment variable '$certificatePasswordEnvironmentVariable' configured for the mobile certificate password is not set."
        }
    }

    $phoneUri = $listenUri
    if ($null -ne $mobile.PSObject.Properties["phoneUrl"] -and
        -not [string]::IsNullOrWhiteSpace([string]$mobile.phoneUrl)) {
        $phoneUrl = ([string]$mobile.phoneUrl).Trim()
        try {
            $phoneUri = [Uri]$phoneUrl
        }
        catch {
            throw "mobileAccess.phoneUrl is invalid: $phoneUrl"
        }

        if (-not $phoneUri.IsAbsoluteUri -or $phoneUri.Scheme -ne "https" -or
            -not [string]::IsNullOrEmpty($phoneUri.UserInfo) -or
            -not [string]::IsNullOrEmpty($phoneUri.Query) -or
            -not [string]::IsNullOrEmpty($phoneUri.Fragment) -or
            $phoneUri.AbsolutePath -ne "/") {
            throw "mobileAccess.phoneUrl must be an absolute HTTPS origin without a path, query string or fragment."
        }
        if ($phoneUri.Port -ne $listenUri.Port) {
            throw "mobileAccess.phoneUrl must use the same port as mobileAccess.listenUrl."
        }
    }

    return [pscustomobject]@{
        Enabled = $true
        ListenUri = $listenUri
        PhoneUri = $phoneUri
        CertificatePath = $certificatePath
        CertificatePasswordEnvironmentVariable = $certificatePasswordEnvironmentVariable
    }
}

function Read-LauncherConfiguration {
    $configurationFile = Resolve-LauncherConfigurationPath
    $localApplicationRoot = if ([string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
        Join-Path $PSScriptRoot ".photoidentity"
    }
    else {
        Join-Path $env:LOCALAPPDATA "PhotoIdentity"
    }

    $publishPath = Join-Path $localApplicationRoot "app"
    $url = "http://127.0.0.1:5080"
    $settings = @{}
    $configurationDirectory = $null
    $mobileAccess = [pscustomobject]@{
        Enabled = $false
        ListenUri = $null
        PhoneUri = $null
        CertificatePath = $null
        CertificatePasswordEnvironmentVariable = $null
    }

    if ($null -ne $configurationFile) {
        $configurationDirectory = Split-Path -Parent $configurationFile
        $raw = Get-Content -LiteralPath $configurationFile -Raw
        try {
            $parsed = $raw | ConvertFrom-Json
        }
        catch {
            throw "Launcher configuration is not valid JSON: $configurationFile. $($_.Exception.Message)"
        }

        if ($null -ne $parsed.PSObject.Properties["publishPath"] -and
            -not [string]::IsNullOrWhiteSpace([string]$parsed.publishPath)) {
            $publishPath = Resolve-ConfiguredPath -Value ([string]$parsed.publishPath) -BaseDirectory $configurationDirectory
        }

        if ($null -ne $parsed.PSObject.Properties["url"] -and
            -not [string]::IsNullOrWhiteSpace([string]$parsed.url)) {
            $url = ([string]$parsed.url).Trim()
        }

        $mobileAccess = Read-MobileAccessConfiguration -ParsedConfiguration $parsed -ConfigurationDirectory $configurationDirectory

        if ($null -ne $parsed.PSObject.Properties["settings"] -and $null -ne $parsed.settings) {
            foreach ($property in $parsed.settings.PSObject.Properties) {
                if ($SupportedSettings -notcontains $property.Name) {
                    throw "Unsupported launcher setting '$($property.Name)'. Only documented PhotoIdentity settings may be supplied."
                }

                if ($null -eq $property.Value) {
                    continue
                }

                $value = [Environment]::ExpandEnvironmentVariables(([string]$property.Value).Trim())
                Assert-LauncherSettingValue -Name $property.Name -Value $value
                $settings[$property.Name] = $value
            }
        }
    }
    else {
        $publishPath = [IO.Path]::GetFullPath($publishPath)
    }

    if (-not [string]::IsNullOrWhiteSpace($PublishPathOverride)) {
        $publishPath = Resolve-ConfiguredPath -Value $PublishPathOverride -BaseDirectory $PSScriptRoot
    }

    try {
        $baseUri = [Uri]$url
    }
    catch {
        throw "Launcher URL is invalid: $url"
    }

    if (-not $baseUri.IsAbsoluteUri -or $baseUri.Scheme -ne "http" -or -not $baseUri.IsLoopback) {
        throw "Launcher URL must be an absolute loopback HTTP URL such as http://127.0.0.1:5080."
    }

    if (-not [string]::IsNullOrEmpty($baseUri.Query) -or -not [string]::IsNullOrEmpty($baseUri.Fragment)) {
        throw "Launcher URL may not contain a query string or fragment."
    }

    return [pscustomobject]@{
        ConfigurationPath = $configurationFile
        PublishPath = [IO.Path]::GetFullPath($publishPath)
        BaseUri = $baseUri
        Settings = $settings
        MobileAccess = $mobileAccess
        LocalApplicationRoot = [IO.Path]::GetFullPath($localApplicationRoot)
    }
}

function Test-PhotoIdentityHealth {
    param([Parameter(Mandatory = $true)][Uri]$BaseUri)

    $healthUri = New-Object Uri($BaseUri, "/health")
    try {
        $response = Invoke-WebRequest -UseBasicParsing -Uri $healthUri.AbsoluteUri -TimeoutSec 2
        if ($response.StatusCode -lt 200 -or $response.StatusCode -ge 300) {
            return $false
        }

        $payload = $response.Content | ConvertFrom-Json
        return $null -ne $payload -and [string]$payload.status -eq "ok"
    }
    catch {
        return $false
    }
}

function Test-TcpPortOpen {
    param([Parameter(Mandatory = $true)][Uri]$BaseUri)

    $client = New-Object System.Net.Sockets.TcpClient
    try {
        $task = $client.ConnectAsync($BaseUri.Host, $BaseUri.Port)
        if (-not $task.Wait(600)) {
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

function Open-PhotoIdentityBrowser {
    param([Parameter(Mandatory = $true)][Uri]$BaseUri)

    if ($NoBrowser) {
        return
    }

    Start-Process $BaseUri.AbsoluteUri | Out-Null
}

function Start-PhotoIdentityServer {
    param([Parameter(Mandatory = $true)]$Configuration)

    if (-not (Test-Path -LiteralPath $Configuration.PublishPath -PathType Container)) {
        throw "Published Photo Identity application was not found at '$($Configuration.PublishPath)'. Publish src\PhotoIdentity.Api\PhotoIdentity.Api.csproj there or create PhotoIdentity.launcher.json from PhotoIdentity.launcher.example.json with the correct publishPath."
    }

    $executable = Join-Path $Configuration.PublishPath "PhotoIdentity.Api.exe"
    $assembly = Join-Path $Configuration.PublishPath "PhotoIdentity.Api.dll"
    $filePath = $null
    $argumentList = $null

    $serverUrls = @($Configuration.BaseUri.AbsoluteUri)
    if ($Configuration.MobileAccess.Enabled) {
        $serverUrls += $Configuration.MobileAccess.ListenUri.AbsoluteUri
    }
    $serverUrlArgument = $serverUrls -join ";"

    if (Test-Path -LiteralPath $executable -PathType Leaf) {
        $filePath = $executable
        $argumentList = "--urls `"$serverUrlArgument`""
    }
    elseif (Test-Path -LiteralPath $assembly -PathType Leaf) {
        $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
        if ($null -eq $dotnet) {
            throw "PhotoIdentity.Api.dll exists, but the .NET runtime was not found on PATH. Install the required .NET runtime or use the packaged Windows application."
        }

        $filePath = $dotnet.Source
        $argumentList = "`"$assembly`" --urls `"$serverUrlArgument`""
    }
    else {
        throw "Publish output '$($Configuration.PublishPath)' does not contain PhotoIdentity.Api.exe or PhotoIdentity.Api.dll. Republish the API project before launching."
    }

    $logDirectory = Join-Path $Configuration.LocalApplicationRoot "launcher-logs"
    New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
    $stdoutLog = Join-Path $logDirectory "api.stdout.log"
    $stderrLog = Join-Path $logDirectory "api.stderr.log"

    $processEnvironment = @{}
    foreach ($name in $Configuration.Settings.Keys) {
        $processEnvironment[$name] = $Configuration.Settings[$name]
    }

    if ($Configuration.MobileAccess.Enabled) {
        $processEnvironment["ASPNETCORE_Kestrel__Certificates__Default__Path"] = $Configuration.MobileAccess.CertificatePath
        $passwordVariable = $Configuration.MobileAccess.CertificatePasswordEnvironmentVariable
        if ([string]::IsNullOrWhiteSpace($passwordVariable)) {
            $processEnvironment["ASPNETCORE_Kestrel__Certificates__Default__Password"] = $null
        }
        else {
            $processEnvironment["ASPNETCORE_Kestrel__Certificates__Default__Password"] =
                [Environment]::GetEnvironmentVariable($passwordVariable, "Process")
        }
    }

    $previousValues = @{}
    foreach ($name in $processEnvironment.Keys) {
        $previousValues[$name] = [Environment]::GetEnvironmentVariable($name, "Process")
        [Environment]::SetEnvironmentVariable($name, $processEnvironment[$name], "Process")
    }

    try {
        return Start-Process `
            -FilePath $filePath `
            -ArgumentList $argumentList `
            -WorkingDirectory $Configuration.PublishPath `
            -WindowStyle Hidden `
            -RedirectStandardOutput $stdoutLog `
            -RedirectStandardError $stderrLog `
            -PassThru
    }
    finally {
        foreach ($name in $processEnvironment.Keys) {
            [Environment]::SetEnvironmentVariable($name, $previousValues[$name], "Process")
        }
    }
}

try {
    $configuration = Read-LauncherConfiguration

    if (Test-PhotoIdentityHealth -BaseUri $configuration.BaseUri) {
        Write-Host "Photo Identity is already running at $($configuration.BaseUri.AbsoluteUri)"
        if ($configuration.MobileAccess.Enabled) {
            Write-Host "The current launcher configuration requests trusted-LAN mobile access at $($configuration.MobileAccess.PhoneUri.AbsoluteUri). Restart the existing Photo Identity process if mobile settings changed."
        }
        Open-PhotoIdentityBrowser -BaseUri $configuration.BaseUri
        exit 0
    }

    if (Test-TcpPortOpen -BaseUri $configuration.BaseUri) {
        throw "Port $($configuration.BaseUri.Port) is already in use, but Photo Identity did not answer its /health endpoint. Stop the conflicting process or change the loopback URL in the launcher configuration."
    }

    if ($configuration.MobileAccess.Enabled -and (Test-TcpPortOpen -BaseUri $configuration.MobileAccess.ListenUri)) {
        throw "The configured mobile HTTPS port $($configuration.MobileAccess.ListenUri.Port) is already in use on the selected trusted-LAN address. Stop the conflicting process or change mobileAccess.listenUrl."
    }

    Write-Host "Starting Photo Identity from $($configuration.PublishPath)"
    if ($null -ne $configuration.ConfigurationPath) {
        Write-Host "Using launcher configuration $($configuration.ConfigurationPath)"
    }
    else {
        Write-Host "No launcher configuration file was found; using local defaults."
    }

    $process = Start-PhotoIdentityServer -Configuration $configuration
    $deadline = [DateTime]::UtcNow.AddSeconds($StartupTimeoutSeconds)

    while ([DateTime]::UtcNow -lt $deadline) {
        if ($process.HasExited) {
            $logDirectory = Join-Path $configuration.LocalApplicationRoot "launcher-logs"
            throw "Photo Identity exited during startup with code $($process.ExitCode). Review '$logDirectory\api.stderr.log' for details."
        }

        if (Test-PhotoIdentityHealth -BaseUri $configuration.BaseUri) {
            Write-Host "Photo Identity is ready at $($configuration.BaseUri.AbsoluteUri)"
            if ($configuration.MobileAccess.Enabled) {
                Write-Host "Trusted-LAN mobile HTTPS is enabled at $($configuration.MobileAccess.PhoneUri.AbsoluteUri)"
            }
            Open-PhotoIdentityBrowser -BaseUri $configuration.BaseUri
            exit 0
        }

        Start-Sleep -Milliseconds 350
    }

    if (-not $process.HasExited) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    }

    $logDirectory = Join-Path $configuration.LocalApplicationRoot "launcher-logs"
    throw "Photo Identity did not become healthy within $StartupTimeoutSeconds seconds. The attempted process was stopped. Review '$logDirectory\api.stderr.log' for details."
}
catch {
    Write-Host "Photo Identity launcher error:" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}
