# WI-0190 — bounded source-free frame sampling foundation

**Checkpoint 1 (2026-10-31): planning and synthetic tests only.** The
`VideoFrameSamplingPlanner` in Core computes deterministic seek timestamps
from already-known duration and optional *externally verified* scene boundaries.
It neither opens nor probes an original, invokes FFmpeg, scans OneDrive,
produces captions/embeddings, persists evidence, changes photo processing or
activates a new worker.

## Sampling contract

- Version: `video-frame-sampling-v1`. A future frame-evidence job must bind
  this policy to the existing `VideoDerivativeKind.FrameEvidence` identity,
  the current source revision, model SHA-256 and preprocessing version.
- Modes: evenly distributed interval midpoints, or scene-balanced timestamps
  from externally supplied, monotonically increasing scene boundary positions.
  No scene analysis is assumed to be implemented by this planner.
- Defaults: 12 requested frames, 2 per scene, 24 seconds *reserved estimated*
  decode time at 1 second per frame. Hard ceilings: 24 frames, 48 seconds
  estimated decode reservation, 128 scene boundaries, 1-hour inspected
  duration, 250 ms minimum separation. Very dense/short scenes yield fewer
  samples, never more; long clips are sampled across their entire timeline.
- Real FFmpeg wall-clock time, seek/decode latency, decoded pixels, GPU memory
  and scratch usage are **not bounded by this pure plan**. An eventual
  decoder/worker must apply independent process, frame, time, memory, temporary
  storage and cancellation guards before accessing any media.
- Missing scene candidates are not evidence of missing events. Sampled frames
  cannot establish motion, actions between frames, speech or audio content.
  Low-light/difficult frames may yield no reliable visual description.

## Developer check (no media or database)

```powershell
dotnet test tests/PhotoIdentity.Core.Tests/PhotoIdentity.Core.Tests.csproj -c Release --filter FullyQualifiedName~VideoFrameSamplingPlannerTests
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
```

A later WI-0190 implementation must still deliver controlled local decoding,
compatible still-photo caption/CLIP preprocessing, schema and current-evidence
read/write fencing, exclusion/stale/partial-result handling, synthetic and
maintained-host decode/relevance/cost evidence. The work item's four acceptance
criteria remain **open**. No personal media, paths, model results or private
evidence belong in the PR.

## Checkpoint 2 — supervised *synthetic* SDR frame extraction and evidence gate

An explicit `video synthetic-frame-pilot` CLI command generates two public
3-second test clips (landscape with audio, portrait without) using the already
approved synthetic-proxy fixture builder. It passes the sampling plan to a
**fixed** FFmpeg JPEG extraction argument profile, runs each process through
the existing SHA-pinned Windows Job Object supervisor, and decodes the bounded
result using the **same OpenCvImageDecoder BGR24 contract used for photos**.
The tool uses a 640px maximum long edge, accurate post-input seek,
`-autorotate`, no audio/subtitle/data maps, 2 MiB per JPEG, and independent
image dimensions/format validation. Temporary files disappear with the
supervisor's workspace even when a process fails or is cancelled. No file
path for a real media source can be passed to this command.

This *tests the shared image decode boundary*, **not** ONNX CLIP inference,
Ollama caption generation, caption/embedding quality, matching preprocess
version or model weight approval. The exact photo CLIP image preprocessing
contract is `ClipZeroShotTagger.ImagePreprocessingVersion`, and captions use
`PhotoCaptionPrompt.VersionFor(language)`; a later guarded worker must
apply them rather than silently substituting defaults.

`VideoFrameEvidenceContract.Validate` checks an in-memory complete batch
against the explicit expected revision/model hash, sampling policy, extraction
and preprocessing identities and exact planned timestamps. It rejects stale,
excluded or partial batches, non-finite/dimension-mismatched vectors,
invalid captions and missing evidence. **It does not read/write PostgreSQL or
prove its caller's authorization.** A future durable publisher must repeat
the checks inside a revision/exclusion/control-generation/lease-fenced
transaction before making results visible. No video search reads these
in-memory batches.

### Local synthetic pilot (optional; no personal media or database)

Run after CI passes on a Windows checkout. Reuse the same operator-approved
FFmpeg executable and SHA-256 from earlier WI-0189 trials. PowerShell:

```powershell
$approved = .\Get-PhotoIdentityApprovedVideoTools.ps1
$ffmpeg = $approved.FfmpegPath
$sha = $approved.FfmpegSha256
$root = Join-Path $env:TEMP ("photoidentity-wi0190-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $root | Out-Null
Set-Content -LiteralPath (Join-Path $root ".photoidentity-synthetic-video-lab") -Value "wi0188-synthetic-video-lab-v1" -NoNewline -Encoding ascii

dotnet run --project src/PhotoIdentity.Cli -c Release -- video synthetic-frame-pilot `
  --scratch-root $root --ffmpeg $ffmpeg --ffmpeg-sha256 $sha

# Only after reviewing the dry-run root and verifying it contains just the marker:
dotnet run --project src/PhotoIdentity.Cli -c Release -- video synthetic-frame-pilot `
  --scratch-root $root --ffmpeg $ffmpeg --ffmpeg-sha256 $sha --apply

# Expect status=validated, 2 cases, 4 frames each, imagesPersisted=false.
# Confirm only the ownership marker remains. Do not delete an unverified root.
Get-ChildItem -LiteralPath $root -Force
```

Tests without FFmpeg or media:

```powershell
dotnet test tests/PhotoIdentity.Core.Tests/PhotoIdentity.Core.Tests.csproj -c Release --filter "FullyQualifiedName~VideoFrameEvidenceContractTests|FullyQualifiedName~VideoFrameSamplingPlannerTests"
dotnet test tests/PhotoIdentity.Integration.Tests/PhotoIdentity.Integration.Tests.csproj -c Release --filter "FullyQualifiedName~VideoFrameExtractionProfileTests|FullyQualifiedName~VideoSyntheticFramePilotTests"
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
```

All WI-0190 acceptance criteria remain open: no real-video extraction, model
inference, persistence, scene detection, transactional invalidation, private
caption quality, storage/clip or CPU/GPU throughput evidence is claimed.

## Checkpoint 3 — actual existing photo models against registered proxies

A separate, manually authorized **read-only** pilot of previously published
WI-0189 local SDR proxies now exercises the existing CLIP and Ollama models
in a transient, model-provenance-checked sequence; it reports aggregate
processing and uncalibrated ranking diagnostics. It does not analyze
originals or persist new evidence. See
[registered proxy frame pilot](video-registered-proxy-frame-pilot.md).
