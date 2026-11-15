<#
.SYNOPSIS
Measure draft video-person presence once from validated WI-0155 C/N/U review.
.DESCRIPTION
Read-only human-reviewed presence, deduplicated across groups and sampled
noise/disagreement. UNKNOWN and real unlabelled faces stay unresolved. Only
aggregate metrics go to stdout; an optional private JSON with pseudonym
aliases can be written once to the local WI0155 folder. No database access,
production identity writes, video models, source images or exemplars.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ReviewedCsvPath,
    [Parameter(Mandatory)][string]$PrivateGroupReportPath,
    [Parameter(Mandatory)][string]$PrivatePeopleMapPath,
    [Parameter(Mandatory)][ValidatePattern('^V(?!00)[0-9]{2}$')]
    [string]$ClipAlias,
    [string]$PrivateDraftPath
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-LocalEvidence([string]$Path,[long]$Limit) {
    if (-not [IO.Path]::IsPathFullyQualified($Path)) {
        throw 'Expected absolute local private evidence.'
    }
    $file = Get-Item -LiteralPath $Path -Force -ErrorAction Stop
    $blocked = [long][IO.FileAttributes]::ReparsePoint -bor
        [long][IO.FileAttributes]::Offline -bor [long]0x00040000 -bor [long]0x00400000
    if ($file.PSIsContainer -or $file.Length -lt 1 -or
        $file.Length -gt $Limit -or
        (([long]$file.Attributes -band $blocked) -ne 0) -or
        (([long](Get-Item -LiteralPath $file.DirectoryName -Force).Attributes -band
          [long][IO.FileAttributes]::ReparsePoint) -ne 0)) {
        throw 'Private evidence is linked, offline or outside size limits.'
    }
    return $file.FullName
}
$stage = 'inputs'
try {
    if (-not $IsWindows -or -not $env:LOCALAPPDATA) {
        throw 'Requires Windows PowerShell 7.6.'
    }
    $csv = Assert-LocalEvidence $ReviewedCsvPath (256KB)
    $reportPath = Assert-LocalEvidence $PrivateGroupReportPath (64MB)
    $mapPath = Assert-LocalEvidence $PrivatePeopleMapPath (4MB)
    $csvHash = (Get-FileHash -LiteralPath $csv -Algorithm SHA256).Hash.ToLowerInvariant()
    $reportHash = (Get-FileHash -LiteralPath $reportPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $mapHash = (Get-FileHash -LiteralPath $mapPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $stage = 'source-validation'
    $inputArgs = @{
        ReviewedCsvPath=$csv
        PrivateGroupReportPath=$reportPath
        PrivatePeopleMapPath=$mapPath
        ClipAlias=$ClipAlias
    }
    $validated = & (Join-Path $PSScriptRoot 'Measure-VideoFaceReviewWorkspace.ps1') @inputArgs
    if (-not $validated.CombinedEvidenceValidated -or
        -not $validated.GroupMetrics.PrivateInputValidated -or
        -not $validated.RankMetrics.InputEvidenceValidated -or
        -not $validated.NoiseMetrics.SourceEvidenceValidated -or
        -not $validated.UncorroboratedMetrics.SourceEvidenceValidated) {
        throw 'Review evidence was not validated.'
    }

    $stage = 'group-representatives'
    $report = Get-Content -LiteralPath $reportPath -Raw -Encoding utf8 |
        ConvertFrom-Json
    $clips = @($report.clips | Where-Object alias -CEQ $ClipAlias)
    if ($clips.Count -ne 1 -or
        [int]$report.schemaVersion -ne 1) {
        throw 'Private source clip missing.'
    }
    $cf = $clips[0].review.diagnostics.centerFaceCounterfactual
    $reviewGroups = @($cf.corroboratedOnlyDbscan.groups)
    $frames = @{}
    foreach ($group in $reviewGroups) {
        $id = [string]$group.alias -creplace '^D','C'
        if ($id -cnotmatch '^C(?!000)[0-9]{3}$' -or
            $frames.ContainsKey($id) -or
            @($group.representativeCrops).Count -lt 1) {
            throw 'Invalid private group representative identity.'
        }
        $list = [Collections.Generic.List[int]]::new()
        foreach ($crop in @($group.representativeCrops)) {
            $frame = [int]$crop.frameNumber
            if ($frame -lt 1 -or $frame -gt 180) {
                throw 'Invalid representative frame.'
            }
            $list.Add($frame)
        }
        $frames[$id] = @($list.ToArray())
    }

    $stage = 'presence-resolution'
    $rows = @(Import-Csv -LiteralPath $csv -Encoding utf8)
    $groupRows = @($rows | Where-Object Source -CEQ 'CenterFaceShadow')
    $audits = @($rows | Where-Object {
        [string]$_.Source -cin @('CenterFaceNoise','CenterFaceUncorroborated')
    })
    # The audit validators check label syntax and source sample identity,
    # but do not resolve audit pseudonyms against the catalogue people map.
    # A syntactically valid yet nonexistent alias must NOT become a known
    # video/person presence proposal.
    $privateMap = Get-Content -LiteralPath $mapPath -Raw -Encoding utf8 |
        ConvertFrom-Json
    $eligible = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    foreach ($person in @($privateMap.people)) {
        $alias = [string]$person.alias
        if ($alias -cnotmatch '^P(?!0000$)[0-9]{4}$' -or
            -not $eligible.Add($alias)) {
            throw 'Private catalogue alias map is not source-consistent.'
        }
    }
    if ([int]$privateMap.schemaVersion -ne 1 -or
        $eligible.Count -ne [int]$report.catalogue.selectedPeople) {
        throw 'Private catalogue people count changed.'
    }
    foreach ($row in $rows) {
        $person = if ([string]$row.Source -ceq 'CenterFaceShadow') {
            [string]$row.SelectedPerson
        } else { [string]$row.PersonAlias }
        if ($person -cne '' -and $person -cne 'UNKNOWN' -and
            -not $eligible.Contains($person)) {
            throw 'Reviewer alias is not part of the eligible catalogue snapshot.'
        }
    }
    Import-Module (Join-Path $PSScriptRoot 'VideoFacePresenceResolution.psm1') -Force
    $resolved = Resolve-VideoFacePresenceEvidence -GroupRows $groupRows -AuditRows $audits -RepresentativeFrames $frames

    # Do not report a person as absent. N/U audits can be biased and
    # capped, and some actual faces may not have been detected.
    $summary = [pscustomobject][ordered]@{
        SchemaVersion=1
        ClipAlias=$ClipAlias
        CombinedEvidenceValidated=$true
        KnownPersonPresenceProposals=$resolved.Metrics.KnownPersonPresenceProposals
        KnownPeopleFromRetainedGroups=$resolved.Metrics.KnownPeopleFromRetainedGroups
        KnownPeopleFromAuditsOnly=$resolved.Metrics.KnownPeopleFromAuditsOnly
        ReviewerKeptGroupRoots=$resolved.Metrics.ReviewerKeptGroupRoots
        ReviewerMergeActions=$resolved.Metrics.ReviewerMergeActions
        UnknownGroupEvidenceUnits=$resolved.Metrics.UnknownGroupEvidenceUnits
        UnlabelledKeptGroupRoots=$resolved.Metrics.UnlabelledKeptGroupRoots
        OtherUnresolvedGroups=$resolved.Metrics.OtherUnresolvedGroups
        UnreviewedAuditSamples=$resolved.Metrics.UnreviewedAuditSamples
        CannotTellAuditSamples=$resolved.Metrics.CannotTellAuditSamples
        RealFaceAuditSamplesUnidentified=$resolved.Metrics.RealFaceAuditSamplesUnidentified
        RealFaceAuditSamplesMarkedUnknown=$resolved.Metrics.RealFaceAuditSamplesMarkedUnknown
        RealFaceNoiseSamples=$resolved.Metrics.RealFaceNoiseSamples
        RealFaceUncorroboratedSamples=$resolved.Metrics.RealFaceUncorroboratedSamples
        UnsampledNoiseObservations=$validated.NoiseMetrics.UnrepresentedObservations
        UnsampledUncorroboratedObservations=$validated.UncorroboratedMetrics.UnrepresentedObservations
        PersonCoverageProven=$false
        ProductionAssignmentsMade=0
        PrivateDraftWritten=$false
    }

    $stage = 'input-immutability'
    if ((Get-FileHash -LiteralPath $csv -Algorithm SHA256).Hash.ToLowerInvariant() -cne $csvHash -or
        (Get-FileHash -LiteralPath $reportPath -Algorithm SHA256).Hash.ToLowerInvariant() -cne $reportHash -or
        (Get-FileHash -LiteralPath $mapPath -Algorithm SHA256).Hash.ToLowerInvariant() -cne $mapHash) {
        throw 'Private evidence changed during measurement.'
    }

    if (-not [string]::IsNullOrWhiteSpace($PrivateDraftPath)) {
        $stage = 'private-draft'
        $root = Join-Path $env:LOCALAPPDATA 'PhotoIdentity\WI0155\reviewed-presence'
        $expected = [IO.Path]::GetFullPath($root)
        $output = [IO.Path]::GetFullPath($PrivateDraftPath)
        $fileName = [IO.Path]::GetFileName($output)
        if (-not [IO.Path]::IsPathFullyQualified($PrivateDraftPath) -or
            -not [string]::Equals((Split-Path -Parent $output),$expected,
                [StringComparison]::OrdinalIgnoreCase) -or
            $fileName -cnotmatch '^wi0155-V[0-9]{2}-presence-[A-Za-z0-9_-]{1,48}\.json$' -or
            -not $fileName.StartsWith(('wi0155-' + $ClipAlias + '-presence-'),
                [StringComparison]::Ordinal)) {
            throw 'Private proposal output path is outside the approved local folder.'
        }
        $ancestors = @($env:LOCALAPPDATA,
            (Join-Path $env:LOCALAPPDATA 'PhotoIdentity'),
            (Join-Path $env:LOCALAPPDATA 'PhotoIdentity\WI0155'),$root)
        foreach ($folder in $ancestors) {
            if (-not (Test-Path -LiteralPath $folder -PathType Container)) {
                New-Item -ItemType Directory -Path $folder -ErrorAction Stop | Out-Null
            }
            if (((Get-Item -LiteralPath $folder -Force).Attributes -band
                  [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw 'Private output folder must not be a link.'
            }
        }
        $draft = [pscustomobject][ordered]@{
            schemaVersion=1
            clipAlias=$ClipAlias
            status='human-reviewed-candidate-only'
            reportSha256=$reportHash
            combinedReviewSha256=$csvHash
            privatePeopleMapSha256=$mapHash
            personCoverageProven=$false
            productionAssignmentsMade=0
            # Only private P-aliases and support counts; no names,
            # canonical IDs, crops, frame timestamps or locations.
            knownPersonProposals=@($resolved.People | ForEach-Object {
                [pscustomobject][ordered]@{
                    personAlias=$_.Alias
                    retainedGroupRoots=$_.GroupRoots
                    reviewedGroupFragments=$_.GroupFragments
                    noiseSampleEvidence=$_.NoiseSamples
                    uncorroboratedSampleEvidence=$_.UncorroboratedSamples
                }
            })
            unknownGroupEvidenceUnits=$resolved.Metrics.UnknownGroupEvidenceUnits
            realFaceAuditSamplesMarkedUnknown=$resolved.Metrics.RealFaceAuditSamplesMarkedUnknown
        }
        $json = $draft | ConvertTo-Json -Depth 8
        $bytes = [Text.UTF8Encoding]::new($false).GetBytes($json)
        $stream = [IO.FileStream]::new($output,[IO.FileMode]::CreateNew,
            [IO.FileAccess]::Write,[IO.FileShare]::None)
        try {
            $stream.Write($bytes,0,$bytes.Length)
            $stream.Flush($true)
        } finally {
            $stream.Dispose()
        }
        $summary.PrivateDraftWritten=$true
    }
    $summary
} catch {
    # No private aliases, paths, notes or source data in error output.
    throw ('WI-0155 reviewed presence proposal rejected private evidence (stage: ' + $stage + ').')
}
