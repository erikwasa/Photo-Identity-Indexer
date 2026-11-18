# WI-0155 — bounded offline video-face verification (main candidate)

This is the **supported integration runbook** for the main-based
**draft** candidate. The longer historical experiment diary remains
on the `wi-0155` branch; it is not required to use the offline tools.

**Scope:** Windows PowerShell 7.6, operator-approved already-local video
frames, private sidecar evidence and explicit execution. Nothing in
`src/` invokes this code automatically. No original video modifications,
background inference, OneDrive hydration, still-photo face enrolment,
production identity writes or migrations are authorized. A presence
proposal in a private worksheet is **not** a persisted person assignment.

## 1. Synthetic verification — no models or private files

After checking out the candidate branch into a separate local worktree:

```powershell
$ErrorActionPreference = 'Stop'
$tools = Join-Path (Get-Location).Path 'tools\PhotoIdentity.VideoFaceFeasibility'
dotnet test .\tests\PhotoIdentity.VideoFaceFeasibility.Tests\PhotoIdentity.VideoFaceFeasibility.Tests.csproj -c Release
if ($LASTEXITCODE -ne 0) { throw 'Video face C# tests failed' }

foreach ($script in @(
  'Test-VideoFacePresenceFirstReview.ps1',
  'Test-VideoFaceGroupReview.ps1',
  'Test-VideoFaceGroupRankAccuracy.ps1',
  'Test-VideoFaceShadowGroupReview.ps1',
  'Test-VideoFaceRefinementComparison.ps1',
  'Test-VideoFacePresenceResolution.ps1'
)) {
  & (Join-Path $tools $script)
}

foreach ($script in @(
  'Test-VideoFacePresenceFirstReview.js',
  'Test-VideoFaceMergeChainValidation.js',
  'Test-VideoFaceGroupMergeAssist.js'
)) {
  node (Join-Path $tools $script)
  if ($LASTEXITCODE -ne 0) { throw "Node regression failed: $script" }
}
```

Expected: every check PASS. The workflow also verifies these on Windows
CI. Do not interpret a passing synthetic test as real-person accuracy.

## 2. Real-data gallery acceptance, reusing existing four-pass evidence

Re-enter the existing **absolute private paths** if the previous
PowerShell session has ended: `$reportPath`, `$casesPath`
and `$peopleMapPath`. These are the previously approved four-pass
catalogue evaluation. `$casesPath` stays in the separate
`case-input` directory. Do not re-run video extraction, detector
inference or PostgreSQL evaluation just to verify gallery mechanics.

```powershell
foreach ($name in 'reportPath','casesPath','peopleMapPath') {
  $existing = Get-Variable -Name $name -ValueOnly -ErrorAction SilentlyContinue
  if (-not $existing -or -not (Test-Path -LiteralPath $existing -PathType Leaf)) {
    Set-Variable -Name $name -Value ((Read-Host "Existing $name path").Trim('"'))
  }
}

$alias = 'V04'
$gallery = & (Join-Path $tools 'New-VideoFaceGroupReview.ps1') `
  -PrivateReportPath $reportPath -PrivateCasesPath $casesPath `
  -ClipAlias $alias -GroupSource Provisional -PresenceFirst -OpenGallery

$gallery | Format-List Mode,Groups,Crops,ObservationsUnassigned,
  UnshownGroupObservations,ProductionAssignmentsMade,GalleryPath,WorksheetPath
```

Expected V04: **6 groups and 6 representative crops**.
V06: **53 groups and 53 representative crops**. No production writes.
In the browser, confirm one known person in two separate groups
using an eligible private P0001-style alias, without merging those
groups. The aggregate should show only one known person. Leave
another group unreviewed, hide it, reveal it, Save CSV, Import
saved CSV and confirm decisions remain intact.

**Do not invent a mixed group in V04/V06.** Their coarse
groups had only one representative crop each. Synthetic regression
covers a multi-crop group with two different people, independent
identity confirmation and a crop-level exclusion annotation.
Such annotations never rewrite source clustering.

## 3. Measure private reviewed presence, source-bound

```powershell
$reviewed = (Read-Host 'Saved presence-first CSV path').Trim('"')
$metrics = & (Join-Path $tools 'Measure-VideoFacePresenceFirstReview.ps1') `
  -ReviewedCsvPath $reviewed -PrivateGroupReportPath $reportPath `
  -PrivatePeopleMapPath $peopleMapPath -ClipAlias $alias

$metrics | Format-List

if (-not $metrics.InputEvidenceValidated -or
    $metrics.ProductionAssignmentsMade -ne 0 -or
    $metrics.PersonCoverageProven) {
  throw 'Private presence or no-write evidence check failed'
}
```

Only share the aggregate fields. Do **not** share the private
worksheet, detected face images, names/aliases, hashes, individual
suggestions, source filenames or approval-specific media.

## 4. Before any merge to main

- All main-based CI stages, solution tests and docs generation PASS.
- Local V04/V06 presence-first gallery and private Save/Import PASS.
- Review the rights, licensing and redistribution requirements for
  YuNet, SFace, CenterFace, FFmpeg and their model weights/runtimes.
- Confirm resource/frame storage caps, expiry/cleanup for private
  crops and safe rollback; do not silently delete existing evidence.
- Verify no unintended production API/worker/job registration,
  database writes, original video edits or cloud hydration.
- Sign off the initial boundary: **offline opt-in module only**.
  Any future persistent video–person editing or exceptional calibrated
  automatic assignment is separately scoped and remains blocked.
- Distinguish full-video sampling coverage from early *human* review
  stopping. Unreviewed groups/observations never imply no other people.

See [decisions, historical evidence and removal inventory](video-face-wi0155-pre-main-decision-record.md).
