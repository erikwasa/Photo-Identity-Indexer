# READ ONLY. Summarize the authoritative private retirement candidate plan
# without printing any personal source keys, image paths or IDs.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PrivatePlanCsv,
    [string]$NormalizedPrefix = 'fideli/'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $PrivatePlanCsv -PathType Leaf)) {
    throw 'The private revalidated retirement candidate plan does not exist.'
}
$prefix = $NormalizedPrefix.Replace('\', '/').Trim().ToLowerInvariant()
if ([string]::IsNullOrWhiteSpace($prefix) -or -not $prefix.EndsWith('/')) {
    throw 'Specify a normalized directory prefix ending in slash.'
}
$rows = @(Import-Csv -LiteralPath $PrivatePlanCsv)
if ($rows.Count -eq 0) { throw 'Private plan contains no collision groups.' }
$columns = @($rows[0].PSObject.Properties.Name)
foreach ($name in @('NormalizedSourceKey', 'SurvivorSourceKey',
    'SecondarySourceKey', 'ProposedAction', 'Blockers')) {
    if ($name -cnotin $columns) { throw "Private plan missing $name." }
}
$inScope = @($rows | Where-Object {
    [string]$_.NormalizedSourceKey -and
    $_.NormalizedSourceKey.ToLowerInvariant().StartsWith(
        $prefix, [StringComparison]::Ordinal)
})
$eligible = @($inScope | Where-Object {
    $_.ProposedAction -ceq 'review-for-reversible-alias-retirement'
})
$held = @($inScope | Where-Object { $_.ProposedAction -ceq 'hold' })
if ($eligible.Count + $held.Count -ne $inScope.Count) {
    throw 'Private plan has unknown action classification.'
}
# Prioritize only the exact intended direction; do not infer from GUID or hash.
$lowercaseSurvivors = @($eligible | Where-Object {
    $_.SurvivorSourceKey.StartsWith($prefix, [StringComparison]::Ordinal) -and
    $_.SecondarySourceKey.StartsWith(
        $NormalizedPrefix.Substring(0, 1).ToUpperInvariant() +
            $NormalizedPrefix.Substring(1).Replace('\', '/'),
        [StringComparison]::Ordinal)
})
$blockerCounts = @(
    foreach ($candidate in $held) {
        foreach ($blocker in @($candidate.Blockers -split ';')) {
            if (-not [string]::IsNullOrWhiteSpace($blocker)) {
                $blocker
            }
        }
    }
) | Group-Object | Sort-Object Count -Descending, Name |
    ForEach-Object { [pscustomobject]@{ Reason = $_.Name; Groups = $_.Count } }

[pscustomobject]@{
    TotalCollisionGroups = $rows.Count
    ScopedCollisionGroups = $inScope.Count
    EligibleUnretired = $eligible.Count
    EligibleWithLowercaseSurvivorAndUppercaseSecondary = $lowercaseSurvivors.Count
    EligibleOtherCaseDirection = $eligible.Count - $lowercaseSurvivors.Count
    HeldOrAlreadyRetired = $held.Count
    HeldReasons = @($blockerCounts)
    AutomaticallyApplied = 0
    FilesDeleted = 0
    RowsDeleted = 0
}
