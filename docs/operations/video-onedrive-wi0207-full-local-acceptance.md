# WI-0207 — Full local acceptance: opt-in OneDrive-only video processing and release

**Purpose:** Run **one** explicitly chosen, already-catalogued OneDrive
video through opt-in hydration, proxy creation, retained playback,
managed release and eventual online-only state. This is the **complete
work-item acceptance review**, not another synthetic checkpoint.

The new video-only path uses the registered Windows Cloud Files sync
provider to hydrate **without +p**. It uses **+u only** when handing
an app-owned, unpinned original back to OneDrive. Existing photo
hydration behavior is unchanged. No third-party inferencing,
external cloud API, new downloader or second original cache is added.

**User authorization is essential for the real step.** Do not run it
automatically, and do not change any existing user-pinned video
just to create an example. The initial, previously verified
preflight found 9 candidates with no proxies: 2 local and
7 pinned local; none online-only. **The pilot must use one
already-catalogued current video with supported inspection
metadata**. Never-inspected online-only videos are skipped
rather than being automatically pinned and downloaded just
to collect metadata. If your intended clip was never
inspected, first make it local and let the normal local
inspection run before the approved online-only pilot. Those counts may have
changed since. If no suitable online-only video is available,
the operator can select an appropriate **unpinned** catalogued
test clip and manually use Explorer **Free up space** after
checking it has no pending use. This is a deliberate operator
file-availability change, not a step the application performs.

## 1. Update main and verify the implementation (PowerShell 7.6)

Do this only after this PR has merged and required CI passed:

~~~powershell
$ErrorActionPreference = "Stop"
cd "C:\Kod\codex\Photo Identity Indexer"
git status --short
git switch main
if ($LASTEXITCODE -ne 0) { throw "Cannot switch main." }
git pull --ff-only origin main
if ($LASTEXITCODE -ne 0) { throw "Cannot update main." }

$filter = @(
    "FullyQualifiedName~VideoUnpinnedOneDrivePlatformTests"
    "FullyQualifiedName~VideoManagedHydrationAdmissionTests"
    "FullyQualifiedName~VideoManagedHydrationRecoveryTests"
    "FullyQualifiedName~VideoHydrationReleaseGuardTests"
    "FullyQualifiedName~VideoOnlineOnlyPreflightTests"
) -join "|"
dotnet test tests/PhotoIdentity.Integration.Tests/PhotoIdentity.Integration.Tests.csproj -c Release --filter $filter
if ($LASTEXITCODE -ne 0) { throw "WI-0207 integration tests failed." }
Write-Host "WI-0207 integration: PASS"

$testAdminAvailable = -not [string]::IsNullOrWhiteSpace(
    [Environment]::GetEnvironmentVariable(
        "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING", "Process"
    )
)
if ($testAdminAvailable) {
    dotnet test tests/PhotoIdentity.Persistence.Tests/PhotoIdentity.Persistence.Tests.csproj -c Release --filter "FullyQualifiedName~PostgresVideoManagedHydrationTests"
    if ($LASTEXITCODE -ne 0) { throw "Disposable PostgreSQL test failed." }
    Write-Host "WI-0207 disposable PostgreSQL: PASS"
}
if (-not $testAdminAvailable) {
    Write-Host "Disposable PostgreSQL: NOT EXECUTED"
}

dotnet run --project tools/PhotoIdentity.Docs -- validate
if ($LASTEXITCODE -ne 0) { throw "Documentation validation failed." }
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
if ($LASTEXITCODE -ne 0) { throw "Generated docs drift." }
Write-Host "WI-0207 documentation: PASS"
~~~

The tests and docs commands do not access production originals.
The disposable PostgreSQL test creates and removes an isolated
temporary database and must not use photoidentity_main.

## 2. Preflight the operator's normal launcher and database

Before any real Archive run, have a **verified and restorable
PostgreSQL backup**; existing production processing writes
catalogue job/derivative records. Use your existing backup process
and do not paste credentials in the review report.

Use the already verified Photo Identity launcher configuration,
approved FFmpeg/FFprobe binaries, trusted OneDrive sync client,
existing PostgreSQL server and the approved video derivative
cache. Do **not** change ffmpeg hashes or copy original video data.
Confirm the shared ArchiveHydration settings are configured:
minimum free-space reserve bytes, maximum managed-hydration
bytes, and maximum concurrent managed transfers. These have
no magic values: if your existing settings are absent the
admission refuses to hydrate. Keep headroom for an at-most-256-MiB
original plus the already configured private video workspace.

Rerun the *read-only* aggregate preflight using your existing
database connection-variable name and database name, e.g.
with the already verified standard names (adjust only if your
configuration differs):

~~~powershell
$connectionVariable = "PHOTOIDENTITY_POSTGRES_CONNECTION_STRING"
$databaseName = "photoidentity_main"

if ([string]::IsNullOrWhiteSpace(
    [Environment]::GetEnvironmentVariable($connectionVariable, "Process")
)) {
    throw "Existing PostgreSQL launcher connection not present in this shell."
}

$preflightRaw = dotnet run --project src/PhotoIdentity.Cli -c Release -- video onedrive preflight --postgres-connection-env $connectionVariable --database $databaseName
if ($LASTEXITCODE -ne 0) { throw "Read-only preflight refused." }
$preflight = ($preflightRaw | Out-String) | ConvertFrom-Json
$preflight.availability | Format-Table status, count -AutoSize
~~~

Do not share raw database connection strings. The preflight is
aggregate-only; it cannot itself identify a particular file.

## 3. Choose exactly one eligible private video

Use the existing Photo Identity video library and the intended
Archive included-folder selection to identify a **current,
supported, unexcluded, operator-owned** video (within the source
size/duration/profile limits) that has **no published playable
proxy**. Prefer a clip already online-only. Ensure the selected
included-folder scope does not expose additional online-only
candidates; the Archive is local-first and can otherwise process
multiple videos before reaching the chosen clip.

If the only candidate is **local but unpinned**, manually use
Explorer **Free up space** for that specific clip *only if you
explicitly authorize this availability change*. Do not do that
to any pinned original or to a clip with active work. Confirm
Explorer reports online-only before proceeding. Do not hash or
read the placeholder to inspect it before the authorized test:
that would implicitly hydrate it.

For a private attribute-only confirmation, enter the specific
path **locally**, without posting it back to the conversation:

~~~powershell
$pilotPath = Read-Host "Full path to the one approved test clip (keep private)"
if (-not [IO.File]::Exists($pilotPath)) {
    throw "Selected source placeholder is unavailable."
}

function Get-Wi0207PilotState {
    param([Parameter(Mandatory)][string]$Path)

    $a = [int][IO.File]::GetAttributes($Path)
    $pinned = ($a -band 0x00080000) -ne 0
    $unpinned = ($a -band 0x00100000) -ne 0
    $missing = (($a -band 0x00001000) -ne 0) -or
               (($a -band 0x00040000) -ne 0) -or
               (($a -band 0x00400000) -ne 0)
    $availability = if ($missing) {
        if ($pinned) { "downloading" } else { "online-only" }
    } else { "local" }

    [pscustomobject]@{
        Availability = $availability
        Pinned = $pinned
        Unpinned = $unpinned
    }
}

$before = Get-Wi0207PilotState -Path $pilotPath
$before | Format-List
if ($before.Availability -ne "online-only" -or $before.Pinned) {
    throw "Choose a single confirmed unpinned online-only video."
}
~~~

This checks only Windows file attributes and does not read bytes.
Keep the file path private. If no supported online-only candidate
can be prepared safely, stop and report "real pilot blocked:
no eligible online-only candidate" rather than forcing it.

## 4. Explicitly authorize the single-video processing run

In the running Photo Identity app launched with your existing
approved Windows configuration:

1. Confirm only the intended Archive included folder is selected;
   photo-first processing, already-local videos, and other
   eligible work take priority.
2. In **Settings → Video processing**, choose **Standard 720p** or
   **Compact 540p** (the output quality, not an input format). Enable
   normal video processing and independently enable **Include
   online-only videos** for this test. Rec.709 full-range input uses
   the approved source-specific conversion automatically under
   either quality. Optionally enable **Stop each Archive advance
   after one video step** for a small controlled trial; it is not
   required by the format. Do not change existing user pins.
3. Start the **operator-triggered Archive Run**. Verify only the
   chosen video gets downloaded by the OneDrive sync client.
   Inspect sanitized worker status; do not run bulk CLI jobs.
4. Observe proxy processing and optional poster. Confirm the
   generated current playable proxy is visible and playable.
   Verify desktop playback/seek and, if relevant, phone HTTPS
   playback. A retained proxy must remain playable after the
   original returns to online-only.
5. Stop Archive after the intended sample is processed. Turn
   **Include online-only videos OFF** again.

A normal run checks current revision, exclusion, source path,
supported inspection metadata, shared managed byte/free-space
limits, pinned state and queued/running work. A managed lease is
recorded **before** Cloud Files hydration. The worker's retained
original is only offered for release after it is no longer needed.
The pin bit remains untouched by the new hydration operation.

## 5. Check managed release and eventual online-only state

Inspect **only the chosen path** using the same attribute-only
helper; never use File.Delete or manually run attrib -p.

~~~powershell
$after = Get-Wi0207PilotState -Path $pilotPath
$after | Format-List
~~~

A managed +u release request is asynchronous. If the file is
still locally available, confirm it remains **unpinned**, then
start one further **operator-triggered** Archive run after the
source is no longer being processed. This invokes the bounded
recovery pass and can retry a previously requested release.
Stop it again after the intended cleanup; check the same state.

A successful full local acceptance should establish:
- Exactly one explicitly authorized online-only original was
  hydrated without a persistent app +p pin.
- Its bounded derivative processed/published, and any dependent
  queued/running work retained the original until completion.
- The original was not deleted or modified by the app.
- With no dependent work remaining, the original eventually
  returns to **online-only**, and the playable proxy continues
  working without rehydration.
- Existing previously local and pinned/user-owned originals
  were unaffected. If the user chose to pin the sample while
  running, release must refuse and preserve it.

If OneDrive does not free local bytes, report whether it remained
local/pinned, whether the app reported a release request, or if
the provider reported an error. Do not claim success from a
logged request alone. A failed +u leaves its managed receipt
pending for a later operator-run retry; it never performs an
ordinary filesystem deletion.

## 6. Scope decisions and known limitations

This PR intentionally uses the smallest implementation necessary
for a full local pilot rather than a new background scheduler:

- **New video hydration uses Windows CfHydratePlaceholder rather
  than +p.** This is Windows-only and serviced by the existing
  OneDrive sync provider. A synchronous native operation can
  continue briefly after operator cancellation, even if the
  worker's five-minute wait is cancelled; the database receipt
  persists. If the Cloud Files API is unsupported on the test
  machine, it fails closed rather than silently falling back to
  permanent pinning. Report that as a focused pilot defect.
- **Release uses +u only after checking the current unpinned state**
  and committing a source-locked, no-pending-jobs release request.
  The underlying Windows pin attributes do not offer an atomic
  "release only if no user pin was set in the same instant" command;
  a very narrow check-to-attribute race remains. The app never
  explicitly issues -p for videos. Do not use a concurrently
  user-pinned pilot file.
- **No background cleanup daemon.** The worker attempts release
  after eligible processing, and later operator-started Archive
  advances scan a bounded set of leftover receipts. If Archive
  is never run again after an interrupted release, local bytes
  may remain temporarily until the next operator action.
- **Historical app-pinned video receipts are not automatically
  unpinned.** We cannot distinguish that old app +p pin from a
  user pin, so those are left local. The reviewed single-video
  pilot must use the new no-pin path.
- The shared photo/video storage cap and supported-video source
  size/profile restrictions remain in force. One sample is
  sufficient for WI-0207's mandatory live acceptance; this is
  not a comprehensive concurrency/OneDrive-outage stress test.

## 7. Report only the following

Do not include original filenames, paths, database credentials
or private frame imagery. Please report:

~~~text
WI-0207 full local review
Focused integration tests: PASS / FAIL (number)
Disposable PostgreSQL test: PASS / FAIL / NOT EXECUTED
Documentation: PASS / FAIL
Current, supported one-video pilot selected: YES / NO
Original before: online-only, unpinned? YES / NO
Explicit opt-in and controlled Archive Run: PASS / FAIL
Proxy published and plays after release: PASS / FAIL
Original after: online-only / local / pinned / downloading / unavailable
Queued/running dependent jobs absent before release: YES / NO / UNKNOWN
Existing user-pinned originals unchanged: YES / NO
Any sanitized error/reason code:
Known limitation encountered:
~~~

Do not mark WI-0207 completed until the maintainer has
reported one approved real example. If the real pilot finds a
concrete failure, fix that *specific* failure rather than
reopening a broad redesign.

## 8. Follow-up acceptance: first inspection of a never-inspected online-only video (PR #761)

**The preceding Step 1–7 runbook describes the previously-inspected
path that the maintainer already accepted on 2026-11-08. Do not
repeat that live test.** The new code in draft PR #761 adds the
missing provisional discovery path; no new real-media authorization
is implied by the PR or this runbook.

Only after PR #761 merges and all CI/disposable PostgreSQL/docs tests
pass, perform **one** additional controlled Windows verification:

1. Update `main` using the Step 1 commands; confirm your existing
   approved FFmpeg/FFprobe, launcher, storage policy and production
   backup remain valid. No new FFmpeg pinning or backup-retention work.
2. Choose **one never-inspected** current unexcluded MOV/MP4 original,
   **online-only and unpinned** from the selected included-folder
   scope, with a FileInfo size between 1 byte and 256 MiB. Keep the
   source path private; read its Windows file attributes with the
   `Get-Wi0207PilotState` helper from Step 3. Do **not** use Explorer
   `Always keep on this device`, `Free up space`, copy, SHA or
   FFprobe manually; that would bypass the feature under test.
3. Ensure no other local video or pending derivative job in the
   selected coverage will take priority. Set video processing ON,
   **Include online-only videos ON**, Standard 720p or Compact 540p,
   automatic poster OFF (unless independently desired), and optional
   **Stop each Archive advance after one video step ON** for control.
   Save. Record aggregate status and set aside source filenames/IDs.
4. Trigger **Advance archive once**, observing that the single
   placeholder is registered provisionally, hydrated without +p,
   hash-verified/FFprobe-inspected, and either queued for a supported
   proxy or durably classified unsupported. The worker may complete
   the first inspection and proxy in the same Archive cycle. A
   bounded second explicit Advance is acceptable for a separately
   queued step, but never run an unrestricted backlog for the test.
5. For a supported video, verify the published proxy on desktop:
   play, pause, seek, colour, orientation, and audio where relevant.
   Confirm no unrelated original hydrated. For unsupported input,
   expect no proxy, an unsupported inspection outcome, no automatic
   repeated download and a managed release. This is a negative-path
   observation, **not** a substitute for the successful supported
   first-time proxy test if such a source is available.
6. Pause Archive, turn **Include online-only videos OFF**, save,
   and confirm that the inspected source returns to **online-only,
   unpinned** once no dependent work needs it. A recovery Archive
   advance can retry an interrupted +u; the source must not be
   manually unpinned or deleted to manufacture a pass. With the
   source still online-only, replay the proxy; verify it causes no
   hydration. Check existing user-local and pinned originals
   remain unchanged.
7. In video Settings distinguish **Latest Archive video step**
   (`no-eligible-video` can be legitimate after an idle cycle)
   from **Last substantive video outcome** (`published`,
   `video-inspected`, `video-inspected-unsupported` or a failure).
   The latter is an in-memory value since application startup.
   `not-run` for automatic posters indicates no attempt since start.

Local repeatable focused checks after merge, run in PowerShell 7.6:

~~~powershell
$ErrorActionPreference = "Stop"
cd "C:\Kod\codex\Photo Identity Indexer"
git switch main
git pull --ff-only origin main

$filter = @(
    "FullyQualifiedName~VideoFirstInspectionAdmissionTests"
    "FullyQualifiedName~VideoManagedHydrationAdmissionTests"
    "FullyQualifiedName~VideoUnpinnedOneDrivePlatformTests"
    "FullyQualifiedName~VideoManagedHydrationRecoveryTests"
    "FullyQualifiedName~VideoHydrationReleaseGuardTests"
) -join "|"
dotnet test tests/PhotoIdentity.Integration.Tests/PhotoIdentity.Integration.Tests.csproj -c Release --filter $filter
if ($LASTEXITCODE -ne 0) { throw "First-inspection integration tests failed." }

# Run ONLY when the existing disposable-test admin variable is set.
if (-not [string]::IsNullOrWhiteSpace($env:PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING)) {
    dotnet test tests/PhotoIdentity.Persistence.Tests/PhotoIdentity.Persistence.Tests.csproj -c Release --filter "FullyQualifiedName~PostgresVideoManagedHydrationTests"
    if ($LASTEXITCODE -ne 0) { throw "Disposable PostgreSQL tests failed." }
}
dotnet run --project tools/PhotoIdentity.Docs -- validate
if ($LASTEXITCODE -ne 0) { throw "Documentation validation failed." }
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
if ($LASTEXITCODE -ne 0) { throw "Generated docs drift." }
~~~

Report no private filenames, directory paths, IDs, frame content or
connection strings. Record aggregate before/after status, one new
supported inspection, one new proxy or explicit unsupported result,
initial and final original states, pin changes, unchanged other
sources, and whether proxy replay rehydrated anything. If CI or the
new live test reveals a focused defect, keep WI-0207 `in_progress`
and patch that issue without repeating the previously accepted
hydration/release acceptance.
