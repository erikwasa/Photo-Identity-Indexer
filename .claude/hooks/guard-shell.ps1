# PreToolUse hook for Bash|PowerShell. Exit 2 (reason on stderr) blocks the command.
$payload = [Console]::In.ReadToEnd() | ConvertFrom-Json
$cmd = [string]$payload.tool_input.command

$oneDrive = @($env:OneDrive, $env:OneDriveConsumer, $env:OneDriveCommercial) |
  Where-Object { $_ } | ForEach-Object {
    # Match both C:\Users\x\OneDrive and C:/Users/x/OneDrive and /c/Users/x/OneDrive spellings.
    $tail = ($_ -replace '^[A-Za-z]:', '') -split '[\\/]' | Where-Object { $_ } | ForEach-Object { [regex]::Escape($_) }
    '(?i)' + ($tail -join '[\\/]+')
  }

$rules = @(
  @{ p = 'git\s+push\b.*\b(main|master)\b'; r = 'Never push to main. Push a feature branch and open a PR.' },
  @{ p = 'git\s+push\b.*\s(--force|-f)\b'; r = 'Force push is not allowed.' },
  @{ p = 'gh\s+pr\s+merge'; r = 'Only the maintainer merges PRs.' },
  @{ p = '(?i)(\bOneDrive[\\/]|\$env:OneDrive|%OneDrive|\$\{?OneDrive)'; r = 'Originals/OneDrive are off-limits to the agent.' },
  @{ p = '(?i)\b(drop\s+(database|table|schema)|truncate\s+(table\s+)?\w+|dropdb|pg_restore|podman\s+(rm|volume\s+rm))\b';
     r = 'Destructive database/container command. Ask the maintainer to run it.' }
)
foreach ($p in $oneDrive) { $rules += @{ p = $p; r = 'Originals/OneDrive are off-limits to the agent.' } }

foreach ($x in $rules) {
  if ($cmd -match $x.p) { [Console]::Error.WriteLine("BLOCKED: $($x.r)"); exit 2 }
}

if ($cmd -match '^\s*git\s+push\s*$') {
  $branch = (git rev-parse --abbrev-ref HEAD 2>$null | Out-String).Trim()
  if ($branch -in @('main', 'master')) { [Console]::Error.WriteLine('BLOCKED: you are on main.'); exit 2 }
}
exit 0
