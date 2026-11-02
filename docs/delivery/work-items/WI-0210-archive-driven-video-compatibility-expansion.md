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

- [ ] A sanitized inventory and **stage-specific** exclusion summary
  identify the highest-value compatibility gap and limitations of
  locally available samples.
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

## Completion notes

- Status: proposed; inventory, prioritization and actual expansion pending.
- Scope: one evidence-selected archive compatibility slice, with new
  workitems only for later independently justified expansions.
