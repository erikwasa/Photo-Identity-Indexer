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
