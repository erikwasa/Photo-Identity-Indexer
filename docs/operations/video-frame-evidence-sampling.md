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
