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
