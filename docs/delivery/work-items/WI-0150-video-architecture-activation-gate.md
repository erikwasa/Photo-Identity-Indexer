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
- [x] FFmpeg platform codec dependencies, observed Gyan GPLv3+ build, operator-installed external-tool packaging and security constraints are evaluated in ADR-0012. Actual binary hashes and runtime validation remain WI-0151 prerequisites.
- [x] The separate opt-in lane, resource limits, derivative cache/invalidation and photo-only search defaults are documented in proposed ADR-0012; final maintainer approval remains open.
- [x] WI-0188 through WI-0191 are separately sequenced; face matching is not a prerequisite for ingestion, proxies or visual evidence.
- [x] Maintainer explicitly requested beginning WI-0150 on 2026-10-14; M30's **design phase** is active. Runtime implementation remains gated on an accepted ADR and private codec survey.

## Verification requirements

Architecture review only. Proposed decisions and a privacy-preserving local codec survey are described in [ADR-0012](../../decisions/ADR-0012-isolated-video-media-architecture.md), the [video codec survey runbook](../../operations/video-codec-survey.md), and [sanitized real-archive evidence](../../operations/video-codec-survey-evidence-2026-10-14.md). **The initial survey has succeeded** for 18 already-local videos. Do not mark WI-0150 completed or begin WI-0151 before the actual FFprobe/FFmpeg binary origin, packaging/security/license review and explicit maintainer acceptance.

## Current design status (2026-10-14)

- The maintainer's request to proceed starts WI-0150 **architecture work**, not automatic video ingestion. M30 is now in progress for planning.
- First proposed implementation slice: opt-in MOV/MP4 cataloguing, revision-bound metadata and still posters (WI-0151). No image pipeline generalization, bulk proxy conversion or automatic face matches.
- ADR-0012 is **proposed**, not accepted. Initial real-archive MOV/MP4 codec metadata evidence, installed Gyan FFmpeg 9.0.2 GPLv3+ flags and external-tool packaging recommendation are now recorded. Pending: maintainer acceptance of initial input scope/opt-in design; before production use also record the local SHA-256 tool fingerprints and verify runtime safeguards.
- Survey helper: `tools/VideoArchiveCodecSurvey.ps1` uses bounded local-only sampling and emits a privacy-conscious aggregate JSON report outside the repository.


## Private codec survey attempt — 2026-10-14

- Maintainer ran the original local survey: 74 MOV and 1,235 MP4 enumerated, but all 1,309 were marked skipped before FFprobe. This is **not** evidence that the videos are unsupported or genuinely online-only.
- The survey introduced an extra "ReparsePoint must be pinned" rule absent from `OneDriveFileAttributeStatusProvider.Classify`. A locally hydrated unpinned OneDrive file can have `ReparsePoint` without missing-content flags, and should be eligible. A focused follow-up corrects this mismatch and separates online-only/downloading/local-reparse diagnostics.
- Actual codecs, metadata, and FFprobe packaging/license evidence remain **unverified** until a successful survey is run. The 2026-10-14 retest confirmed that all 1,309 files were marked online-only at that time; a second attempt failed with PowerShell StrictMode `The property 'Name' cannot be found`, caused by dereferencing `.PSObject.Properties.Name` for missing/empty format tags. WI-0150 survey follow-up corrects this and awaits a completed retest. No original video content or private paths are recorded here.

## Successful codec metadata survey — 2026-10-14

- Maintainer supplied a successful `schema_version: 2` report. Within the surveyed camera folder, 74 MOV and 1,235 MP4 were enumerated; 3 MOV and 15 MP4 already-local clips were successfully probed, with zero failures/timeouts. The remaining 1,291 were online-only and not probed.
- 9 H.264/AVC (8 High, 1 Main), 9 HEVC/H.265 (8 Main, 1 Main 10 with HLG transfer); audio AAC 17, PCM 1; -90° rotation 15, missing/unknown 3; one 4K clip. Capture-time tag was present in 15, location tag in 6. 15 clips were under 15 seconds and 3 were under 2 minutes.
- **Evidence limitation:** 18 availability-selected clips do not represent all 1,309 videos; no actual poster decode/quality, long-video behavior or full-archive codec support has been verified.
- [Sanitized counts and proposed WI-0151 input set](../../operations/video-codec-survey-evidence-2026-10-14.md) are in Git; the private JSON, identifying metadata and media are not.
- FFmpeg architecture review supplied: Winget-installed Gyan full build 9.0.2; `--enable-gpl --enable-version3 --enable-static --enable-libx264 --enable-libx265`; `--enable-nonfree` absent; reported GPL v3 or later. **Select external, operator-installed binaries only** (no bundled FFmpeg in Photo Identity). SHA-256 fingerprints of exact executables remain unrecorded and must be verified before production tool execution.
- Remaining WI-0150 architecture gate: maintainer explicitly accept the proposed ADR-0012 and initial opt-in scope before WI-0151 implementation.

## Completion notes

- Files changed: ADR-0012, video codec survey runbook/helper, WI-0150 lifecycle and M30 plan/status, project scope/decision index.
- Trade-offs: opt-in independent video lane avoids changing photo operations, but requires distinct metadata/job/derivative contracts and later measured resource budgets.
- Deferred work: explicit maintainer ADR acceptance; exact executable SHA-256 and runtime tool-adapter safety checks; all runtime changes and poster-decode/long-video validation under WI-0151 onward.
- Commands run: GitHub document/branch operations; .NET CLI and private Windows survey were not runnable in this environment. CI must run `PhotoIdentity.Docs validate` and `generate --check`.
