---
id: WI-0150
title: Define the video media architecture and explicitly activate deferred implementation
milestone: M30
status_source: ../status/work-items.yaml
depends_on: [WI-0137]
related_adrs: [ADR-0012, ADR-0007, ADR-0008, ADR-0010]
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Api, PhotoIdentity.Web, docs]
---

# WI-0150: Define the video media architecture and explicitly activate deferred implementation

## Objective

Before implementation begins, define how video fits asset/revision, metadata, derivative, collection and playback boundaries and record the deliberate activation decision.

## Why

Video is feasible but broader than another image format: codecs, duration, posters, range serving, browser playback, hydration, frame analysis and Live Photos introduce different lifecycle concerns.

## In scope

- Confirm initial containers/codecs from the maintained archive.
- Define media-kind contracts without rewriting stable identity.
- Distinguish still posters from smaller playable video proxies; choose codecs, managed local cache/quota/retention and revision/exclusion cleanup.
- Define opt-in video-only processing jobs, priority relative to photos, bounded CPU/GPU/IO/hydration and stop/resume controls.
- Define sampling/caption/visual embedding evidence, model/provenance and search-media defaults without assuming sparse frames describe full motion/audio.
- Define browser range-serving and hydration expectations.
- Define optional manual metadata/collection integration *later* while preserving existing photo-only results by default.
- Sequence face/track identification experiments, viewer/slideshow work and Live Photo pairing separately.
- Record an accepted ADR, supported archive codec survey and FFprobe/FFmpeg packaging/security/licensing review at activation.

## Out of scope

- Implementing video while M30 remains held.
- Universal codec promises.
- Making frame-level face recognition prerequisite for basic support.

## Acceptance criteria

- [ ] An accepted design identifies the smallest useful first slice.
- [x] Media-neutral versus image-specific contracts are explicit in proposed ADR-0012.
- [ ] FFmpeg/platform codec dependencies, packaging, security and licensing are evaluated before adoption.
- [x] The separate opt-in lane, resource limits, derivative cache/invalidation and photo-only search defaults are documented in proposed ADR-0012; final maintainer approval remains open.
- [x] WI-0188 through WI-0191 are separately sequenced; face matching is not a prerequisite for ingestion, proxies or visual evidence.
- [x] Maintainer explicitly requested beginning WI-0150 on 2026-10-14; M30's **design phase** is active. Runtime implementation remains gated on an accepted ADR and private codec survey.

## Verification requirements

Architecture review only. Proposed decisions and a privacy-preserving local codec survey are described in [ADR-0012](../../decisions/ADR-0012-isolated-video-media-architecture.md) and the [video codec survey runbook](../../operations/video-codec-survey.md). The maintained archive is not available through GitHub and **no real codec survey has been run**; do not mark WI-0150 completed or begin WI-0151 before the survey, executable security/license check and maintainer acceptance.

## Current design status (2026-10-14)

- The maintainer's request to proceed starts WI-0150 **architecture work**, not automatic video ingestion. M30 is now in progress for planning.
- First proposed implementation slice: opt-in MOV/MP4 cataloguing, revision-bound metadata and still posters (WI-0151). No image pipeline generalization, bulk proxy conversion or automatic face matches.
- ADR-0012 is **proposed**, not accepted. Pending evidence: representative local MOV/MP4 codec/metadata counts, actual FFprobe/FFmpeg build provenance/license flags and explicit maintainer approval of the design.
- Survey helper: `tools/VideoArchiveCodecSurvey.ps1` uses bounded local-only sampling and emits a privacy-conscious aggregate JSON report outside the repository.


## Private codec survey attempt — 2026-10-14

- Maintainer ran the original local survey: 74 MOV and 1,235 MP4 enumerated, but all 1,309 were marked skipped before FFprobe. This is **not** evidence that the videos are unsupported or genuinely online-only.
- The survey introduced an extra "ReparsePoint must be pinned" rule absent from `OneDriveFileAttributeStatusProvider.Classify`. A locally hydrated unpinned OneDrive file can have `ReparsePoint` without missing-content flags, and should be eligible. A focused follow-up corrects this mismatch and separates online-only/downloading/local-reparse diagnostics.
- Actual codecs, metadata, and FFprobe packaging/license evidence remain **unverified** until a successful survey is run. The 2026-10-14 retest confirmed that all 1,309 files were marked online-only at that time; a second attempt failed with PowerShell StrictMode `The property 'Name' cannot be found`, caused by dereferencing `.PSObject.Properties.Name` for missing/empty format tags. WI-0150 survey follow-up corrects this and awaits a completed retest. No original video content or private paths are recorded here.

## Completion notes

- Files changed: ADR-0012, video codec survey runbook/helper, WI-0150 lifecycle and M30 plan/status, project scope/decision index.
- Trade-offs: opt-in independent video lane avoids changing photo operations, but requires distinct metadata/job/derivative contracts and later measured resource budgets.
- Deferred work: Maintained-archive codec proof and FFmpeg build compliance check, ADR acceptance, all runtime changes under WI-0151 onward.
- Commands run: GitHub document/branch operations; .NET CLI and private Windows survey were not runnable in this environment. CI must run `PhotoIdentity.Docs validate` and `generate --check`.
