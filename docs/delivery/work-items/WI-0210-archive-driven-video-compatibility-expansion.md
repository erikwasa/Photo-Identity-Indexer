---
id: WI-0210
title: Expand video compatibility for the highest-value archive cases
milestone: M30
status_source: PhotoIdentity.Docs
depends_on: [WI-0151, WI-0188]
related_adrs: [ADR-0012, ADR-0013]
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Cli, PhotoIdentity.Api, PhotoIdentity.Persistence.Postgres, docs]
---

# WI-0210: Expand video compatibility for the highest-value archive cases

## Objective

Expand **one initial high-value subset** of playable/processable videos
identified from the actual archive. Do not turn this into universal
FFmpeg compatibility or a promise to handle every container, codec,
duration, resolution and colour profile. If further breadth is needed,
create a subsequent, separate workitem using the evidence from this one.

## Why and scheduling

The existing system catalogues and probes a wider MOV/MP4 subset than
the normal video worker accepts. Normal processing currently caps
originals at 256 MiB, duration at 600 seconds and coded dimensions at
4096, requires Rec.709 SDR, known rotation and selected codecs/pixel
formats; poster publishing and caption evidence have their own
eligibility policies. These are *different stages*, not a single
global support flag. Some locally present videos are catalogued but
cannot yet become playable/searchable.

**Priority:** Begin after basic browser playback (WI-0152) provides an
end-to-end result the maintainer can use. This is a planning preference,
not a hard technical dependency. WI-0207 separately owns on-demand
OneDrive hydration; do not conflate it with codec/processing eligibility.

## In scope

1. Produce a **privacy-safe aggregate, read-only archive survey** of
   known files and existing video metadata: counts and estimated user
   value by container, codec, transfer/colour, pixel format, rotation,
   resolution, duration, source size and source availability. Show
   exactly which gate currently prevents poster, playable proxy or
   search-evidence production. Avoid opening online-only placeholders
   merely to gather statistics. A small approved locally available
   sample may be probed where catalogue metadata is insufficient.
2. Rank two or three *candidate* coverage gaps by how many relevant
   archive videos they affect and the expected implementation effort.
   The maintainer chooses **one bounded first expansion** (or a
   naturally inseparable small cluster) before implementation.
3. Expand only the necessary detection/inspection, budget and
   derivative-policy gates for that selected class. Preserve explicit
   unsupported diagnostics and existing photo-only defaults.
   Examples, not commitments: source sizes above 256 MiB; durations
   above 600 seconds; currently rejected Rec.709 full-range pixels;
   missing rotation values; or a visually reviewed HLG-to-SDR profile.
4. Test the chosen class on one or a few approved real local files,
   plus focused guard/revision/exclusion/cleanup regressions. Verify
   whichever end-user stages the expansion promises. Do not require
   broad scene-recall, GPU accounting, extensive load tests or more
   inference than needed for that proof.
5. Update the staged support matrix and report counts unlocked at
   each stage. Revisit previously deferred WI-0190 scene coverage and
   WI-0191 candidate/relevance limits **only if** the resulting
   examples or library size actually justify it.

## Out of scope

- Unlimited codecs, container families, input sizes or duration.
- Full-archive hydration, background transcription or automatic
  transcoding of all discovered videos.
- Making all catalogued videos immediately playable and searchable.
- Mandatory HLG/PQ tone mapping without inspected real colour output.
- Building a second processing service or new Azure architecture.
- Video face recognition, video collections and slideshow integration
  (separate workitems).
- End-to-end OneDrive-only materialization/release (WI-0207).

## Acceptance criteria

- [x] A sanitized inventory and **stage-specific** exclusion summary
  identify the highest-value compatibility gap and limitations of
  locally available samples. **Satisfied by merged PRs #721–722**
  and maintainer-reported 58-video inventory. All source
  availability remains last-recorded **unknown**; this is only
  an archive-registered inventory, not a file availability test.
- [ ] Maintainer selects one bounded compatibility expansion and
  explicitly approves its supported input class and target stages.
- [ ] Selected real locally available video(s) succeed at the
  promised stage(s); unsupported cases remain diagnosable, originals
  unchanged, and existing photo/video exclusion guards intact.
- [ ] Focused automated safety/behaviour tests and maintained-host
  verification pass; update stage matrix with admitted limits and
  known remaining gaps.
- [ ] Further possible formats and deferred coverage are noted
  **without** preventing completion or automatically opening more
  compatibility work.

## Verification and risk boundaries

Use the existing FFprobe/FFmpeg installation, local processing lane,
video proxy publisher, safe output profiles and disposable PostgreSQL
tests. Prefer changes to configuration/policy and a small number of
confirmed real cases over a new technology stack. No private
file paths, names, GPS, camera serials, images or captions in PRs.
A source file that is already a OneDrive placeholder must remain
untouched unless WI-0207 separately authorizes hydration.

See [the staged compatibility matrix](../../operations/video-compatibility-matrix.md),
[ADR-0012](../../decisions/ADR-0012-isolated-video-media-architecture.md)
and the [M30 milestone](../milestones/M30-video-media-support.md).

## Development checkpoints

- **2026-11-03 checkpoint 1 — maintainer accepted:** PR #721 merged.
  Maintainer reported all seven local checks PASS, including the isolated
  PostgreSQL integration test. The sanitized catalogue census contains
  58 current nonexcluded videos (0 excluded): first worker gates are
  26 transfer, 13 pixel-format, 5 rotation, 1 unsupported inspection,
  and 13 metadata-eligible. Current registered receipts are 13 proxies,
  3 frame batches and 1 still poster. The gate partition sums to 58;
  stage receipt counts are **not** guaranteed to refer to the same videos.
  Historical 1,309 archive files are not all represented by the
  current PostgreSQL catalogue.
- **Next development slice — read-only single-gate candidate projections:**
  Quantify exactly how many Rec.709 `yuvj420p` revisions fail only
  the pixel gate, separately by last-recorded local/online-only state.
  Also show HLG-transfer-only, missing-transfer-only and
  missing-rotation-only upper bounds without relaxing any admission.
  **Proposed first expansion:** a separately validated full-range
  Rec.709 `yuvj420p` input to the **playable proxy stage only**.
  This is an engineering target, **not yet maintainer approval of a
  production processing profile**. A known-local source must prove
  correct full-to-limited YUV range conversion and Rec.709 signaling,
  orientation, preserved original bytes, and no unexpected hydration;
  `color_range` is not presently recorded in catalogue inspection.
  Keep existing HDR/poster/frame policies unchanged.

- **Pending:** CI for the next checkpoint, exact pixel-format and
  counterfactual eligibility distribution, a bounded approved local
  real-video conversion test and final production-profile authorization.
  No format, budget, encoding, poster or frame policy has been widened;
  acceptance checkboxes remain open.
- **Approval gate:** select one bounded supported input class and promised
  stage(s) from actual archive findings *before* modifying compatibility.

- **2026-11-03 checkpoint 2 — merged PR #722, maintainer census:**
  Real production database `photoidentity_main` contains 58 current
  nonexcluded videos. Single-gate projections: **12** Rec.709
  `yuvj420p` pixel-only, **22** HLG transfer-only and **5**
  missing-rotation-only candidates. All 58 have **unknown**
  last-recorded source availability. The format breakdown is
  20 `yuv420p`, 25 `yuv420p10le`, 13 `yuvj420p`;
  transfer is 32 `bt709`, 25 `arib-std-b67`, 1 unknown.
  Select Rec.709 full-range **playable-proxy only** for further
  engineering, conditional on real local-source and colour validation.
- **Checkpoint 3 — isolated Rec.709 full-range evaluation contract,
  proposed:** add a fail-closed independent FFprobe input contract,
  versioned full-to-limited Rec.709 720p conversion recipe and
  stricter limited-range output validation with deterministic
  tests. The profile is **not registered** with production workers,
  encoding preferences, enqueuers or durable receipts. This does
  not authorize OneDrive hydration, processing or release.
  A real already-local clip, proven `color_range=pc` and visual
  conversion quality are required before production admission.

- **2026-11-03 checkpoint 3 — PR #723 merged:** the maintainer
  merged the unregistered fail-closed Rec.709 `yuvj420p` input/output
  contract after fixing its initial no-op JSON fixture tests.
  No production worker, source, OneDrive or codec-policy expansion.
- **Checkpoint 4 — guarded synthetic actual-tool roundtrip (proposed):**
  Extend only the existing `video synthetic-proxy-pilot` with explicit
  `--full-range-evaluation`, keeping default dry-run and current
  standard/compact profiles unchanged. Generate two fixed synthetic
  videos with actual full-range pixels and explicit Rec.709 signalling;
  independently prove `yuvj420p` + `pc` on the generated source,
  transcode using the unregistered versioned BT.709 full→limited
  720p recipe, and independently prove H.264/AAC output
  `color_range=tv` with correct orientation/audio. Continue using
  approved FFmpeg/FFprobe digests, Windows Job Object budgets and
  marked disposable scratch. CI tests must cover safe opt-in,
  independent range evidence, existing profile isolation,
  failure cleanup and regression.
  **Run only after PR merge on local main**, using the new
  [synthetic acceptance runbook](../../operations/video-fullrange-wi0210-synthetic-acceptance.md);
  no separate local docs validation is requested.
- **2026-11-03 checkpoint 4 — PR #724 merged; maintainer accepted:**
  The actual maintained Windows FFmpeg/FFprobe roundtrip reported
  `status=validated` for both **720×406 landscape with audio** and
  **406×720 portrait without audio**, 3 seconds each; recorded
  compressed sizes 183,352/149,221 bytes and 630/832 ms encode times.
  `originalsOpened=false`, `schemaUsed=false`,
  `videoWorkersActivated=false`, `derivativesRetained=false`;
  scratch contained only the ownership marker and was removed by
  the maintainer. This is accepted synthetic colour-*signalling*
  evidence, not approved real-source colour/contrast.
- **Checkpoint 5 — bounded, metadata-only live availability audit
  proposed:** Inspect only the 12 Rec.709 `yuvj420p` pixel-only
  current, nonexcluded catalogue candidates for current Windows
  Files On-Demand **attributes and length**; no source content is
  opened, no hashing, FFprobe, staging, hydration or jobs. Emit only
  aggregate results without source names or IDs. Use
  [the post-merge local-check runbook](../../operations/video-compatibility-wi0210-local-check.md)
  **after the PR merges into main**. CI owns all docs checks.
- **Still pending:** a selected, already-local *real* `yuvj420p`
  video, live OneDrive availability without hydration, revision/SHA
  checks, real colour/contrast/rotation approval, a separately
  versioned opt-in production profile and worker, publisher and
  UI/playback acceptance. WI-0210 stays in_progress.

## Completion notes

- Status: in_progress; checkpoint 1 and the merged PR #722 survey
  identify 12 isolated Rec.709 pixel candidates. All 58 source
  availabilities remain unknown; production admission and real-video
  conversion evidence are pending.
- Scope: one evidence-selected archive compatibility slice, with new
  workitems only for later independently justified expansions.
