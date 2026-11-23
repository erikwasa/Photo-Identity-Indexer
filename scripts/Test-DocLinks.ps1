<#
.SYNOPSIS
Checks that relative Markdown links resolve to an existing file or folder.

.DESCRIPTION
Ported from the relative-link check in the retired tools/PhotoIdentity.Docs DocumentationValidator.
Empty targets, in-page anchors and absolute URIs are skipped. Fragments and query strings are removed
and the target is URL-decoded before it is resolved relative to the linking file.
With no -Path it checks README.md and docs/index.md. Exits 1 when any link is broken, otherwise 0.
#>
[CmdletBinding()]
param(
    [string[]] $Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $Path -or $Path.Count -eq 0) {
    $Path = @((Join-Path $repoRoot 'README.md'), (Join-Path $repoRoot 'docs/index.md'))
}

$linkPattern = [regex]'(?<!!)\[[^\]]*\]\((?<target>[^)]+)\)'
$checked = 0
$broken = New-Object System.Collections.Generic.List[string]

foreach ($file in $Path) {
    $full = [IO.Path]::GetFullPath($file)
    if (-not (Test-Path -LiteralPath $full -PathType Leaf)) {
        $broken.Add("$file does not exist.")
        continue
    }
    $directory = Split-Path -Parent $full
    $content = [IO.File]::ReadAllText($full)
    foreach ($match in $linkPattern.Matches($content)) {
        $target = $match.Groups['target'].Value.Trim().Trim('<', '>')
        if ($target.Length -eq 0 -or $target.StartsWith('#')) { continue }
        $uri = $null
        if ([Uri]::TryCreate($target, [UriKind]::Absolute, [ref] $uri)) { continue }

        $withoutFragment = $target.Split('#')[0].Split('?')[0]
        if ($withoutFragment.Length -eq 0) { continue }

        $checked++
        $resolved = [IO.Path]::GetFullPath((Join-Path $directory ([Uri]::UnescapeDataString($withoutFragment))))
        if (-not (Test-Path -LiteralPath $resolved)) {
            $broken.Add("$file links to missing path '$target'.")
        }
    }
}

foreach ($line in $broken) { Write-Host "BROKEN_LINK $line" }
Write-Host "Checked $checked relative links in $($Path.Count) file(s); broken: $($broken.Count)."
if ($broken.Count -gt 0) { exit 1 }
exit 0
