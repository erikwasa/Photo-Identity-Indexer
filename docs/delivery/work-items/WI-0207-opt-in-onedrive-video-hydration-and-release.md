---
id: WI-0207
title: Verify opt-in OneDrive-only video hydration and safe online-only release
milestone: M30
status_source: PhotoIdentity.Docs
depends_on: [WI-0188]
related_adrs: [ADR-0012, ADR-0007]
affected_modules: [PhotoIdentity.Api, PhotoIdentity.Source.OneDriveSync, PhotoIdentity.Persistence.Postgres, PhotoIdentity.Integration.Tests, docs]
---

# WI-0207: Verify opt-in OneDrive-only video hydration and safe online-only release

## Objective and revised scope — maintainer 2026-11-02

Reuse the **existing photo-style managed OneDrive hydration strategy**
for opt-in video processing. Videos that were online-only may be
materialized when a specific authorized processing job needs them,
and **kept locally for as long as the application's pending and
active processing requires them**, across applicable dependent
derivatives or safe retries. Once no processing lease or planned
near-term job needs the bytes, the normal bounded managed-cache
policy may release eligible app-hydrated files back to OneDrive
online-only. No automatic recall for mere browsing or search.

This replaces a needlessly strict expectation that every video must
be released immediately following one isolated processing step.
The existing implementation already has default-off online-only
controls and guarded staging; this workitem **first verifies and
reuses** them, and changes code only for observed gaps.

## Background

WI-0188 was completed for already-local video processing on
2026-10-30. Its real media test did not hydrate an online-only
OneDrive file or verify managed release. Those behaviors remain
unverified, not failed. WI-0207 is independent of accepted
local-file processing and does not reopen previous test gates.

## Essential contract

- Keep **Include online-only videos** independently optional and
  off by default. Turning on general video processing must not
  silently grant online-only hydration.
- Hydrate **one authorized current revision on demand**, with
  existing source/exclusion, capacity and availability checks.
  Reuse the OneDrive sync client and existing managed hydration
  ownership tracking; no new downloader, cloud API or second cache.
- **Keep an application-hydrated original local while processing
  still requires it**, including active stages and already-planned
  adjacent jobs or bounded retries. Do not release a file merely
  because a proxy was published while another eligible approved
  derivative still needs the input. Do not retain it forever when
  processing has concluded and it is managed-cache-eligible.
- Only original files **hydrated by Photo Identity** may be
  automatically released. Pre-existing local files or user-pinned
  files remain local. Failed release must leave bytes intact and
  be visible; do not attempt ordinary filesystem deletion of an
  authoritative OneDrive original.
- Respect current revision, source-copy exclusion and job
  cancellation/lease guards before using bytes or publishing a
  derivative. Do not allow infinite automatic transfer retries.
- **Playback and browsing**: an already-retained, current,
  authorized playable proxy can be served by WI-0152 even while
  the original is online-only. Play/seek/search never causes
  hydration. If the proxy is unavailable, the viewer shows a
  clear state; a future explicit preparation action can reuse
  this existing controlled hydration/processing pathway.

## Three levels of acceptance

1. **One real OneDrive example (mandatory):** With maintainer
   approval, exercise one known online-only catalogue video using
   the explicit setting. Verify it hydrates, is processed safely,
   remains local while needed, and follows the existing owned-cache
   release policy only after work is finished. Report private
   before/after availability and basic results without filenames.
2. **Core safety (mandatory):** Default-off does not hydrate;
   an already-local or user-pinned original is never released by
   the app; exclusions/current revision and unchanged originals
   hold; failed release preserves the local file.
3. **Exceptional failures (targeted simulation):** Use existing
   tests and add only focused checks for any uncovered cancellation,
   restart, stale-revision or cloud-error guard. A battery of real
   destructive disconnects or cloud outages is **not required**
   without a demonstrated problem.

## Acceptance criteria (revised)

- [ ] Default-off online-only opt-in remains independent of the
  normal video switch; local-video processing still works.
- [ ] One specifically authorized online-only video demonstrates
  bounded hydration, safe processing and local reuse while needed;
  its original remains unchanged.
- [ ] The release policy recognizes app-owned hydration, active/
  pending use and user/pre-existing pinning; release happens only
  when eligible and preserves bytes on failure.
- [ ] Focused existing/disposable tests cover essential
  exclusion/revision/cancel/retry safety; no mandatory large failure
  simulation campaign.
- [ ] Maintainer confirms the real example and its bounded
  privacy/storage behavior; record any missing capability as a
  specifically scoped fix, not a general overhaul.

## Out of scope

- Auto-hydrating placeholders by browsing, search or opening a proxy.
- Bulk hydration, a second OneDrive connector, arbitrary cloud URLs,
  whole-archive staging, forced local-file deletion or changes to
  user pinning preferences.
- Video format expansion (WI-0210), browser playback (WI-0152),
  face recognition, broad performance/transfer-cost benchmarking.
- Re-running WI-0188's already accepted test campaign.

## Verification and safety

Start with inspection of current local settings and managed-hydration
behaviour. The maintained-host test uses a single explicitly approved,
already-catalogued online-only video, with an authoritative database
backup when the acceptance operation writes to the catalogue. Do not
download unrelated placeholders or alter original source bytes.
A file that remains local after an attempted OneDrive release is
**not** proof of failed processing; record release success/failure
separately from durable derivative publication.

The **reuse-until-no-longer-needed** policy is a desired scope/contract
for WI-0207; documentation does not assert that the current worker
already implements every cross-job reuse case. Confirm the current
implementation before adding coordination.

References: [ADR-0007](../../decisions/ADR-0007-permanent-archive-bounded-storage.md),
[WI-0188](WI-0188-video-isolated-processing-lane.md),
[WI-0152](WI-0152-video-serving-playback.md) and
[shared video runbook](../../operations/video-processing-shared-archive-verification.md).

## Current status — 2026-11-05

**In progress — checkpoint 1 read-only audit proposed for CI/local
verification.** WI-0211 is now completed and archived. WI-0207 is
the next selected, unblocked M30 work item, ahead of the intentionally
deferred WI-0153 metadata/collections work.

This checkpoint adds a no-media, attribute-only Windows CLI
preflight at video onedrive preflight. It selects at most 64 current,
supported, nonexcluded video revisions lacking a published playable
proxy; checks a repeated-read-only PostgreSQL snapshot and current
OneDrive Files On-Demand attributes; and outputs only aggregate
standard/full-range, online-only/local/pinned/error categories,
queued/running derivative counts and existing app-managed hydration
receipt counts. It never prints IDs, private paths or filenames,
opens source bytes, migrates schema, runs FFprobe/FFmpeg, hydrates,
pins/unpins or enqueues jobs. Results do NOT independently approve
a source format or transfer. See the
[Windows read-only preflight runbook](../../operations/video-onedrive-wi0207-readonly-preflight.md).

**Observed safety gap requiring a later implementation checkpoint:**
the existing video worker currently releases a just-hydrated video
original inside its per-attempt finally block without first checking
whether already planned dependent jobs still need the original or
whether the user has pinned the file. It also does not yet
demonstrate durable shared managed hydration ownership with the
photo capacity/release policy. No production online-only test has
been run. Until this contract is addressed, do not enable
online-only video work merely to complete this audit.

**Next:** passing PR CI and one maintainer-executed read-only
aggregate preflight; then a bounded, reviewed shared-ownership
and dependent-job-aware release change, with focused fake/cloud
state tests before separately authorized single-real-video
acceptance. The work item remains open.

## Maintained Windows verification — checkpoint 1 accepted (2026-11-05)

After PR #747 merged, the maintainer ran the focused
`VideoOnlineOnlyPreflightTests`: **16 passed, 0 failed**; build
succeeded. The newly added read-only OneDrive preflight returned
**exit code 0** with `readOnly=true`, and reported **no original
content opened, hydration requested or release requested**.

The sanitized live aggregate covered **9** supported current
video candidates without published playable proxies: **2 local**
and **7 pinned-local**. **0 online-only** candidates, **0
queued/running dependent jobs**, and **0 active managed-hydration
receipts** were observed. These are metadata/attribute-only
point-in-time classifications, not verified original bytes
or authorization to change pinning. As there was no online-only
candidate in the audited cohort, **no real download or release
acceptance has occurred**. This checkpoint is accepted and
does not need to be repeated solely to proceed.

## Development checkpoint 2 — conservative release guard (2026-11-05)

The existing video's per-attempt `finally` had called
`RequestOnlineOnlyAsync` whenever that attempt had initiated
OneDrive hydration. New `VideoHydrationReleaseGuard` logic
now checks, using fresh evidence before considering any release:

- The request was initiated by Photo Identity for that video,
  not a pre-existing locally available original.
- The revision is still current, active, unexcluded and
  belongs to the approved video catalogue.
- No queued/running dependent video jobs remain for that
  revision and photo-first processing is not active.
- Windows currently reports the original fully local
  **and unpinned**. Downloading, unavailable/error and
  any pinned state fail closed.
- Failed database reads or status checks preserve the
  original; successful playable proxy output is not
  undone to force cleanup.

A OneDrive Files On-Demand `+p` pin is shared by both
application-initiated hydration and an explicit user
"Always keep on this device" choice. The current API
cannot conclusively distinguish them after hydration.
**Consequently, a pinned video remains local** under the
conservative guard even when the app originally requested
hydration. This is a safety-oriented intermediate change,
not proof of complete storage reclamation. It avoids
unpinning user-pinned originals, at the cost of possibly
retaining app-requested video bytes. Existing photo
managed-hydration ownership/capacity policy remains to
be safely integrated and proved for video, together with
crash/restart recovery and eventually eligible release
without overriding user choices. The separate
`Include online-only videos` setting must remain OFF
during this checkpoint.

Tests use only synthetic pin, status, exclusion/currentness
and dependent/photo-work combinations; **no original
OneDrive video transfer was exercised**. See
[the follow-up Windows verification guide](../../operations/video-onedrive-wi0207-release-guard.md).
**Maintained-host result:** PR #749 merged and the maintainer ran
the combined checkpoint 1+2 integration filter: **27 passed,
0 failed, 0 skipped**, build succeeded in **26.3 seconds**.
This accepts the conservative release-guard checkpoint. No
original video or OneDrive transfer was needed. WI-0207
remains **in progress** for durable ownership and actual
safe release.

## Development checkpoint 3 — shared managed-hydration admission (2026-11-05)

The next implementation uses the **existing photo managed-hydration
capacity service and PostgreSQL ownership receipts** for authorized
online-only video source requests. It does not add a new downloader
or cache. The existing operator-controlled Archive run and
default-off Include online-only videos setting remain prerequisites.

The new `VideoManagedHydrationCoordinator` re-reads the precise
current source revision under catalogue/exclusion fences, checks
that the registered path is a contained local descendant without
linked ancestors, refuses an already active source-level lease,
active release or resumed photo work, then admits the request
through `ArchiveHydrationCapacityService`. The same configured
free-space reserve, maximum managed bytes and concurrency apply
to photo and video originals. The coordinator persists a revision
ownership receipt **before** the OneDrive +p hydration request.
A failed request therefore retains a durable conservative
record instead of leaving an untracked app-pinned original.
A later video processing attempt touches ownership as needed.

The shared capacity service now distinguishes video leases using
the **catalogued revision media kind**. Video leases contribute to
the same managed byte and transfer budget, but **are not selected
for automatic photo-cache LRU release**, even when local. Their
+p pin bit cannot currently distinguish an app pin from a
subsequent user "Always keep on this device" pin. The separate
video cleanup guard from checkpoint 2 still checks current
revision, exclusions, dependent jobs, photo priority and
live pin/availability state before release. Any permitted
release request is tracked durably and reconciled by the
existing managed hydration service. This is intentionally
fail-closed rather than silently overriding user pin choices.

**Scope limit:** Shared admission and durable ownership are
implemented here, but **reliable eligible release of the
app-hydrated, pinned video file remains unresolved**. This is
not permission to flip the online-only switch in production.
There is no fully validated distinction between user-held
and app-held pins, no final video-dependent lease coordination
across crashes, and no real online-only video transfer
acceptance yet. A disposable PostgreSQL test covers typed
persistent video/photo leases and their receipt lifecycle;
synthetic contracts assert the shared-budget and no-auto-unpin
fences. The read-only local baseline is already accepted
and need not be repeated.

See the
[checkpoint 3 Windows verification runbook](../../operations/video-onedrive-wi0207-shared-admission.md).
WI-0207 stays **in progress** until the final safeguards
and separately authorized real-media pilot are accepted.

## Maintained Windows acceptance — checkpoint 3 (2026-11-06)

**Checkpoint 3 accepted; WI-0207 remains in progress.** After PR
[#750](https://github.com/erikwasa/Photo-Identity-Indexer/pull/750)
merged, the maintainer ran the combined
`VideoManagedHydrationAdmissionTests`,
`VideoHydrationReleaseGuardTests` and
`VideoOnlineOnlyPreflightTests` filter against the maintained
Windows checkout: **30 succeeded, 0 failed, 0 skipped** (3.1 s
test duration; 34.0 s total build). The previously configured
disposable PostgreSQL administrator variable was **present**;
the real `PostgresVideoManagedHydrationTests` filter **executed
one test and passed** (0 failed or skipped; 5.5 s build). This
confirms persisted video/photo ownership-record type and lifecycle
in a temporary database, rather than merely a test that silently
returns because its test credential is absent.

`PhotoIdentity.Docs validate` returned "Documentation registries
and links are valid"; `generate --check` passed. No production
PostgreSQL database, original-video bytes, OneDrive hydration/release
or Archive advance were used.

Two PowerShell output quirks do **not** change the acceptance:
the `else` clause was run later as a separate command after the
successful `if` and produced a parser/command error, and the
final summary printed `IntegrationTests: NOT VERIFIED` because
the separate reporting variable `$integrationPassed` had
not been assigned in that shell. The authoritative test runner
output explicitly confirms all 30 integration tests passed.
No repeat test is required simply to correct a display variable.

**Still open:** The indistinguishable app/user OneDrive `+p`
pin prevents safe automatic release of pinned video originals.
Cross-job/crash recovery and the one explicitly authorized
real online-only hydrate/process/reuse/release example still
require separate implementation and verification.
`Include online-only videos` stays OFF until those gates are
met; do not manufacture an online-only sample for this checkpoint.

## Documentation closeout verified — checkpoint 3 (2026-11-06)

After documentation PR
[#752](https://github.com/erikwasa/Photo-Identity-Indexer/pull/752)
merged, the maintainer ran the maintained PowerShell verification.
The documentation validator returned **"Documentation registries and
links are valid."**, the generated documentation
check returned exit code 0, and **all five** explicit canonical
WI-0207 acceptance-record checks returned **True**:
work item exists, remains in progress, PR #750 is recorded, and
the 30/30 integration and 1/1 disposable-PostgreSQL test outcomes
are recorded. No media or database tests need repeating to
close checkpoint 3's documentation gate.

## Development checkpoint 4 — resume and reconcile durable video receipts (2026-11-06)

The existing managed-hydration receipt survives process shutdown,
but a previously app-hydrated original that appeared local on the
next selected Archive video step was previously handled as though
it were just another ordinary local original. Checkpoint 4 adds
an explicit **resume-current-managed-local** path:

- For a local selected video, read an existing revision ownership
  receipt; **do not claim anything** if no active receipt exists.
  This is how pre-existing local/pinned user originals remain
  independent of the managed cache.
- Revalidate the exact current, unexcluded video revision, source
  identity and path; reject a conflicting source-managed receipt,
  an outstanding release request or a newly nonlocal state.
  Reassert and touch durable usage only while processing is
  authorized. Subsequent cleanup still protects user/app pins
  and queued/running derivative dependents.
- At each already operator-authorized video advance (after the
  photo-first gate), a bounded recovery pass reads up to
  **16 previously release-requested video receipts**, selected
  with a database-side PostgreSQL LIMIT so normal photo hydration
  records are never loaded for this reconciliation.
  Only a file **already** reported online-only and unpinned
  can have its old release receipt closed by an **atomic
    PostgreSQL update requiring the same video receipt still to be
    active and release-requested**. A concurrently superseded or
    already-closed receipt is not counted as newly closed. All other cases
  leave ownership intact; the pass does **not** call OneDrive
  pin, unpin, hydrate, release or any original-byte API.
- Fixed aggregate counts and reason codes omit all private
  paths, IDs and platform errors. Cancellation propagates;
  unexpected metadata or database receipt-update failures
  preserve the record instead of misreporting it as released.
- New fake-platform integration tests cover unowned versus
  active managed local originals, pending release, local/pinned/
  downloading/offline/error states, path escapes, the 16-receipt
  cap, and a failed record update. The existing disposable
  PostgreSQL test now checks the new bounded pending-video
  receipt query before, during and after a release request.

Read the
[checkpoint 4 Windows verification guide](../../operations/video-onedrive-wi0207-restart-recovery.md).
**Checkpoint 4 CI and maintained Windows verification are pending.**
The accepted earlier 30 integration tests and one disposable
PostgreSQL test remain historical checkpoint-3 acceptance.

**WI-0207 is still in progress:** This is release-*receipt*
reconciliation, **not** authorization to release app-pinned video
original bytes. Because the OneDrive +p attribute does not prove
whether the user subsequently pinned a file, physical release of
pinned video originals is still prohibited. Race-free coordinated
release admission relative to new dependent jobs, safe eventual
storage reclamation and one separately approved real online-only
video test remain open. Keep the online-only video switch OFF.

## Maintained Windows acceptance — checkpoint 4 (2026-11-07)

**Checkpoint 4 is locally accepted; WI-0207 remains in progress.**
After PR [#753](https://github.com/erikwasa/Photo-Identity-Indexer/pull/753)
merged and the final-head GitHub Actions run #3625 passed, the
maintainer ran the targeted
`VideoManagedHydrationRecoveryTests`,
`VideoManagedHydrationAdmissionTests`, and
`VideoHydrationReleaseGuardTests` filter on the maintained
Windows PowerShell 7.6 checkout. The runner reported **35
succeeded, 0 failed, 0 skipped**, a 3.3-second test duration
and successful 37.1-second build.

The existing disposable PostgreSQL administrator connection
was available; `PostgresVideoManagedHydrationTests` **actually
executed 1 test and passed**, 0 failures, with a successful
6.0-second build. The test uses a temporary database, not
`photoidentity_main`. `PhotoIdentity.Docs validate` reported
"Documentation registries and links are valid"; the generated
documentation check also succeeded.

PowerShell later received a separate standalone `else` statement
after the successful PostgreSQL `if` block and rejected it
because the original conditional had already completed. This
was a shell presentation error, **not** a test failure;
nothing needs rerunning to correct that output.

**Verified scope:** synthetic tests for existing app-managed
original reuse across retries/restarts, unowned local-source
protection, bounded pending-release-receipt reconciliation,
safe-path rules, atomic release confirmation and error handling,
plus disposable PostgreSQL receipt persistence. No real
OneDrive hydrate/unpin/release or original-video processing
was performed or authorized.

**Next WI-0207 development gates:** prove race-free coordination
between release admission and new queued/running dependent jobs;
design an app/user pin policy that can safely free eligible
managed bytes without clearing a user pin; then separately
authorize a single real online-only video hydrate/process/reuse/
release trial. Keep **Include online-only videos OFF**.

## Development checkpoint 5 — single-PR full local review candidate (2026-11-07)

The maintainer requested completing the remaining WI-0207 implementation
and moving to one full local acceptance review in as few PRs as
possible, **ideally the next PR**. This checkpoint changes the
new-video hydration strategy, preserving all previous accepted
checkpoint 1–4 evidence and retaining `status: in_progress`
until a real operator-approved OneDrive example succeeds.

### New video-only transient hydration and release

The previous app video hydration used OneDrive `attrib +p`,
making the subsequent pin indistinguishable from a user's
"Always keep on this device" choice. This prevented safe
automatic release. Newly authorized video hydration now uses
Windows **`CfHydratePlaceholder`**, which asks the *existing*
OneDrive Windows sync provider to materialize the bounded
original without setting its pin bit. It makes no direct cloud
API call and needs no separate copy or downloader. Photo
hydration remains unchanged.

After the selected video proxy job and optional downstream
work finish, the worker checks live Files On-Demand attributes,
existing managed ownership, current revision and exclusions,
photo priority, and queued/running derivative dependencies.
It requests an **idle video release under the same PostgreSQL
source row lock used by enqueue/restart**. Future enqueues
and explicit failed-job restarts must refuse a managed video
that has an outstanding release. Only an unpinned, app-owned,
locally present video can have `attrib +u` requested.
**Video release never invokes `attrib -p`**.

The user can inspect playback through the current registered
proxy while the original is online-only; the new hydration path
is reached only by an authorized Archive worker with the
independent online-only opt-in enabled.

### Complete bounded cleanup without a background worker

The worker requests release at the end of eligible processing
and runs a bounded idle-ownership sweep if no new video
candidate is ready. Existing checkpoint-4 recovery of at most
16 release-requested receipts is extended: if the file is
already online-only, it is reconciled atomically; if a prior
release was requested but the file remains **local and
unpinned**, and revision/job eligibility still holds, the
operator-started recovery can retry `+u` without `-p`.
A failed release leaves its durable ownership record active,
never deletes authoritative original bytes, and can be
retried on a later operator Archive run.

All automated checks use synthetic OneDrive data or a
**disposable PostgreSQL database**. The new SQL test covers
queued derivative work blocking release, transition to
eligibility after a terminal job, idempotent release
admission and pending receipt confirmation. Platform
contract tests verify that new video hydration uses no
`+p` and new video release uses no `-p`.
The earlier photo-hydration behavior is unaffected.

### Deliberate simplifications and consequences

1. **Windows Cloud Files API only:** The chosen machine must
   support `CfHydratePlaceholder`. We fail closed if it does
   not; there is no automatic fallback to permanent pinning.
   A synchronous native hydration may outlive cancellation
   of the worker's bounded wait. Durable ownership keeps it
   accounted for until a later Archive action.
2. **No separate cleanup scheduler:** Release is requested on
   job completion or a subsequent operator-initiated Archive
   advance. After an interrupted run, managed bytes can
   remain local until the next authorized operation.
3. **Tiny pin-state race:** A last-moment user pin between
   observing unpinned metadata and the provider receiving
   `+u` cannot be made fully atomic using the existing
   attribute interface. The app never clears an explicit
   pin itself. The single controlled test must avoid
   concurrently pinning that clip.
4. **Historical pinned receipts remain local:** Old video
   `+p` hydration cannot be distinguished from user
   choice and is never automatically unpinned. The live
   pilot uses a newly authorized, initially unpinned
   online-only video.
5. **Never-inspected online-only sources are not recalled
   merely for metadata registration.** The historical video
   discovery code used `+p` before an immutable revision
   receipt existed and `-p` in cleanup. It is now strictly
   local-only, preventing unsafe user-pin changes. An
   online-only video must already have a current, supported
   inspection record to enter the opt-in video hydration
   worker. To process a never-inspected video, the operator
   must first make it local and allow normal inspection;
   this is deliberately outside the single-video pilot.

These are intentionally scoped shortcuts for a single
maintainer-verified work-item completion, not a claim that
every possible OneDrive driver/outage/pin race has been tested.

**The full local review is now the next acceptance gate**, not
another indefinite synthetic checkpoint. The maintained
operator must select **one** current supported unexcluded
video (at most 256 MiB) with no registered playable
proxy, confirm its initial online-only/unpinned state,
authorize controlled Archive processing, verify publication
and playback, and observe the original's eventual return
to online-only after safe job completion. The test is
**not automatically authorized by merging this PR**;
it explicitly changes local OneDrive availability. Use the
[full Windows local acceptance runbook](../../operations/video-onedrive-wi0207-full-local-acceptance.md).

**No real OneDrive transfer, release or live completion
evidence has been run for this checkpoint.** WI-0207 must
remain `in_progress` until CI and the maintainer's
one-video verification pass. If a concrete pilot issue
appears, fix that issue rather than expand the project
into a new general hydration subsystem.

## WI-0207 follow-up: input support independent of output quality (2026-11-07)

**Agreed requirement:** Video settings offer **Standard 720p** and
**Compact 540p** only. These select the output dimensions, bitrate and
durable conversion recipe—not which input media classes Photo Identity
accepts. Any newly *approved* playable-proxy input class must
automatically be considered under both output qualities, not require
another operator-selectable source-format preset.

**Current approved classes:** BT.709 SDR `yuv420p`/`yuv420p10le`
and validated full-range Rec.709 `yuvj420p`, in the existing
codec, container, rotation, geometry, duration and source-size limits.
Full-range originals still require an exact staged input FFprobe
colour-range/BT.709 proof and independent limited-range output checks.
HLG/PQ, unknown transfer/rotation and otherwise unapproved formats
remain excluded. Input eligibility does not imply that every
metadata-matching source is independently validated.

**Run-control correction:** The initial WI-0210 full-range 720p
rollout required local-only, one-video-step Archive operation.
Remove those format-specific constraints. **Include online-only
videos** stays separately default-off and explicitly authorized;
video workers stay sequential, photo-first and quota bounded.
The one-video-step mode remains an optional test throttle for
both quality choices. An online-only video still requires a
*current already-inspected* supported revision before the worker
can authorize hydration; unknown online-only originals are not
hydrated merely to inspect metadata.

**Planned and implemented in one PR candidate:**
1. Migrate the old saved `rec709-fullrange-720p` quality choice to
   Standard 720p on preference load without clearing the processing
   switch, online-only consent, or one-step setting. New settings
   writes accept only Standard/Compact.
2. Reuse unchanged SDR 720p/540p durable versions and the existing
   WI-0210 full-range 720p job version. Add a distinct **540p
   full-range conversion** version. Do not silently rewrite existing
   published proxies or reuse a changed recipe under an existing
   source-class/version pair.
3. Both pending and queued Archive selectors match the full
   supported input set independent of output quality; the actual
   approved conversion is chosen by the source class under the
   selected quality. Continue staged-source proof, source SHA,
   output signalling checks, approved FFmpeg/FFprobe, lease/revision
   and exclusion fencing, photo priority, privacy metadata stripping
   and managed OneDrive ownership/release.
4. Update settings text, compatibility matrix, full local runbook
   and focused synthetic tests for both quality/source combinations,
   persistence migration and independent run controls.

**Required acceptance:** GitHub CI/build/docs checks plus one
operator-approved WI-0207 online-only current, inspected video
hydrate → proxy → playback → safe eventual release test on the
maintained Windows PC; optionally validate the new 540p full-range
conversion using one deliberately chosen real source. Unit and
disposable-database tests cannot claim a real OneDrive/colour
acceptance. Keep WI-0207 **in progress** until that test passes.

**If one PR is insufficient:** Limit a follow-up PR to a concrete
failed build, source validation, queue selection, encoding or
managed-release defect. Record the failure and its scope in this
section, rerun only targeted tests and the affected acceptance step,
and do not reopen earlier accepted WI-0207 checkpoints.

## Maintained Windows real acceptance — previously inspected source (2026-11-08)

**Core WI-0207 opt-in hydrate/process/release path: PASS on the maintained host.**
After PR #759 merged, the maintainer confirmed: updated main and approved
launcher/FFmpeg/FFprobe, restorable backup, focused integration tests,
disposable PostgreSQL test, documentation validation and read-only OneDrive
preflight all passed. One already inspected, supported **SDR** original was
initially **online-only and unpinned**. With separate online-only opt-in,
one controlled Archive step hydrated only that source, published a **Standard
720p** playable proxy and successfully released the original back to
**online-only, unpinned**. Desktop play/pause/seek, colour, orientation and
audio passed; proxy playback after release did not rehydrate the original;
existing pinned originals were unchanged. Video job totals:
succeeded **31 to 32**, queued **0**, running **0**, failed **0**.
Dependent jobs were not directly observed before the release, but the durable
release fence and zero post-run queued/running jobs provide supporting evidence.
This acceptance does **not** establish first-time inspection of an online-only
placeholder or 540p/full-range real-video acceptance.

**Usability/functional gap discovered immediately afterwards:** In one selected
folder, five MOV/MP4 originals were all online-only (four within the 256 MiB
worker limit); Archive reported `no-eligible-video` although online-only was
enabled. A database-wide metadata preflight returned nine supported candidates
(two local, seven pinned-local), **zero online-only**. The existing discovery
loop deliberately restricts *uninspected* video metadata registration to
already-local originals, while managed hydration requires a verified revision.
This leaves never-inspected online-only videos unprocessable without manual
Explorer materialization and a separate Archive inspection. The previous
local-only restriction was a defensible release-safety shortcut, but it is
not an adequate normal operator workflow.

**New acceptance criterion (follow-up within WI-0207):** An operator-started,
included-folder-scoped, opt-in Archive step must discover a previously
uninspected, currently online-only unpinned MOV/MP4 placeholder using metadata
only; acquire durable *source-level* ownership and the existing shared
storage/concurrency budget *before* any unpinned Cloud Files hydration;
independently probe/hash/register the resulting local revision; choose the
approved Standard/Compact source-specific converter if supported; retain local
bytes through dependent processing; and eventually release app-owned originals
back to online-only without changing user pins. Unsupported, oversized, changed,
excluded, cancelled and interrupted sources must fail closed and not create
unbounded repeated downloads. Persist enough progress to recover after a
process restart. Keep photo-first, one-worker, current-revision/exclusion,
source SHA, approved tools and proxy-output checks.

**Settings/status improvement:** `LastResult=no-eligible-video` describes
only the *latest idle worker cycle* and can overwrite a preceding `published`
even though succeeded increased 31→32. Separate the last substantive video
outcome from the latest scan/idle reason, and show useful aggregate discovery
states without disclosing source names, paths or UUIDs. Automatic poster
`not-run` is normal when no poster attempt occurred.

**Development and verification plan:** Prefer one focused development PR for
source-level first-time online-only admission, safe retry/release, status
semantics and tests, with documentation and PR #759 acceptance evidence tracked
together. Only split if CI or a concrete ownership/recovery defect requires
another patch. Complete CI plus synthetic/disposable PostgreSQL verification
before asking the maintainer for another local test. The subsequent **single**
new live acceptance begins with a **never-inspected online-only** original,
verifies controlled inspection, supported proxy or durable unsupported
classification, and eventual safe release. Previously accepted real hydration/
release and earlier WI-0207 checkpoints are not to be repeated.
WI-0207 remains `in_progress` pending this newly agreed behavior.

**Source-format extensibility after the accepted live review:** Draft PR #761
introduces a single `VideoApprovedInputPolicy` for the queued and
newly discovered Archive selectors and post-inspection approval. Both
output qualities use this internal admission policy. A future source
class still needs an explicitly validated conversion recipe, source
probe and output proof before admission; adding a third operator quality
choice is not how source compatibility is extended. The current
Rec.709 SDR and full-range classes retain their existing durable recipe
identities; HDR is not admitted merely by being discovered.

## Maintainer final acceptance and WI-0207 closeout — 2026-11-08

**Status: COMPLETED; verified by maintainer on the maintained Windows PC.**
PR #761 merged on 2026-11-08 (merge commit
`f91df89a9524ff4ace0e51da39e8f3f6ef5fa6a5`).
Its final GitHub Actions run
[42428320140](https://github.com/erikwasa/Photo-Identity-Indexer/actions/runs/42428320140)
passed **all six jobs**: build/test, both integration shards,
launcher, package and change classification.

**Final first-time-discovery live test: PASS.** The maintainer selected
a previously **uninspected, online-only** video, with the **newly
approved input-format class** per operator report. An explicitly
authorized Archive run downloaded and inspected/processed the source,
then returned its original to **online-only**. The resulting
Photo Identity video played with working **audio, seeking, pausing,
and colours**. No manual local-first inspection step was necessary.
The exact source FFprobe tags, quality selection and job counters were
not included in this final report and are not invented here.

This completes the separate, earlier real acceptance for an
**already-inspected SDR** source (Standard 720p proxy, success
counter 31→32, no unintended hydration, unpinned source returned
online-only, proxy replay stayed online-only, user-pinned sources
unchanged) and the focused synthetic/disposable-PostgreSQL safety
checks. The source-level pre-revision ownership/hydration gap is
resolved for this controlled example. `no-eligible-video` continues
to mean an idle Archive scan, not failure of the last published video;
the settings now separately expose the last substantive result.

**Accepted limitations, not remaining WI-0207 blockers:** the Windows
Cloud Files/OneDrive provider and explicit online-only opt-in,
256 MiB maximum, single-video/photo-first worker, current
supported Rec.709 input classes with separately verified conversion
recipes for Standard 720p and Compact 540p, managed release only when
safe, operator-triggered retry after interruption (no daemon),
legacy pinned originals never automatically unpinned, and the
documented narrow last-moment user-pin/+u race. The live tests do
not prove all video formats, interrupted transfers or independent
real 540p quality. Those are not required for this scoped closeout.

**Decision: WI-0207 completed.** Further compatibility expansions
or broad fault-injection work require their own scoped approval rather
than reopening the accepted OneDrive hydration/release work item.
