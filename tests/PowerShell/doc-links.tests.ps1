# Synthetic test for scripts/Test-DocLinks.ps1. Uses a temporary folder only.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$tool = Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'scripts/Test-DocLinks.ps1'
$root = Join-Path ([IO.Path]::GetTempPath()) ('doc-links-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory((Join-Path $root 'sub')) | Out-Null

function Invoke-Tool {
    param([string[]] $Files)
    $output = & pwsh -NoProfile -File $tool -Path $Files 2>&1 | Out-String
    [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $output }
}

try {
    [IO.File]::WriteAllText((Join-Path $root 'sub/target file.md'), '# target')
    $page = Join-Path $root 'page.md'
    $good = '[ok](sub/target%20file.md#heading) [dir](sub/) [anchor](#top) [web](https://example.invalid/x) ![img](missing.png)'

    [IO.File]::WriteAllText($page, "$good [bad](sub/missing.md)")
    $result = Invoke-Tool @($page)
    if ($result.ExitCode -ne 1) { throw "Expected exit 1 for a broken link, got $($result.ExitCode). $($result.Output)" }
    if ($result.Output -notmatch "BROKEN_LINK .*'sub/missing\.md'") { throw "Broken link not reported. $($result.Output)" }
    if ($result.Output -notmatch 'broken: 1\.') { throw "Expected exactly one broken link. $($result.Output)" }

    $result = Invoke-Tool @((Join-Path $root 'absent.md'))
    if ($result.ExitCode -ne 1) { throw "Expected exit 1 for a missing input file, got $($result.ExitCode)." }

    [IO.File]::WriteAllText($page, $good)
    $result = Invoke-Tool @($page)
    if ($result.ExitCode -ne 0) { throw "Expected exit 0 once links resolve, got $($result.ExitCode). $($result.Output)" }

    Write-Host 'doc-links tests passed (3 cases).'
}
finally {
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}
