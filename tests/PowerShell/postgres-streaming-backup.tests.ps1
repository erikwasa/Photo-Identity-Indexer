# Synthetic Windows/PowerShell 7 contract checks for the streamed backup helper.
# Never invokes Podman, PostgreSQL, OneDrive, or a real database.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$scriptPath = Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'backup-postgres-catalogue-improved.ps1'
$temp = Join-Path ([IO.Path]::GetTempPath()) ('photoidentity-stream-backup-test-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($temp) | Out-Null
$script:checks = 0

function Check {
    param([bool]$Condition, [string]$Explanation)
    $script:checks++
    if (-not $Condition) { throw "Streaming backup assertion failed: $Explanation" }
}
function Refuse {
    param([scriptblock]$Action, [string]$Explanation)
    $script:checks++
    $rejected = $false
    try { & $Action | Out-Null } catch { $rejected = $true }
    if (-not $rejected) { throw "Streaming backup unexpectedly allowed: $Explanation" }
}

try {
    $tokens = $null
    $errors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile(
        $scriptPath, [ref]$tokens, [ref]$errors)
    Check ($errors.Count -eq 0) 'streaming script parses in PowerShell'
    $functions = @($ast.FindAll({
        param($node)
        $node -is [System.Management.Automation.Language.FunctionDefinitionAst]
    }, $true))
    foreach ($name in @('Quote-WindowsArgument', 'Invoke-PodmanBinary', 'Assert-AvailableSpace', 'Resolve-PostgresContainer', 'Assert-DistinctBackupDestination', 'Publish-BackupFile')) {
        $match = @($functions | Where-Object Name -eq $name)
        Check ($match.Count -eq 1) "exactly one helper named $name"
        . ([scriptblock]::Create($match[0].Extent.Text))
    }

    Check ((Quote-WindowsArgument '') -ceq '""') 'empty native argument'
    Check ((Quote-WindowsArgument 'simple') -ceq 'simple') 'plain native argument'
    Check ((Quote-WindowsArgument 'C:\some dir\file') -ceq '"C:\some dir\file"') 'spaces in native argument'
    Check ((Quote-WindowsArgument 'C:\some dir\') -ceq '"C:\some dir\\"') 'trailing slash in quoted native argument'

    $local = Join-Path $temp 'local.dump'
    $remote = Join-Path $temp 'remote.dump'
    Refuse { Assert-DistinctBackupDestination -BackupPath $local -CopyPath $local } 'same output and cloud destination'
    Assert-DistinctBackupDestination -BackupPath $local -CopyPath $remote
    Check $true 'distinct cloud destination accepted'

    [IO.File]::WriteAllText($local, 'original backup')
    [IO.File]::WriteAllText($remote, 'existing remote backup')
    Refuse { Publish-BackupFile -Source $local -Destination $remote } 'never overwrite existing backup'
    Check (([IO.File]::ReadAllText($local) -ceq 'original backup') -and
        ([IO.File]::ReadAllText($remote) -ceq 'existing remote backup')) 'collision preserves both files'

    $ContainerName = 'disposable-override'
    Check ((Resolve-PostgresContainer) -ceq 'disposable-override') 'explicit container override avoids Podman discovery'

    Assert-AvailableSpace -Directory $temp -MinimumBytes 1 -Purpose 'synthetic check'
    Check $true 'reachable Windows volume has available space'
    Refuse { Assert-AvailableSpace -Directory $temp -MinimumBytes ([long]::MaxValue) -Purpose 'synthetic full drive' } 'insufficient free space'

    # Exercise the exact binary stream helper with cmd.exe, not a fake pg_dump.
    # It must preserve bytes and propagate a failed native process exit code.
    if ($env:OS -ne 'Windows_NT' -or -not $env:ComSpec) {
        throw 'This synthetic native-process check must run on Windows.'
    }
    $podmanExe = $env:ComSpec
    $stdoutPath = Join-Path $temp 'stdout.bin'
    Invoke-PodmanBinary -Arguments @('/d', '/c', 'echo stream-payload') -StdoutPath $stdoutPath
    $bytes = [IO.File]::ReadAllBytes($stdoutPath)
    $expected = [System.Text.Encoding]::ASCII.GetBytes("stream-payload`r`n")
    Check (($bytes -join ',') -ceq ($expected -join ',')) 'native stdout is copied byte-for-byte'

    $stdinPath = Join-Path $temp 'stdin.txt'
    [IO.File]::WriteAllText($stdinPath, 'synthetic-only-input')
    Invoke-PodmanBinary -Arguments @('/d', '/c', 'more') -StdinPath $stdinPath
    Check (Test-Path -LiteralPath $stdinPath) 'native stdin source survives decode/consume'

    $failedPath = Join-Path $temp 'failed.bin'
    Refuse { Invoke-PodmanBinary -Arguments @('/d', '/c', 'exit /b 7') -StdoutPath $failedPath } 'nonzero native exit cannot be successful'
    Write-Host "PASS: $script:checks source-free streaming backup assertions"
}
finally {
    Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
}
