# WI-0188 / WI-0189 handoff — validated SDR 720p proxy candidate

**Development-only codec evaluation profile. Production video processing is still disabled.**

An unregistered VideoExperimentalProxyAdapter brings together the existing isolated
Windows video attempt, revision-fenced locally staged video, fixed-argument FFmpeg
candidate profile and independent, hash-approved FFprobe. No CLI command or hosted
worker registers or enables it. There is no automatic source hydration, new
video queue admission, media-serving endpoint or photo pipeline change.

## Pilot policy

Profile version: wi0188-sdr-h264-aac-720p-pilot-v1. The video job must carry
that exact version and the approved separately installed FFmpeg binary SHA-256.
The independent FFprobe binary must also have an approved operator-selected SHA-256.

- Only previously inspected **supported** video revisions: H.264/HEVC,
  coded width/height 16–4096, duration 0.2–600s, explicitly Rec.709 SDR
  transfer, YUV420 8/10-bit, known 0/±90/±180-degree display rotation,
  and AAC/PCM or no detected audio. HLG, PQ, unknown transfer and other
  input formats are deliberately deferred, not silently interpreted as SDR.
- FFmpeg selects one video stream and optionally one audio stream,
  strips source metadata, subtitles/data streams and chapters; software
  libx264 Main / yuv420p, no more than 720px on either axis, max 30fps,
  AAC 96k stereo 48kHz, aspect-preserving even scaling and normal FFmpeg
  auto-rotation. No GPU flags, shell, user-defined filters or protocol fetches.
- The isolated lane still requires a native Windows Job Object,
  one-process publishing, bounded scratch, memory/CPU, and lease/photo-priority
  checks. The FFmpeg profile applies an additional 256MiB file cap, a
  600s output duration cap and faststart MP4 layout. It is not a hard
  filesystem/IO quota.

## Independent output validation

Before durable publication, the successful child-exit callback verifies
MP4 top-level boxes: ftyp first; valid box boundaries; complete layout;
moov *before* mdat. A separate SHA-256-approved FFprobe runs with its own
15-second, single-process, CPU-limited native Windows Job Object and file-only
protocol whitelist. Its JSON is written only to a marked scratch workspace,
never stdout/logs, and is capped at 64KiB. The probe also runs through
live lease/control and photo-priority checks before launch, periodically
during execution and once more after completion; any loss of permission
stops it and refuses publication.

The validator requires exactly one H.264 yuv420p Rec.709 video stream,
max 720x720, even dimensions, at most 30.1fps, zero remaining display
rotation, at most one AAC mono/stereo audio stream and no other stream
types. It verifies the MP4 container, duration and declared output size
against the actual file. The existing v36 publisher then rehashes the
file and atomically persists the private receipt and succeeded job under
source, lease, control, revision, cancellation and exclusion fences.

FFprobe and basic MP4 boxes **do not establish full decode, correct color,
visual quality, browser seeking or audio synchronization**. The candidate
profile is not a production-approved default. Full WI-0189 evaluation,
privacy-aware registered-derivative purge/retention, quotas and hard I/O
confinement remain essential before video activation.

## Consolidated future Windows acceptance

After an explicit reviewed activation gate, evaluate only already-local
selected MOV/MP4 clips with rotation, landscape/portrait, H.264/AAC,
HEVC/PCM, silent, HLG-deferred, longer/higher-fps and corrupt cases.
Record original SHA-256 before/after; video dimensions, duration, encoded
bytes, encode elapsed time, actual browser seeking, perceived sharpness,
orientation/audio behavior and OneDrive no-recall outcome. Benchmark the
photo-only and mixed workload plus search/slideshow latency and stop,
cancel, exclusion, restart and cleanup semantics. Do not upload media,
source paths or private tool logs to GitHub.

The experimental libx264 argument profile now explicitly emits Rec.709
color primaries, transfer and matrix in the H.264 VUI bitstream, in
addition to MP4 encoder metadata. This was tested against an installed
FFmpeg/FFprobe pair after the independent validator rejected missing
transfer/primaries fields. No relaxation of the fail-closed color
validator was needed.

## Source-free end-to-end codec smoke path

A complementary development command now offers a real **synthetic-only**
FFmpeg → experimental 720p fixed encoder → independently verified FFprobe
pipeline, exercised on two short landscape/portrait generated fixtures
without reading an archive, database or OneDrive. It leaves no video
artifacts, cannot publish durable receipts, and is dry-run by default.
See [the synthetic pilot runbook](video-synthetic-proxy-pilot.md).
This does not replace private camera-media quality, audio, browser and
no-hydration acceptance.

## Maintained schema protection

The accepted maintained database is v34; this PR retains v36 and adds
no migration. Do **not** run any v36-based application/CLI against that
database until a **new** authoritative backup and isolated v34→v36
rehearsal pass following
[the consolidated v36 runbook](video-publication-and-v36-rehearsal.md).
No maintainer local verification is requested for this unregistered batch.

The FFprobe -o flag is documented by
https://ffmpeg.org/ffprobe.html. The separately operator-installed
FFmpeg deployment/licensing decision is recorded in ADR-0012; no external
binaries are bundled.
