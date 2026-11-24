# Synthetic tests for .claude/hooks and .claude/settings.json. Uses temporary folders only; never pushes.
[CmdletBinding()]
param([string] $HooksRoot)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not $HooksRoot) { $HooksRoot = Join-Path $repo '.claude/hooks' }
$root = Join-Path ([IO.Path]::GetTempPath()) ('claude-hooks-' + [Guid]::NewGuid().ToString('N'))
$onMain = Join-Path $root 'on main'
$onFeature = Join-Path $root 'on-feature'
$outside = Join-Path $root 'no-repo'
$oldProjectDir = $env:CLAUDE_PROJECT_DIR
$failures = [Collections.Generic.List[string]]::new()
$script:count = 0

function Invoke-Hook {
    param([string] $Name, [string] $Payload)
    $output = $Payload | & pwsh -NoProfile -File (Join-Path $HooksRoot $Name) 2>&1 | Out-String
    [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $output }
}

function Assert-Hook {
    param([string] $Case, [string] $Name, [string] $Payload, [int] $ExitCode, [string] $Match = '', [switch] $Silent)
    $script:count++
    $result = Invoke-Hook $Name $Payload
    if ($result.ExitCode -ne $ExitCode -or $result.Output -notmatch $Match -or ($Silent -and $result.Output.Trim())) {
        $failures.Add("${Name}: $Case => exit $($result.ExitCode) (expected $ExitCode). $($result.Output.Trim())")
    }
}

function Assert-Shell {
    param([string] $Command, [string] $Cwd, [int] $ExitCode)
    $payload = @{ cwd = $Cwd; tool_input = @{ command = $Command } } | ConvertTo-Json -Compress
    $match = if ($ExitCode -eq 2) { 'BLOCKED: ' } else { '' }
    Assert-Hook "[$(Split-Path -Leaf $Cwd)] $Command" 'guard-shell.ps1' $payload $ExitCode $match
}

try {
    foreach ($dir in $onMain, $onFeature, $outside) { [IO.Directory]::CreateDirectory($dir) | Out-Null }
    git init -q -b main $onMain
    git init -q -b wi/1-x $onFeature
    if ($LASTEXITCODE -ne 0) { throw 'git init failed.' }

    foreach ($command in @(
            'git push origin main',
            'git -C ../x push origin main',
            'git -C "C:/some dir" push origin HEAD:main',
            'git -c a.b=c push origin main',
            'git --git-dir "C:/some dir/.git" push origin main',
            'git --no-pager -C x push origin master',
            'git.exe push origin main',
            '& "C:\Program Files\Git\cmd\git.exe" -C x push origin main',
            'git push --force origin wi/1-x',
            'git -C x push -f origin wi/1-x',
            'git push --force-with-lease origin wi/1-x',
            'git push origin +wi/1-x',
            'git push origin "+HEAD:refs/heads/wi/1-x"',
            "git -C x push -u origin '+wi/1-x'",
            "git -C `"$onMain`" push",
            "git -C `"$onMain`" push origin HEAD",
            "git -C '../on main' push -u origin HEAD",
            "git -C `"$root`" -C 'on main' push origin",
            'git -C missing-dir push origin HEAD',
            'gh pr merge 1')) {
        Assert-Shell $command $onFeature 2
    }
    foreach ($command in @(
            'git push',
            'git push origin',
            'git push -u origin HEAD',
            'git push origin "HEAD"',
            'git push origin @',
            'git push origin :',
            'git push origin HEAD 2>&1 | Select-Object -Last 3',
            'git push origin 2>&1',
            'git status; git push origin HEAD; git status',
            'bash -c "git push origin HEAD"',
            'git push origin $(git branch --show-current)')) {
        Assert-Shell $command $onMain 2
    }
    foreach ($command in @(
            'git push',
            'git push -u origin HEAD',
            'git push -u origin wi/1-x',
            "git -C `"$onFeature`" push origin wi/1-x",
            'git push origin wi/1-x 2>&1; "1 +2"',
            'git stash push -m "main"',
            ('git ' + '-c ' * 300 + 'x'), # must not hang on regex backtracking
            'git log --oneline main')) {
        Assert-Shell $command $onFeature 0
    }
    foreach ($command in @(
            'git switch -c wi/1-x && git commit -m x && git push -u origin wi/1-x',
            "git -C `"$onFeature`" push origin HEAD")) {
        Assert-Shell $command $onMain 0
    }
    Assert-Shell 'git push origin HEAD' $outside 2

    $env:CLAUDE_PROJECT_DIR = $onFeature
    $paths = @{
        'CLAUDE.md'                          = 'ask'
        'sub/Claude.md'                      = 'ask'
        '.claude/settings.json'              = 'ask'
        '.claude/settings.local.json'        = 'ask'
        '.claude/hooks/guard-shell.ps1'      = 'ask'
        'src/../.claude/hooks/new/x.ps1'     = 'ask'
        '.claude/worktrees/x/CLAUDE.md'      = 'ask'
        '.github/workflows/build.yml'        = 'ask'
        'models/x.json'                      = 'deny'
        '../other/x.txt'                     = 'deny'
        'src/x.cs'                           = ''
        '.claude/skills/x/SKILL.md'          = ''
        'docs/NOT-CLAUDE.md'                 = ''
    }
    foreach ($path in $paths.Keys) {
        $payload = @{ cwd = $onFeature; tool_input = @{ file_path = $path } } | ConvertTo-Json -Compress
        if ($paths[$path]) {
            Assert-Hook $path 'guard-paths.ps1' $payload 0 "`"permissionDecision`":`"$($paths[$path])`""
        }
        else { Assert-Hook $path 'guard-paths.ps1' $payload 0 -Silent }
    }

    foreach ($hook in 'guard-shell.ps1', 'guard-paths.ps1', 'build-on-stop.ps1', 'format-file.ps1') {
        foreach ($payload in '', '{ not json', 'null', '"text"') {
            Assert-Hook "malformed '$payload'" $hook $payload 2 'malformed'
        }
    }
    foreach ($hook in 'guard-shell.ps1', 'guard-paths.ps1') {
        foreach ($payload in '{}', '{"tool_input":{}}') { Assert-Hook "missing field '$payload'" $hook $payload 2 'malformed' }
    }

    # A clean repo skips the build; a changed .cs file with no solution makes the build fail at once.
    Assert-Hook 'clean repo' 'build-on-stop.ps1' '{"stop_hook_active":false}' 0 -Silent
    [IO.File]::WriteAllText((Join-Path $onFeature 'x.cs'), '')
    Assert-Hook 'failing build' 'build-on-stop.ps1' '{"stop_hook_active":false}' 2 'Build failed'
    Assert-Hook 'failing build, already continued' 'build-on-stop.ps1' '{"stop_hook_active":true}' 0 -Silent

    $settings = Get-Content -Raw (Join-Path (Split-Path -Parent $HooksRoot) 'settings.json') | ConvertFrom-Json
    foreach ($extension in 'jpg', 'heic', 'heif', 'webp', 'tif', 'tiff', 'mov', 'mkv', 'avi', 'm4v') {
        $script:count++
        if ("Read(**/*.$extension)" -notin $settings.permissions.deny) { $failures.Add("settings.json: no Read deny for .$extension") }
    }

    if ($failures.Count -gt 0) {
        $failures | ForEach-Object { Write-Host "FAILED $_" }
        throw "$($failures.Count) of $script:count cases failed."
    }
    Write-Host "claude-hooks tests passed ($script:count cases)."
}
finally {
    $env:CLAUDE_PROJECT_DIR = $oldProjectDir
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}
