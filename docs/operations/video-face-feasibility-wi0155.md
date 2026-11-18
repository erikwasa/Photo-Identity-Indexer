# WI-0155 — bounded offline video-face verification (main candidate)

## In-app private review (post-PR #824 development branch)

The previous HTML gallery remains available for diagnostics. New crop-level
review should happen in Photo Identity's **Video face review** workspace,
reachable from the **Videos** page or **Advanced → Archive operations**.

The existing source-validation runner now also generates a self-contained
private JSON package with the same G-group crop keys and report digest. This
uses only the already-approved extracted local frame evidence and creates a
new private derived output; it does **not** open original videos or contact
the catalogue. Do not distribute this JSON: it contains face images.

After this PR merges and local main has been updated, with the approved
private paths and the existing tool directory variable from above:

```powershell
$alias = 'V06'
$gallery = & (Join-Path $tools 'New-VideoFaceGroupReview.ps1') `
  -PrivateReportPath $reportPath -PrivateCasesPath $casesPath `
  -ClipAlias $alias -GroupSource Provisional -PresenceFirst

$gallery | Format-List Mode,Groups,Crops,WebReviewBundlePath,ProductionAssignmentsMade
if ($gallery.ProductionAssignmentsMade -ne 0 -or
    -not (Test-Path -LiteralPath $gallery.WebReviewBundlePath -PathType Leaf)) {
  throw 'Private web review bundle missing or unexpected assignment'
}
```

Open **Video face review** in the *local* web app and choose the displayed
WebReviewBundlePath file. Review a known person from one crop, leave other
groups unreviewed, try hide/reveal, export CSV, and import the CSV back.
Check that distinct-person counts and deliberately unreviewed decisions
survive, then validate the exported review with the same authoritative
source-bound PowerShell script:

```powershell
$reviewed = (Read-Host 'Exported private in-app CSV path').Trim('"')
$metrics = & (Join-Path $tools 'Measure-VideoFacePresenceFirstReview.ps1') `
  -ReviewedCsvPath $reviewed -PrivateGroupReportPath $reportPath `
  -PrivatePeopleMapPath $peopleMapPath -ClipAlias $alias
$metrics | Format-List InputEvidenceValidated,KnownPersonPresenceProposals,UnreviewedRepresentativeCrops,PersonCoverageProven,ProductionAssignmentsMade
if (-not $metrics.InputEvidenceValidated -or
    $metrics.PersonCoverageProven -or
    $metrics.ProductionAssignmentsMade -ne 0) {
  throw 'In-app review did not retain safe source-bound decisions'
}
```

This is **one** local post-merge smoke test, not a request to reclassify all
53 V06 groups or rerun face inference. New in-app review decisions are
private *proposals* and are not applied to production video/person tags.
Browser file selection happens entirely client-side; do not open the private
package through a public deployment. The browser keeps no saved session:
explicitly export before navigation. The original report, map, HTML/CSV
and reconstructed crops remain under operator-controlled retention; the
new JSON file also needs explicit local cleanup when no longer needed.

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
## Local acceptance checkpoint — V04 complete; limited V06 smoke still needed

The maintainer completed a real **V04** presence-first gallery on
the main-based candidate. Six groups / six source-bound crops were
generated; the private exported CSV passed the strict aggregate
measurement with **4 confirming crops → 2 distinct known people**,
2 background crops, zero unknown/unreviewed/mixed/excluded and
**zero production identity writes**. Browser rendering, export
and validation are supported by this evidence. We do **not** yet
have a distinct maintainer report that re-import of the CSV
worked. V04 is all-reviewed, so it does not test stopping early.

**Minimal outstanding local check, not another exhaustive review:**
Open V06 in the same opt-in gallery, review **just a few**
representative crops, leave most G groups untouched and optionally
hide/reveal one. Save and **Import the saved CSV in the browser**,
verify that reviewed and unreviewed decisions are restored, then
measure it with `-ClipAlias V06`. Expect 53 provisional
groups / 53 representative crops, nonzero
`GroupsWithoutReviewedCrops` and
`UnreviewedRepresentativeCrops`, source validity True,
`PersonCoverageProven=False`, zero production writes. Use
correctly identified private aliases only where a human actually
recognizes the crop; otherwise choose Cannot tell/Not a face or
leave unreviewed. Do not force any merge, inspect all 53
groups, invent identities or rerun the 204-frame catalogue
evaluation.

**The V04 private CSV itself is not part of this PR.** Report
only the safe aggregated counters and whether the V06 Import
round-trip worked. Existing private evidence remains untouched.
## V06 partial-review acceptance checkpoint — 2026-11-18

The maintainer's **real V06** presence-first gallery returned
53 groups, 53 representative crops and no production writes.
The private CSV passed source-bound validation. Of 53 crops,
**33 were reviewed, 20 remained unreviewed**. Human review
included 15 known-person confirmations deduplicated to **7**
distinct known people (8 duplicate crop confirmations),
8 unknown-face observations, 7 background objects and 3
uncertain observations. There were 20 groups with no reviewed
crop, zero mixed-group annotations and exclusions, and zero
unshown/unassigned G observations. Coverage remains unproven.

Do **not** infer eight distinct unknown people from the eight
unknown *crop* decisions or assume the 20 unreviewed groups
contain no additional known people. This is evidence that
group completion is optional, not proof of full recognition.
There are no source/media or production identity writes.

**One remaining browser-only check:** Use `Import saved CSV`
in the existing V06 gallery. Confirm decisions previously
saved on one reviewed crop and one still-unreviewed group
reappear, and that the view continues to show 7 distinct
known-person proposals, 20 unreviewed crops and 20 groups
with no reviewed crop. Do not rerun any inference or re-label
all groups to check the import. The existing saved CSV has
already been accepted by the PowerShell source validator.

Current CI caveat: a subsequent CI run for the documentation
checkpoint had a failure in an unchanged Windows
VideoGuardedToolExecutor photo-priority process-shutdown test;
all video-face unit tests and both integration shards passed.
Do not promote to main until CI clears and any intermittent
shutdown defect is understood.

## Local V06 import checkpoint — PASS (2026-11-18)

The maintainer confirmed **V06 CSV Import: PASS** in the private
presence-first browser, after generating the 53-crop V06 gallery
and validating its exported CSV with the source-bound local
PowerShell verifier. The original review intentionally left
20 of 53 representative crops/groups unreviewed and deduplicated
15 known confirmation crops to 7 distinct known people.
The saved review and source evidence remain private. No further
V06 group review, video extraction or inference is required for
this acceptance. Source completeness and production identity
assignment are **not** established by this check.
