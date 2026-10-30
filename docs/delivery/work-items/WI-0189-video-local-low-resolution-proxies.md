---
id: WI-0189
title: Generate and retain bounded local low-resolution video proxies
milestone: M30
status_source: PhotoIdentity.Docs
depends_on: [WI-0151, WI-0188]
related_adrs: []
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Api, PhotoIdentity.Persistence.Postgres, docs]
---

# WI-0189: Generate and retain bounded local low-resolution video proxies

## Objective

Generate a smaller playable video copy to keep on the local machine, separate from read-only originals and the OneDrive archive.

## Implementation progress (2026-10-30)

**In progress — PRs #665 and #666 merged; PR #667 under review.** The WI-0188 video
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
- **Checkpoint 3, PR #667:** optional, explicit application-configuration
  total registered-byte and per-proxy output caps (both required), validated
  before activating video work. The shared Archive worker propagates them
  through its approved tool adapter. Publication checks per-file size before
  moving bytes and checks the total of all registered proxy receipts inside
  the same PostgreSQL transaction that records success. A single exclusive
  video-control row lock serializes competing publication commits, preserving
  source/revision/exclusion/lease fences. Quota denial is a path-free,
  non-retryable failure: existing proxies/originals are preserved and only
  the rejected app-owned uncommitted output is reclaimed. Added targeted
  parser and synthetic PostgreSQL publication tests (pending CI).
  **No Settings UI, automatic eviction, unregistered-file measurement or
  physical disk quota enforcement is claimed.**
- Added the [retention checkpoint runbook](../../operations/video-proxy-retention-wi0189.md)
  with explicit remaining safety and verification gates.
- **Not yet implemented:** Settings UI or persisted quota/profile choices,
  automatic cache eviction and a true physical disk quota; the optional
  publication fence guards only newly registered output bytes.
  Measured size/quality acceptance, configurable encode profiles, and
  expanded real-MOV/MP4 verification remain open.
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
- Commands run: maintainer-confirmed PR #665 focused tests (8/8 passed),
  maintainer-reported docs validation/generation; checkpoint 2 .NET/CI
  tests and docs generation checks for PRs #666/#667 are **not yet
  independently verified** in this connector-only environment.
  Require CI before merging PR #667.
