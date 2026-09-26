[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$Report,

    [ValidateRange(0, 20)]
    [int]$SamplesPerRule = 2,

    [string]$Rules,

    [string[]]$ApproveRule,

    [switch]$ApproveAll,

    [string]$ApprovedRulesOutput
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Resolve-ExistingFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [string]$Label
    )

    $resolved = Resolve-Path -LiteralPath $Path -ErrorAction Stop
    if (-not (Test-Path -LiteralPath $resolved.Path -PathType Leaf)) {
        throw "$Label '$Path' is not a file."
    }

    return $resolved.Path
}

function Resolve-OutputPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
}

function Get-SourceRoot {
    param([string]$SourceKey)

    if ([string]::IsNullOrWhiteSpace($SourceKey)) {
        return '<unknown>'
    }

    $normalized = $SourceKey.Replace('\\', '/')
    $slash = $normalized.IndexOf('/')
    if ($slash -lt 0) {
        return '<root>'
    }

    return $normalized.Substring(0, $slash)
}

$reportPath = Resolve-ExistingFile -Path $Report -Label 'Report'
$reportData = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json

if ($null -eq $reportData.summary -or $null -eq $reportData.items) {
    throw "Report '$reportPath' is not a metadata enrichment report."
}

$placeItems = @(
    $reportData.items |
        Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_.proposedPlace) }
)

Write-Host ''
Write-Host 'Metadata enrichment review'
Write-Host '--------------------------'
Write-Host ("Mode:              {0}" -f $reportData.mode)
Write-Host ("Photos scanned:    {0}" -f $reportData.summary.TotalPhotos)
Write-Host ("Missing location:  {0}" -f $reportData.summary.MissingLocation)
Write-Host ("Place proposals:   {0}" -f $placeItems.Count)
Write-Host ("Ambiguous:         {0}" -f $reportData.summary.Ambiguous)

if ($placeItems.Count -eq 0) {
    Write-Host ''
    Write-Host 'No place proposals to review.'
    return
}

$grouped = @(
    $placeItems |
        Group-Object placeRule |
        ForEach-Object {
            $items = @($_.Group)
            $places = @($items.proposedPlace | Sort-Object -Unique)
            $dates = @($items.existingDate | Where-Object { $_ } | Sort-Object -Unique)
            $roots = @(
                $items |
                    ForEach-Object { Get-SourceRoot -SourceKey ([string]$_.sourceKey) } |
                    Group-Object |
                    Sort-Object -Property @(
                        @{ Expression = 'Count'; Descending = $true },
                        @{ Expression = 'Name'; Descending = $false }
                    ) |
                    ForEach-Object { '{0}:{1}' -f $_.Name, $_.Count }
            )

            [pscustomobject]@{
                Rule   = if ([string]::IsNullOrWhiteSpace([string]$_.Name)) { '<unnamed>' } else { [string]$_.Name }
                Photos = $items.Count
                Date   = if ($dates.Count -eq 1) { [string]$dates[0] } elseif ($dates.Count -eq 0) { '<partial/unknown>' } else { '{0} dates' -f $dates.Count }
                Place  = if ($places.Count -eq 1) { [string]$places[0] } else { '<CONFLICT>' }
                Roots  = $roots -join ', '
            }
        } |
        Sort-Object -Property @(
            @{ Expression = 'Photos'; Descending = $true },
            @{ Expression = 'Rule'; Descending = $false }
        )
)

Write-Host ''
$grouped | Format-Table -AutoSize | Out-Host

if ($SamplesPerRule -gt 0) {
    Write-Host ''
    Write-Host ("Samples ({0} per rule)" -f $SamplesPerRule)
    Write-Host '--------------------'

    foreach ($group in $grouped) {
        Write-Host ''
        Write-Host ("[{0}] {1} photo(s) -> {2}" -f $group.Rule, $group.Photos, $group.Place)
        $placeItems |
            Where-Object { [string]$_.placeRule -eq $group.Rule } |
            Select-Object -First $SamplesPerRule sourceKey, existingDate |
            Format-Table -AutoSize |
            Out-Host
    }
}

if ([string]::IsNullOrWhiteSpace($ApprovedRulesOutput)) {
    return
}

if ([string]::IsNullOrWhiteSpace($Rules)) {
    throw '-Rules is required when -ApprovedRulesOutput is used.'
}

$approveRuleCount = @(
    $ApproveRule |
        Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) }
).Count
if ($ApproveAll -and $approveRuleCount -gt 0) {
    throw 'Use either -ApproveAll or -ApproveRule, not both.'
}

if (-not $ApproveAll -and $approveRuleCount -eq 0) {
    throw 'Specify -ApproveAll or at least one -ApproveRule when writing approved rules.'
}

$rulesPath = Resolve-ExistingFile -Path $Rules -Label 'Rules'
$rulesData = Get-Content -LiteralPath $rulesPath -Raw | ConvertFrom-Json
$availableRules = @($rulesData.placeRules)

$selectedNames = if ($ApproveAll) {
    @($grouped.Rule | Where-Object { $_ -ne '<unnamed>' })
}
else {
    @($ApproveRule | Sort-Object -Unique)
}

$groupNames = @($grouped.Rule)
$unknownSelections = @($selectedNames | Where-Object { $_ -notin $groupNames })
if ($unknownSelections.Count -gt 0) {
    throw "Selected rule(s) are not present in the report: $($unknownSelections -join ', ')"
}

$selectedRules = @(
    $availableRules |
        Where-Object { [string]$_.name -in $selectedNames }
)

$selectedRuleNames = @($selectedRules | ForEach-Object { [string]$_.name })
$missingDefinitions = @(
    $selectedNames |
        Where-Object { $_ -notin $selectedRuleNames }
)
if ($missingDefinitions.Count -gt 0) {
    throw "Selected rule(s) are missing from the rules file: $($missingDefinitions -join ', ')"
}

$outputObject = [ordered]@{
    inferDateFromFilename = $false
    inferDateFromDirectory = $false
    excludedDateDirectories = @('1970')
    placeRules = $selectedRules
}

$outputFullPath = Resolve-OutputPath -Path $ApprovedRulesOutput
$outputDirectory = [System.IO.Path]::GetDirectoryName($outputFullPath)
if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
    [System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
}

$outputObject |
    ConvertTo-Json -Depth 100 |
    Set-Content -LiteralPath $outputFullPath -Encoding utf8

$approvedPhotoCount = @(
    $placeItems |
        Where-Object { [string]$_.placeRule -in $selectedNames }
).Count

Write-Host ''
Write-Host ("Approved rules written: {0}" -f $outputFullPath)
Write-Host ("Approved rule count:    {0}" -f $selectedRules.Count)
Write-Host ("Approved photo count:   {0}" -f $approvedPhotoCount)
