# Stop hook. Blocks finishing (exit 2) while the solution does not build. Never runs tests.
$ErrorActionPreference = 'Stop'
trap { [Console]::Error.WriteLine("build-on-stop failed (malformed payload?): $_"); exit 2 }
$payload = [Console]::In.ReadToEnd() | ConvertFrom-Json
if ($payload -isnot [System.Management.Automation.PSCustomObject]) { throw 'not a JSON object.' }
# Already continuing because of a Stop hook: let Claude stop and report instead of looping on a build it can't fix.
if ($payload.stop_hook_active) { exit 0 }
$root = if ($env:CLAUDE_PROJECT_DIR) { $env:CLAUDE_PROJECT_DIR } else { (Get-Location).Path }
Push-Location $root
try {
  # Skip when no build inputs changed (doc-only turns stay fast).
  $changed = git status --porcelain --untracked-files=all 2>$null | Where-Object { $_ -match '\.(cs|csproj|props|targets|slnx)"?$' }
  if (-not $changed) { exit 0 }

  $out = dotnet build PhotoIdentity.slnx --no-restore -v q -nologo 2>&1
  if ($LASTEXITCODE -ne 0) {
    $errors = $out | Where-Object { $_ -match ': error ' } | Select-Object -Unique -First 30
    if (-not $errors) { $errors = $out | Select-Object -Last 30 }
    [Console]::Error.WriteLine("Build failed. Fix these before finishing:`n$($errors -join "`n")")
    exit 2
  }
}
finally { Pop-Location }
exit 0
