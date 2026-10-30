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

**Implementation ready for review — PRs #665–#670 merged and #670
Windows GitHub CI passed; PR #671 adds the isolated 540p/720p
synthetic preflight and final operator acceptance handoff.** The WI-0188 video
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
- **Checkpoint 5, PR #669 merged:** add explicit Video Settings
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
  orphan accounting is added. GitHub Windows CI passed for #669,
  but maintained-host actual deletion is not yet accepted.
- **Checkpoint 6, PR #670 merged:** add a bounded operator-selectable
  `standard-720p` (unchanged WI-0188 preset/version and exact arguments)
  or `compact-540p` (new immutable v1 preset, lowered bitrate/CRF
  parameters) SDR H.264/AAC MP4 profile in persisted Video Settings.
  Also add a strictly bounded user-configurable minimum retention
  age for manually confirmed cleanup (48 hours, 7 days or 30 days),
  preserving the 48-hour default on legacy preference files.
  Changes in retention choice invalidate older fingerprinted previews;
  they do not launch or schedule any eviction. The maintenance
  preview/apply HTTP boundary now requires **both socket endpoints
  to be loopback**, preventing direct trusted-LAN clients from
  accessing proxy deletion even when phone viewing is enabled.
  Strict whitelist only: no arbitrary FFmpeg flags, external codec, GPU
  dependency or online-only hydration. New profile selection changes
  the immutable idempotency identity and filters queued work by exact
  selected preset and FFmpeg digest. Independent FFprobe output checks
  enforce the profile's maximum dimension. Old jobs, receipts and
  copies are **not** silently converted or deleted; switching back
  reuses matching existing identity. Tests cover argument compatibility,
  validation, idempotency and Settings/legacy persistence.
  **Actual relative size, quality, encode duration, orientation, audio
  and playback results remain pending maintained-host verification.**
- **Checkpoint 7, PR #671:** extend the already accepted, hash-pinned,
  source-free synthetic FFmpeg/FFprobe pilot with an optional strictly
  allowlisted `compact-540p` profile. The existing `standard-720p`
  remains the default. Both pilot profiles check temporary-file cleanup,
  orientation and optional audio on disposable generated footage; no
  PostgreSQL, original videos or hydration are used. Profile-specific
  pilot durations and output sizes are synthetic diagnostic evidence,
  not representative real-video acceptance.
- **Maintainer decision (2026-10-30):** manually confirmed, stopped-lane,
  SHA-verified database-first eviction **satisfies WI-0189 retention**.
  Unattended eviction is no longer required and will not be implemented
  for this item. The registered-byte admission quota is **not** a hard
  physical disk quota; unregistered/orphan files remain bounded by the
  existing conservative maintenance/recovery tooling, not auto-deleted.
- Added the [retention checkpoint runbook](../../operations/video-proxy-retention-wi0189.md)
  with explicit remaining safety and verification gates.
- **Development handoff:** the chosen scope (local, default-off,
  photo-first processing; versioned selectable preset; registered-byte
  quotas; manually confirmed retention/recovery) is implemented.
  WI-0189 is now `in_review`, pending PR #671 CI, maintainer
  Windows synthetic pilot, already-local MOV/MP4 comparative quality
  measurements, revision/preset reuse and safe cleanup verification.
  No real-media or end-to-end WI-0189 acceptance is claimed yet.
  All acceptance checkboxes remain open until the results are supplied.

## In scope

- Opt-in transcoding through the isolated lane after WI-0150 approves FFmpeg/deployment decisions; measure a browser-friendly profile such as bounded 720p H.264/AAC MP4 without assuming universal input compatibility.
- Application-managed **local** derivative cache, never the original or OneDrive-synced folders. Store source revision, encoder/preset version, size, codec and creation status.
- User-configurable bounded encoding profile, **registered-byte** quota,
  per-file cap and retention age; eviction is manual, previewed and
  explicitly confirmed, affecting only generated private copies.
  Unattended deletion and strict physical filesystem quota are not
  requirements for the maintainer-accepted scope.
- Atomic temp-file publication, deduplication, cancellation cleanup, revision/preset invalidation, and exclusion/deletion privacy cleanup.
- Preserve retained **already-local** proxy availability when an
  original subsequently becomes online-only without triggering recall.
  Online-only input must be skipped by default; actual opt-in hydration,
  reversion to online-only and any related release tests are **WI-0207**,
  not WI-0189.

## Out of scope

Player/range API (WI-0152), automatic retention scheduling, strict
physical filesystem quota, OneDrive-only hydration/release (WI-0207),
editing/trimming, universal codec support, captioning and facial identification.

## Acceptance criteria

- [ ] Representative MOV/MP4 clips produce smaller, seekable proxies; orientation, aspect and audio policy are correct, and originals remain bit-for-bit unchanged.
- [ ] Repeated jobs reuse a valid revision/preset-matched proxy; changed revisions or policy invalidate correctly.
- [ ] Registered-byte quota refusals, manually confirmed eviction,
  stopped jobs, corrupt/unsupported input, exclusion and **default-off
  online-only skipping** are deterministic and do not leak temporary
  files; opt-in hydration/release is independently verified in WI-0207.
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
- PR #668 and #669 GitHub build CI completed successfully. PR #669
  actual maintained-host removal/recovery is **not** accepted yet.
- Checkpoint 6: versioned FFmpeg profile registry, preset-scoped queue
  discovery/idempotent identity, independent output validation,
  persisted Settings for profile/48h–30d retention age and
  loopback-only cleanup. PR #670 merged, GitHub Windows build CI
  succeeded; local comparative media acceptance has not been run.
- Checkpoint 7 (PR #671): optional compact-profile synthetic pilot,
  parser/output-size/orientation/audio/cleanup regression coverage,
  and repository-native verification handoff. No .NET tools or media
  were executed in the GitHub connector authoring environment.
- Acceptance pending: PR #671 CI, Windows isolated 720p/540p
  pilot, bounded already-local representative media comparison,
  original SHA-256 immutability, preset/revision reuse, quota
  refusal, manual stopped-lane recovery and default profile decision.
  No automatic OneDrive recall or release; WI-0207 owns that gate.
