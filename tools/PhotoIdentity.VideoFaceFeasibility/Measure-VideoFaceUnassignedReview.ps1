<#
.SYNOPSIS
Private read-only aggregate measurement for the WI-0155 unassigned audit CSV.
.DESCRIPTION
Compares every reviewed sample ordinal to the same private catalogue report.
Prints only counts. Biased/stratified samples are NOT an estimate of
detector precision and must not be extrapolated to all unassigned observations.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ReviewedCsvPath,
    [Parameter(Mandatory)][string]$PrivateGroupReportPath,
    [ValidatePattern('^V(?!00)[0-9]{2}$')][string]$ClipAlias = 'V03'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function LocalFile([string]$Path,[long]$Limit) {
    if (-not [IO.Path]::IsPathFullyQualified($Path)) {
        throw 'Expected an absolute private evidence file.'
    }
    $item = Get-Item -LiteralPath $Path -Force -ErrorAction Stop
    $blocked = [long][IO.FileAttributes]::ReparsePoint -bor
        [long][IO.FileAttributes]::Offline -bor [long]0x00040000 -bor [long]0x00400000
    if ($item.PSIsContainer -or $item.Length -lt 1 -or $item.Length -gt $Limit -or
        ([long]$item.Attributes -band $blocked) -ne 0 -or
        ((Get-Item -LiteralPath $item.DirectoryName -Force).Attributes -band
          [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw 'Unsafe, non-local or oversized private evidence input.'
    }
    return $item.FullName
}

try {
    $csvPath = LocalFile $ReviewedCsvPath (64KB)
    $reportPath = LocalFile $PrivateGroupReportPath (64MB)
    $rows = @(Import-Csv -LiteralPath $csvPath -Encoding utf8)
    $header = @('Clip','Sample','Frame','Face','Label','PersonAlias')
    if ($rows.Count -lt 1 -or $rows.Count -gt 96 -or
        (@($rows[0].PSObject.Properties.Name) -join '|') -cne
        ($header -join '|')) {
        throw 'Audit review CSV has unexpected columns or row count.'
    }
    $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
    $clips = @($report.clips | Where-Object { $_.alias -ceq $ClipAlias })
    if ($report.schemaVersion -ne 1 -or $clips.Count -ne 1) {
        throw 'Private report does not identify the requested video once.'
    }
    $review = $clips[0].review.diagnostics.provisionalGroups
    $audit = $review.unassignedAudit
    if ($review.schemaVersion -ne 1 -or $audit.schemaVersion -ne 1 -or
        [int]$review.observationsConsidered -ne
          [int]$clips[0].review.aggregate.facesDetected -or
        [int]$review.observationsAssigned + [int]$review.observationsUnassigned -ne
          [int]$review.observationsConsidered -or
        @($review.groups).Count -ne 64 -or
        [int]$audit.totalUnassignedObservations -ne
          [int]$review.observationsUnassigned -or
        [int]$audit.maximumReviewSamples -ne 96 -or
        [int]$audit.temporalStrata -ne 12) {
        throw 'Private report has invalid or incomplete audit evidence.'
    }
    $samples = @($audit.sampledCrops | Sort-Object timestampMs,frameNumber,faceNumber)
    if ($samples.Count -lt 1 -or $samples.Count -gt 96 -or
        $rows.Count -ne $samples.Count -or
        $samples.Count -gt [int]$audit.totalUnassignedObservations) {
        throw 'CSV row count is not the entire matched audit sample.'
    }

    $labels = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    foreach ($s in @('','Real face','Not a face','Cannot tell')) {
        [void]$labels.Add($s)
    }
    $seen = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    $real = 0; $background = 0; $uncertain = 0; $unreviewed = 0
    $known = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    $knownObservations = 0; $unknownObservations = 0
    $realConf70 = 0; $falseConf70 = 0
    $realQuality50 = 0; $falseQuality50 = 0

    for ($i=0; $i -lt $rows.Count; $i++) {
        $row = $rows[$i]
        $sample = $samples[$i]
        $alias = [string]$row.PersonAlias
        $label = [string]$row.Label
        $key = '{0}:{1}' -f $row.Frame,$row.Face
        if ($row.Clip -cne $ClipAlias -or
            $row.Sample -cne ('S{0:D3}' -f ($i+1)) -or
            [string]$row.Frame -cne [string]$sample.frameNumber -or
            [string]$row.Face -cne [string]$sample.faceNumber -or
            -not $seen.Add($key) -or -not $labels.Contains($label) -or
            ($alias -cne '' -and
             ($label -cne 'Real face' -or
              $alias -cnotmatch '^(?:P(?!00$|0000$)(?:[0-9]{2}|[0-9]{4})|UNKNOWN)$'))) {
            throw 'Review CSV sample, label or person alias mismatches the private audit.'
        }

        switch -CaseSensitive ($label) {
            'Real face' {
                $real++
                if ([double]$sample.detectorConfidence -ge 0.7) { $realConf70++ }
                if ([double]$sample.quality -ge 0.5) { $realQuality50++ }
                if ($alias -ceq 'UNKNOWN') {
                    $unknownObservations++
                } elseif ($alias) {
                    [void]$known.Add($alias)
                    $knownObservations++
                }
            }
            'Not a face' {
                $background++
                if ([double]$sample.detectorConfidence -ge 0.7) { $falseConf70++ }
                if ([double]$sample.quality -ge 0.5) { $falseQuality50++ }
            }
            'Cannot tell' { $uncertain++ }
            default { $unreviewed++ }
        }
    }

    # NO person aliases, group aliases, frame ordinals, private paths,
    # timestamps or individual detector scores are printed.
    [pscustomobject][ordered]@{
        SchemaVersion = 1
        SourceEvidenceValidated = $true
        TotalUnassignedObservations = [int]$audit.totalUnassignedObservations
        SampledObservations = $samples.Count
        UnrepresentedObservations = [int]$audit.totalUnassignedObservations - $samples.Count
        ReviewedSamples = $real + $background + $uncertain
        RealFaceSamples = $real
        BackgroundSamples = $background
        CannotTellSamples = $uncertain
        UnreviewedSamples = $unreviewed
        RealFaceSamplesWithKnownPersonAlias = $knownObservations
        RealFaceSamplesWithUnknownPerson = $unknownObservations
        DistinctKnownPeopleInSample = $known.Count
        RealFaceSamplesAtDetectorConfidence070 = $realConf70
        BackgroundSamplesAtDetectorConfidence070 = $falseConf70
        RealFaceSamplesAtCropQuality050 = $realQuality50
        BackgroundSamplesAtCropQuality050 = $falseQuality50
        SamplingIsQualityBiased = $true
        SuppressionRulesValidated = $false
    }
}
catch {
    # Never echo exception detail, paths, identities or review contents.
    throw 'WI-0155 private unassigned review rejected inconsistent or unsafe inputs.'
}
