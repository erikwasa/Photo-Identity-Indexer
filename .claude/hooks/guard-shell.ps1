# PreToolUse hook for Bash|PowerShell. Exit 2 (reason on stderr) blocks the command.
# Rules match the whole command text. If a PR body or commit message only quotes a blocked command,
# pass it as a file instead (gh --body-file, git commit -F).
$ErrorActionPreference = 'Stop'
trap { [Console]::Error.WriteLine("BLOCKED: guard-shell failed (malformed payload?): $_"); exit 2 }

function Stop-Command([string] $reason) {
  [Console]::Error.WriteLine("BLOCKED: $reason (Only quoting it? Use --body-file or git commit -F.)")
  exit 2
}

$payload = [Console]::In.ReadToEnd() | ConvertFrom-Json
$cmd = [string]$payload.tool_input.command
if (-not $cmd) { throw 'no tool_input.command.' }

# 'git push', also with global options in between: git -C <dir> push, git -c a=b push.
# Atomic (?>) groups: without them a long run of options backtracks exponentially and the hook times out.
$arg = '(?>(?:"[^"]*"|''[^'']*''|[^\s"''])+)'
$push = "\bgit(?:\.exe)?[`"']?(?<opts>(?:\s+(?>(?:-c|--git-dir|--work-tree|--namespace)\s+$arg|-$arg))*)\s+push\b"

$oneDrive = @($env:OneDrive, $env:OneDriveConsumer, $env:OneDriveCommercial) |
  Where-Object { $_ } | ForEach-Object {
    # Match both C:\Users\x\OneDrive and C:/Users/x/OneDrive and /c/Users/x/OneDrive spellings.
    $tail = ($_ -replace '^[A-Za-z]:', '') -split '[\\/]' | Where-Object { $_ } | ForEach-Object { [regex]::Escape($_) }
    '(?i)' + ($tail -join '[\\/]+')
  }

$rules = @(
  @{ p = "$push.*\b(main|master)\b"; r = 'Never push to main. Push a feature branch and open a PR.' },
  @{ p = "$push.*\s(--force|-f)\b"; r = 'Force push is not allowed.' },
  @{ p = "$push[^;|&\r\n]*\s[`"']?\+\S"; r = 'Force push (+refspec) is not allowed.' },
  @{ p = 'gh\s+pr\s+merge'; r = 'Only the maintainer merges PRs.' },
  @{ p = '(?i)(\bOneDrive[\\/]|\$env:OneDrive|%OneDrive|\$\{?OneDrive)'; r = 'Originals/OneDrive are off-limits to the agent.' },
  @{ p = '(?i)\b(drop\s+(database|table|schema)|truncate\s+(table\s+)?\w+|dropdb|pg_restore|podman\s+(rm|volume\s+rm))\b';
     r = 'Destructive database/container command. Ask the maintainer to run it.' }
)
foreach ($p in $oneDrive) { $rules += @{ p = $p; r = 'Originals/OneDrive are off-limits to the agent.' } }

foreach ($x in $rules) {
  if ($cmd -match $x.p) { Stop-Command $x.r }
}

# A push that doesn't name a branch pushes the current one, so it must not run on main.
foreach ($m in [regex]::Matches($cmd, "(?i)$push(?<args>[^;|&(){}\r\n]*)")) {
  $positional = @([regex]::Matches(($m.Groups['args'].Value -replace '\d*>.*$', ''), $arg) |
    ForEach-Object { $_.Value -replace '["'']', '' } | Where-Object { $_ -notlike '-*' })
  $refs = @($positional | Select-Object -Skip 1)
  if ($refs.Count -gt 0 -and -not ($refs -match '^(HEAD|@|:)$|[$`]')) { continue }

  $dirs = @('-C', $(if ($payload.cwd) { [string]$payload.cwd } else { '.' }))
  foreach ($c in [regex]::Matches($m.Groups['opts'].Value, "\s-C\s+(?<dir>$arg)")) {
    $dirs += '-C', ($c.Groups['dir'].Value -replace '["'']', '')
  }
  $branch = (git @dirs symbolic-ref --short -q HEAD 2>$null | Out-String).Trim()
  if ($LASTEXITCODE -notin 0, 1) { Stop-Command 'Cannot tell which branch this pushes. Name the branch explicitly.' }
  if ($branch -in @('main', 'master')) { Stop-Command 'You are on main. Push a feature branch by name.' }
}
exit 0
