#requires -Version 7.0
# Smoke test the WI-0150 survey with absent, empty and populated format tags.
# Uses a synthetic ffprobe executable and throwaway inputs, never private videos.
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $IsWindows) {
    throw 'The synthetic ffprobe regression test requires a Windows runner.'
}

$surveyScript = Join-Path $PSScriptRoot 'VideoArchiveCodecSurvey.ps1'
$workspace = Join-Path ([IO.Path]::GetTempPath()) ("photoidentity-video-survey-" + [Guid]::NewGuid().ToString('N'))
$archive = Join-Path $workspace 'archive'
$project = Join-Path $workspace 'probe'
$buildOutput = Join-Path $workspace 'output'

try {
    [void](New-Item -Path $archive, $project, $buildOutput -ItemType Directory -Force)
    [IO.File]::WriteAllBytes((Join-Path $archive 'test.mov'), [byte[]](1, 2, 3, 4))
    [IO.File]::WriteAllBytes((Join-Path $archive 'test.mp4'), [byte[]](1, 2, 3, 4))

    Set-Content -LiteralPath (Join-Path $project 'VideoSurveyProbeFixture.csproj') -Encoding utf8 -Value @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <UseAppHost>true</UseAppHost>
  </PropertyGroup>
</Project>
'@

    Set-Content -LiteralPath (Join-Path $project 'Program.cs') -Encoding utf8 -Value @'
using System;

string mode = Environment.GetEnvironmentVariable("PHOTOIDENTITY_SURVEY_TEST_TAG_MODE") ?? "missing";
string tags = mode switch
{
    "empty" => ",\"tags\":{}",
    "present" => ",\"tags\":{\"creation_time\":\"2026-10-01T00:00:00Z\",\"location\":\"dummy\"}",
    _ => ""
};
Console.WriteLine("{\"format\":{\"format_name\":\"mov,mp4\",\"duration\":\"35\"" + tags +
    "},\"streams\":[{\"codec_type\":\"video\",\"codec_name\":\"h264\",\"width\":1920,\"height\":1080," +
    "\"avg_frame_rate\":\"30/1\"},{\"codec_type\":\"audio\",\"codec_name\":\"aac\"}]}");
'@

    dotnet build (Join-Path $project 'VideoSurveyProbeFixture.csproj') --configuration Release --output $buildOutput --verbosity quiet
    if ($LASTEXITCODE -ne 0) {
        throw 'Failed to build the synthetic ffprobe fixture.'
    }

    $probe = Join-Path $buildOutput 'VideoSurveyProbeFixture.exe'
    if (-not (Test-Path -LiteralPath $probe -PathType Leaf)) {
        throw 'Synthetic ffprobe executable not produced.'
    }

    foreach ($mode in @('missing', 'empty', 'present')) {
        $env:PHOTOIDENTITY_SURVEY_TEST_TAG_MODE = $mode
        $reportPath = Join-Path $workspace "survey-$mode.json"
        & $surveyScript -ArchiveRoot $archive -FfprobePath $probe -SamplesPerExtension 1 -OutputPath $reportPath

        $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
        if ($report.schema_version -ne 2 -or @($report.sampled_media).Count -ne 2) {
            throw "The $mode tag fixture did not generate two survey rows."
        }

        foreach ($stat in @($report.statistics)) {
            if ($stat.probed_ok -ne 1 -or $stat.locally_available -ne 1) {
                throw "The $mode tag fixture did not complete its $($stat.extension) probe."
            }
        }
        foreach ($row in @($report.sampled_media)) {
            if ($row.video_codec -ne 'h264' -or $row.audio_codec -ne 'aac' -or $row.duration_bucket -ne '15_seconds_to_2_minutes') {
                throw "The $mode tag fixture lost expected codec/duration metadata."
            }
            $expected = $mode -eq 'present'
            if ([bool]$row.creation_time_present -ne $expected -or
                [bool]$row.location_tag_present -ne $expected) {
                throw "The $mode tag fixture did not classify optional metadata tags."
            }
        }

        Write-Host "PASS video survey metadata: $mode format tags"
    }

    # The synthetic originals remain unchanged after probing.
    foreach ($extension in @('.mov', '.mp4')) {
        $bytes = [IO.File]::ReadAllBytes((Join-Path $archive "test$extension"))
        if ([Convert]::ToHexString($bytes) -ne '01020304') {
            throw "Survey modified the synthetic $extension original."
        }
    }
}
finally {
    Remove-Item Env:\PHOTOIDENTITY_SURVEY_TEST_TAG_MODE -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $workspace -Recurse -Force -ErrorAction SilentlyContinue
}
