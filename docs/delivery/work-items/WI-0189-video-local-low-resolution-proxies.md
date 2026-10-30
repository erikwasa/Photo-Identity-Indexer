---
id: WI-0189
title: Generate and retain bounded local low-resolution video proxies
milestone: M30
status_source: PhotoIdentity.Docs
depends_on: [WI-0151, WI-0188]
related_adrs: []
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Cli, PhotoIdentity.Api, PhotoIdentity.Web, PhotoIdentity.Persistence.Postgres, docs]
---

# WI-0189: Generate and retain bounded local low-resolution video proxies

## Objective

Generate a smaller playable video copy to keep on the local machine, separate from read-only originals and the OneDrive archive.

## Implementation progress (2026-10-30)

**In progress — PRs #665–#668 merged (PRs #667 and #668 passed CI);
PR #669 adds confirmed operator-only cache reclamation.** The WI-0188 video
lane already provides a bounded, validated SDR H.264/AAC MP4 candidate
profile with local staging and atomic, revision-fenced publication. WI-0189
extends that foundation rather than enabling a second worker.

- Added `VideoProxyRetentionPolicy`, a pure quota/admission planner with
  configurable **policy inputs** (total bytes and per-proxy cap), stable
  oldest-unpinned-first selection, fail-closed malformed inventory handling,
  and no filesystem side effects.
- Added deterministic tests for fit, eviction order, pinned entries,
  insufficient capacity, invalid sizes and duplicate/overflow inventory.
  **Maintainer Windows verification (2026-10-30):** PR #665 merged,
  8 passed / 0 failed / 0 skipped; build succeeded with
  `-p:WasmEnableWebcil=false` to bypass local Webcil permission failure.
  Maintainer also reports running documentation validation/generation,
  without separate command outputs in the record.
- **Checkpoint 2, PR #666 merged:** read-only, bounded PostgreSQL snapshot of
  registered, current, revision/exclusion-fenced proxy receipts; checked
  canonical app-owned cache bytes; fail-closed incomplete/inconsistent
  inventory; deterministic hypothetical quota/age planning and an
  operator-only `video derivatives retention-plan` command. It has
  no `--apply` option and never modifies files, jobs or originals.
  Added focused CLI parse cases, over-quota recovery policy test and
  isolated PostgreSQL fixture scenarios (PR merged; no separate maintainer
  verification outcome has been supplied).
- **Checkpoint 3, PR #667 merged:** optional, explicit application-configuration
  total registered-byte and per-proxy output caps (both required), validated
  before activating video work. The shared Archive worker propagates them
  through its approved tool adapter. Publication checks per-file size before
  moving bytes and checks the total of all registered proxy receipts inside
  the same PostgreSQL transaction that records success. A single exclusive
  video-control row lock serializes competing publication commits, preserving
  source/revision/exclusion/lease fences. Quota denial is a path-free,
  non-retryable failure: existing proxies/originals are preserved and only
  the rejected app-owned uncommitted output is reclaimed. Added targeted
  parser and synthetic PostgreSQL publication tests (PR #667 GitHub CI
  completed successfully; no maintainer real-video acceptance is claimed).
  **No Settings UI, automatic eviction, unregistered-file measurement or
  physical disk quota enforcement is claimed.**
- **Checkpoint 4, PR #668 merged:** add optional user-editable and locally
  persisted per-proxy (1..256 MiB) and total registered-byte
  (64..16384 MiB) override in Video Settings, using the already-approved
  transaction-level quota fence. Both are required when overriding
  launcher values; invalid/partial values fail closed before video control
  changes, and malformed persisted settings disable video and cloud consent.
  Historic two-boolean preference JSON remains backward compatible.
  The UI does not delete any files or enable eviction; settings take
  effect for the next video attempt without a restart. Focused persisted
  preferences/precedence/legacy/invalidity tests added (PR #668 CI
  passed; optional maintainer UI-only smoke remains unverified).
- **Checkpoint 5, PR #669:** add explicit Video Settings
  **Preview proxy cleanup → Confirm removal** workflow using the
  pre-existing stopped-lane, DB-first registered derivative maintenance,
  not a second unlink implementation. A bounded scan (at most 8
  receipts, SHA-256 verified) requires the video lane disabled+paused
  and zero running leases. An opaque five-minute plan fingerprint
  binds the candidate IDs/paths/receipt evidence, quota and cache root;
  apply re-evaluates these checks and rejects stale/replayed plans.
  Only approved private cache outputs are selected; source originals
  and unknown cache files remain out of scope. Synthetic isolated
  PostgreSQL cases cover same-size corruption, stale/replayed
  confirmation, cleanup and unchanged healthy/unknown files.
  **This is manual, explicitly confirmed eviction only**:
  no automatic retention scheduler, full physical disk quota or
  orphan accounting is added. PR #669 CI pending.
- Added the [retention checkpoint runbook](../../operations/video-proxy-retention-wi0189.md)
  with explicit remaining safety and verification gates.
- **Not yet implemented:** user-configurable *encoding profile*, automatic
  unattended cache eviction, true physical disk quota and
  unregistered/orphan-byte accounting. Manual confirmed retention
  and user-editable publication limits are implemented; the latter
  still guards only newly registered output bytes. Representative MOV/MP4
  size/quality, compatibility and restart/retention workflow verification
  remain open.
  No real-media or end-to-end local verification is claimed for this checkpoint.
  The acceptance checkboxes below remain open.

## In scope

- Opt-in transcoding through the isolated lane after WI-0150 approves FFmpeg/deployment decisions; measure a browser-friendly profile such as bounded 720p H.264/AAC MP4 without assuming universal input compatibility.
- Application-managed **local** derivative cache, never the original or OneDrive-synced folders. Store source revision, encoder/preset version, size, codec and creation status.
- User-configurable profile, disk quota, per-file cap and retention/eviction; safe eviction affects only generated copies.
- Atomic temp-file publication, deduplication, cancellation cleanup, revision/preset invalidation, and exclusion/deletion privacy cleanup.
- Bounded/manual hydration, explicit unsupported/corrupt-file errors and proxy availability after the original returns online-only where policy allows.

## Out of scope

Player/range API (WI-0152), editing/trimming, universal codec support, captioning and facial identification.

## Acceptance criteria

- [ ] Representative MOV/MP4 clips produce smaller, seekable proxies; orientation, aspect and audio policy are correct, and originals remain bit-for-bit unchanged.
- [ ] Repeated jobs reuse a valid revision/preset-matched proxy; changed revisions or policy invalidate correctly.
- [ ] Quota, eviction, stopped jobs, corrupt input, exclusion and OneDrive-online-only cases are deterministic and do not leak temp files.
- [ ] Report proxy size, encode time, browser compatibility and perceived quality to choose a default profile.

## Verification requirements

Integration lifecycle and representative private-file performance/quality tests, including long/rotated/no-audio clips.

## Completion notes

- Files changed (checkpoint 1): `src/PhotoIdentity.Core/Processing/VideoProxyRetentionPolicy.cs`,
  `tests/PhotoIdentity.Integration.Tests/VideoProxyRetentionPolicyTests.cs`,
  `docs/operations/video-proxy-retention-wi0189.md`, and canonical/generated
  delivery status documentation.
- Trade-offs: start with side-effect-free, opaque-ID retention planning;
  no file deletion until ownership, lease and revision fences can be enforced.
- Deferred work: catalogue-backed quotas and eviction, configurable profile,
  output quality/size evidence and private-file end-to-end verification.
- Checkpoint 2 changed files: `src/PhotoIdentity.Persistence.Postgres/PostgresVideoProxyRetentionPreview.cs`,
  `src/PhotoIdentity.Cli/VideoProxyRetentionPreviewCommand.cs`,
  CLI dispatch/help, fixture and argument tests, pure planner correction,
  runbook and canonical/generated documentation.
- Checkpoint 3 changed files: Core strict opt-in quota configuration,
  PostgreSQL registered receipt commit/publisher, video adapters and API
  runtime, CLI failure diagnostic, launcher configuration allowlist and
  synthetic regression tests, plus canonical/generated documentation.
- Checkpoint 4 changed files: VideoProcessingPreferences persistence and
  validation, API response/update contracts, Video Settings Blazor UI,
  shared Archive worker policy resolution, focused quota-preference tests,
  CI filter, operations/runbook and canonical/generated delivery documentation.
- Verified evidence: maintainer confirmed PR #665 focused tests **8/8** on
  Windows and reported documentation commands; merged PR #667 GitHub
  build workflow reported **success**. PR #666 merged; no separate
  maintainer PostgreSQL fixture outcome supplied.
- Checkpoint 5 changed files: registered maintenance fingerprint,
  explicit API operator service/endpoints, Videos Settings preview/confirm
  controls, isolated PostgreSQL regression cases, operations runbook,
  canonical/generated status and milestone/build context.
- PR #668 GitHub build CI completed successfully. PR #669 .NET tests
  and docs generator have **not** run in this GitHub connector authoring
  environment; require GitHub Windows CI before merge. Do not claim
  maintained-host deletion/recovery accepted until later local pilot.
