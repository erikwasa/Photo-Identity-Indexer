# WI-0210 checkpoint 7 — exact local original SHA proof

This is a **separately authorized** proof of one deliberately selected
Rec.709 `yuvj420p` archive revision. It is not FFprobe inspection,
conversion, worker admission, hydration or derivative publishing.

## Prerequisite and protections

The maintained-host post-merge PR #727 `local-candidates` command
succeeded on production `photoidentity_main`: 12 candidates,
12 local matches, read-only, no source contents opened and no
OneDrive hydration requested. Those results establish only the
source attribute and size observation, NOT SHA-256 or colour fidelity.

The existing `video one-shot source-proof` now accepts an optional
`--require-rec709-fullrange-pixel-only` flag to require the exact
WI-0210 metadata class **before reading source contents**. It must
still independently recheck currentness, exclusions, stopped
video worker/idle photos, local availability and unsafe symlink
ancestry, and uses bounded Windows FILE_FLAG_OPEN_NO_RECALL SHA-256
reads with a 256 MiB / 3-minute maximum. No source content is ever
opened by the default dry-run. Nothing is written or published.
The original-path, SHA, private metadata and UUID are not reported.

## Verification on the Windows host — ONLY after PR merge on main

CI runs docs validation, generation, builds and disposable
PostgreSQL tests; do not repeat the docs tests locally.

1. Update `main` and obtain a current **private** shortlist.

~~~powershell
cd "C:\Kod\codex\Photo Identity Indexer"
git switch main
git pull --ff-only origin main

$db = "photoidentity_main"
$envName = "PHOTOIDENTITY_POSTGRES_CONNECTION_STRING"

$lines = & dotnet run --project src/PhotoIdentity.Cli --configuration Release -- video compatibility local-candidates --postgres-connection-env $envName --database $db --show-revision-ids
if ($LASTEXITCODE -ne 0) { throw "Local shortlist failed." }

$selection = ($lines -join "`n") | ConvertFrom-Json -ErrorAction Stop
if (-not $selection.readOnly -or $selection.sourceContentOpened -or
    $selection.oneDriveHydrationRequested) {
    throw "Local shortlist violated safety contract."
}
$selection | Select-Object candidateCount, locallyMatchedCount
$selection.candidates | Format-Table revisionId, sourceBytes, durationSeconds, codedOrientation, hasAudio -AutoSize
~~~

2. Select **one** current candidate UUID privately. Never paste
UUIDs, paths, camera metadata or credentials in GitHub or chat.

~~~powershell
$revision = [Guid](Read-Host "Select one exact local candidate UUID")
if (@($selection.candidates | Where-Object {
    [Guid]$_.revisionId -eq $revision
}).Count -ne 1) {
    throw "Chosen UUID was not in the current local shortlist."
}
~~~

3. Dry-run the new **exact-class** proof without opening the source
or connecting to PostgreSQL.

~~~powershell
$proofArgs = @(
    "run", "--project", "src/PhotoIdentity.Cli",
    "--configuration", "Release", "--",
    "video", "one-shot", "source-proof",
    "--postgres-connection-env", $envName,
    "--database", $db,
    "--revision-id", $revision.ToString(),
    "--require-rec709-fullrange-pixel-only"
)
$dry = & dotnet @proofArgs
if ($LASTEXITCODE -ne 0) { throw "SHA proof dry-run failed." }

$dryReport = ($dry -join "`n") | ConvertFrom-Json -ErrorAction Stop
if ($dryReport.status -ne "not-run" -or -not $dryReport.dryRun -or
    -not $dryReport.requireRec709FullRangePixelOnly -or
    $dryReport.originalBytesOpened -or
    $dryReport.oneDriveHydrationRequested) {
    throw "Unexpected dry-run safety state."
}
$dryReport | Select-Object status, dryRun, requireRec709FullRangePixelOnly, originalBytesOpened
~~~

4. **Optional and separately authorized original read.** Only if
you deliberately approve hashing this *one* already-local
original, add `--read-local-bytes`. This step opens bytes via
no-recall; it does not decode, convert, write or enqueue.

~~~powershell
# Do not run without your explicit decision to read one original.
$proofOutput = & dotnet @proofArgs --read-local-bytes
$proofExitCode = $LASTEXITCODE
$proofReport = ($proofOutput -join "`n") | ConvertFrom-Json -ErrorAction Stop
$proofReport | Select-Object status, revisionDigestMatched,
    sourceMetadataStable, originalBytesOpened,
    requireRec709FullRangePixelOnly, oneDriveHydrationRequested,
    jobsEnqueued, realVideoExecutionAuthorized

if ($proofExitCode -ne 0) {
    throw "Source proof refused: $($proofReport.status)."
}
if ($proofReport.status -ne "verified" -or
    -not $proofReport.revisionDigestMatched -or
    -not $proofReport.sourceMetadataStable -or
    -not $proofReport.originalBytesOpened -or
    -not $proofReport.requireRec709FullRangePixelOnly -or
    $proofReport.oneDriveHydrationRequested -or
    $proofReport.jobsEnqueued -or
    $proofReport.videoWorkersActivated -or
    $proofReport.derivativesPublished -or
    $proofReport.realVideoExecutionAuthorized) {
    throw "WI-0210 source proof acceptance failed."
}
~~~

Expected, **only when source proof was explicitly authorized and
succeeded**: `status=verified`, `revisionDigestMatched=true`,
`sourceMetadataStable=true`, `originalBytesOpened=true`,
`oneDriveHydrationRequested=false`, `jobsEnqueued=false`,
`realVideoExecutionAuthorized=false`. A stopped-worker, photo
load, revision, digest, file status or policy rejection is an
appropriate fail-closed result; do not bypass it.

Share only the sanitized proof `status` and boolean fields,
never the selected private UUID or media path. If the operator
does not authorize original reads, stopping after dry-run is a
successful checkpoint-7 safety preview, but not a SHA verification.

## Not yet proven

A successful exact-revision SHA check does NOT confirm actual
FFprobe `color_range=pc`, Rec.709 colour metadata,
rotation, full-to-limited camera appearance, unchanged bytes
after a future conversion, or end-user proxy playback.

Those require an additional reviewed and explicitly authorized
real-video sample harness. Production worker, HDR,
poster and frame-evidence policies remain unchanged.
