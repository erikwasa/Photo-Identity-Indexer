---
id: WI-0207
title: Verify opt-in OneDrive-only video hydration and safe online-only release
milestone: M30
status_source: PhotoIdentity.Docs
depends_on: [WI-0188]
related_adrs: [ADR-0012]
affected_modules: [PhotoIdentity.Api, PhotoIdentity.Source.OneDriveSync, PhotoIdentity.Persistence.Postgres, PhotoIdentity.Integration.Tests, docs]
---

# WI-0207: Verify opt-in OneDrive-only video hydration and safe online-only release

## Objective

Independently verify and, only if evidence identifies gaps, harden the
**already opt-in** online-only video materialization and release behavior
that was intentionally **not exercised** during WI-0188 local-file
acceptance. This is a separate M30 workitem; it is not part of the
completed WI-0188 local-first processing acceptance.

## Origin and boundary

**Maintainer scope decision (2026-10-30):** WI-0188 successfully
processed locally available media and passed its persistence, integrity,
photo-regression, controls/recovery, exclusion and 17 isolated rejection/
scratch-cleanup tests. No online-only video was hydrated or released
during that verification. The unverified cloud-only behavior is moved
here rather than being represented as passed in WI-0188.

The WI-0188 implementation already contains default-off controls and
a guarded OneDrive-local staging path. This workitem first verifies
the implemented semantics rather than assuming new code is necessary.

## In scope

- Verify **Include online-only videos** is independently opt-in and
  disabled by default; with it off, online-only placeholders remain
  unhydrated and do not block eligible already-local video candidates.
- With explicit maintainer authorization and an already-catalogued
  eligible online-only test video, observe **one** bounded,
  source-controlled OneDrive hydration, with capacity/source/revision/
  exclusion checks before and after materialization; no mass recall.
- Verify the worker stages only the authorized local revision, enforces
  the same bounded FFprobe/FFmpeg, photo-priority, pause/cancellation
  and publication fences as for local inputs, and never writes originals.
- Verify that **only application-initiated** hydration may request
  return to online-only after use. Originally local media must remain
  local. A failed release leaves local bytes in place and reports the
  reason safely; no normal filesystem deletion of a cloud original.
- Test restart, cancellation and cloud/API failure cases without
  leaking paths or credentials, losing verified copies, or silently
  retrying unlimited transfers. Preserve exclusion and latest-revision
  fencing across attempted hydration.
- Document a reproducible operator verification procedure with
  explicit before/after local/online-only availability, job and source
  integrity evidence; add focused automated regression coverage where
  a demonstrated gap warrants changes.

## Out of scope

- Automatically hydrating online-only video, enabling this setting
  by default or changing WI-0188's locally available processing path.
- Processing arbitrary Internet URLs or using non-OneDrive cloud sources.
- Deleting source files, changing existing source-pinning preferences,
  mass downloading or migrating originals to Azure.
- Video playback, caption/embedding search and enhanced proxy profiles
  (separate M30 workitems).
- Re-running WI-0188's previously accepted 17 isolated safety tests or
  resetting its historical terminal failed jobs as a prerequisite.

## Acceptance criteria

- [ ] Default-off and independent online-only opt-in confirmed through
  settings persistence and safe handling when permission/tools are absent.
- [ ] With opt-in off, no cloud-only source is hydrated and local videos
  still make progress; excluded/out-of-coverage sources remain ineligible.
- [ ] One specifically authorized online-only video passes bounded
  hydration, revision/hash/local-availability checks and guarded processing
  without changes to originals or photo functionality.
- [ ] Return-to-online-only is attempted **only for an app-initiated
  download**; a pre-existing local file is never unpinned or deleted.
  Release failure or cancellation preserves bytes and is observable.
- [ ] Repeated runs, restart, cancellation and stale revision/exclusion
  cases fail closed with bounded retries, temporary cleanup and no
  incorrectly published result.
- [ ] Maintainer reports final real-OneDrive local acceptance, including
  before/after availability and privacy safety, and keeps this feature
  separate from local-video WI-0188 completion.

## Verification and safety

Start with read-only inspection of current controls and available test
media. Do not activate online-only hydration merely by creating this
workitem. A dedicated future verification run must explicitly record
the maintainer's approval of one non-sensitive, already-catalogued test
video and an authoritative backup where database changes are needed.
Never run bulk retries, force-download placeholders, delete originals,
or use a whole-archive test to compensate for an unavailable fixture.

Reference: [WI-0188 local-file acceptance](WI-0188-video-isolated-processing-lane.md),
[shared archive video runbook](../../operations/video-processing-shared-archive-verification.md),
and [ADR-0012](../../decisions/ADR-0012-isolated-video-media-architecture.md).

## Current status

**Proposed / not exercised (2026-10-30).** No real online-only
hydration, release or maintained-host cloud-only PASS has been claimed.
