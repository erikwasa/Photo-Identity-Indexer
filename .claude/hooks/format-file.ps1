# PostToolUse hook for Edit|Write. Whitespace-formats an edited .cs file.
$ErrorActionPreference = 'Stop'
trap { [Console]::Error.WriteLine("format-file failed (malformed payload?): $_"); exit 2 }
$payload = [Console]::In.ReadToEnd() | ConvertFrom-Json
if ($payload -isnot [System.Management.Automation.PSCustomObject]) { throw 'not a JSON object.' }
$file = [string]$payload.tool_input.file_path
if (-not $file -or -not $file.EndsWith('.cs', [StringComparison]::OrdinalIgnoreCase)) { exit 0 }

$root = if ($env:CLAUDE_PROJECT_DIR) { $env:CLAUDE_PROJECT_DIR } else { $payload.cwd }
$full = [System.IO.Path]::GetFullPath($file)
$rel = [System.IO.Path]::GetRelativePath($root, $full)
if ($rel.StartsWith('..') -or -not (Test-Path $full)) { exit 0 }

Push-Location $root
try {
  $out = dotnet format whitespace --folder --include $rel 2>&1
  if ($LASTEXITCODE -ne 0) {
    [Console]::Error.WriteLine("dotnet format whitespace failed for ${rel}:`n$($out | Out-String)")
    exit 2
  }
}
finally { Pop-Location }
exit 0
