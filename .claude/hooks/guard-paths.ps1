# PreToolUse hook for Edit|Write. Denies or asks based on the target path.
$ErrorActionPreference = 'Stop'
trap { [Console]::Error.WriteLine("guard-paths failed (malformed payload?): $_"); exit 2 }
$payload = [Console]::In.ReadToEnd() | ConvertFrom-Json
$file = [string]$payload.tool_input.file_path
if (-not $file) { throw 'no tool_input.file_path.' }

function Write-Decision([string] $decision, [string] $reason) {
  @{ hookSpecificOutput = @{
      hookEventName = 'PreToolUse'
      permissionDecision = $decision
      permissionDecisionReason = $reason
    } } | ConvertTo-Json -Compress
  exit 0
}

function ConvertTo-Norm([string] $path) {
  ([System.IO.Path]::GetFullPath($path) -replace '\\', '/').TrimEnd('/')
}

$root = if ($env:CLAUDE_PROJECT_DIR) { $env:CLAUDE_PROJECT_DIR } else { $payload.cwd }
$root = ConvertTo-Norm $root
$full = ConvertTo-Norm $(if ([System.IO.Path]::IsPathRooted($file)) { $file } else { Join-Path $root $file })

$inside = $full.StartsWith("$root/", [StringComparison]::OrdinalIgnoreCase)
if (-not $inside) {
  # Claude's own plan/memory files and its temp scratchpad are allowed.
  # TEMP may be an 8.3 short path, so also allow the long LOCALAPPDATA form.
  $allowed = @((Join-Path $HOME '.claude'), (Join-Path ([System.IO.Path]::GetTempPath()) 'claude'),
    (Join-Path "$env:LOCALAPPDATA" 'Temp/claude')) | Where-Object { $_ } |
    ForEach-Object { (ConvertTo-Norm $_) + '/' }
  foreach ($a in $allowed) {
    if ($full.StartsWith($a, [StringComparison]::OrdinalIgnoreCase)) { exit 0 }
  }
  Write-Decision 'deny' "Edits outside the repository root are not allowed: $full"
}

$rel = $full.Substring($root.Length + 1)
if ($rel -match '(?i)^models/') {
  Write-Decision 'deny' 'models/ holds model manifests and binaries; the agent must not edit it.'
}
if ($rel -match '(?i)^\.github/workflows/') {
  Write-Decision 'ask' 'CI workflow edit: allowed only when the issue is explicitly about CI.'
}
if ($rel -match '(?i)(^|/)(CLAUDE\.md$|\.claude/(hooks/|settings(\.local)?\.json$))') {
  Write-Decision 'ask' 'Agent guardrail edit: allowed only when the issue is explicitly about hooks, permissions or CLAUDE.md.'
}
if ($rel -match '(?i)^deploy/postgres/' -or $rel -match '(?i)PostgresCatalogueDatabase[^/]*Schema[^/]*\.cs$') {
  Write-Decision 'ask' 'Schema/migration surface: confirm /db-backup has run and verified before this change.'
}
exit 0
