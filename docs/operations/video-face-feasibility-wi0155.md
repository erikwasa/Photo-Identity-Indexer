# WI-0155 — isolated offline video-face feasibility checkpoint

This experimental tool lives on the long-running `wi-0155` integration branch,
not on `main`. Feature PRs target `wi-0155` until the maintainer chooses a
small production slice or closes the feasibility experiment.

## Scope and limits

The prototype accepts **one private JSON manifest** naming **1–12 already
extracted JPEG/PNG frames** from one expressly selected clip, with relative
timestamps in strictly increasing order, maximum 10 minutes. Input frames
must be ordinary files immediately beside the manifest, at most 8 MiB each.
The program reuses the repository's governed, SHA-verified
`yunet-2023mar-fp32` and `sface-2021dec-fp32` models and existing
OpenCV alignment pipeline; no detector training or new external inference
provider is used.

It prints only aggregate counts and elapsed milliseconds, including a
**heuristic**, one-to-one count of potentially repeated faces across
consecutive sampled frames no more than five seconds apart. Similarity
threshold 0.80 is **not calibrated for identity**, not video tracking and
not a known-person match. No recognition quality is claimed before a
human-approved fixture pilot.

The project has **no PostgreSQL, worker, API, OneDrive or video-file
dependency**. It reads only the manifest, explicitly named still frames
and already-installed approved models. It does **not** create crops,
embeddings on disk, identity assignments, catalogue writes, or any
automatic video source or OneDrive hydration. Outputs exclude file
paths, face images, vectors, person IDs and identifiers. Repeated
observations are aggregated, not published.

This is **not** yet a video face-recognition feature. Known-person
suggestions, representative stills, sensitivity comparisons and any
production persistence must be decided after a private human pilot.
An offline model test cannot establish photo/video identity accuracy.

## Windows checks without personal media

On a clean checkout of the experimental feature branch:

```powershell
cd "C:\Kod\codex\Photo Identity Indexer"
git fetch origin
git switch wi-0155-offline-frame-feasibility
git pull --ff-only origin wi-0155-offline-frame-feasibility

dotnet test tests/PhotoIdentity.VideoFaceFeasibility.Tests/PhotoIdentity.VideoFaceFeasibility.Tests.csproj -c Release

dotnet run --project tools/PhotoIdentity.VideoFaceFeasibility -c Release -- --help

dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
```

The tests use synthetic byte fixtures, fake detection/embedding models and
temporary folders. They do not need installed ONNX models, local
PostgreSQL or private videos. Passing them does not prove real face detection
or recognition.

## Explicit optional local model pilot (not required for first PR)

**Only when deliberately approved by the maintainer**, create a private
directory *outside the repository*, containing a JSON file and up to
12 JPEG/PNG stills extracted from one already-local, approved clip.
The tool never extracts frames from a video by itself. Do not supply an
online-only original. Make sure the stills have the desired portrait
orientation and SDR colors before starting. For example:

```text
%LOCALAPPDATA%\PhotoIdentity\Temp\wi0155\clip-01\
  selected-frames.json
  frame-01.png
  frame-02.png
```

```json
{
  "schemaVersion": 1,
  "frames": [
    { "timestampMs": 1000, "file": "frame-01.png" },
    { "timestampMs": 3000, "file": "frame-02.png" }
  ]
}
```

After independently installing the governed models, run from the
repository root (replace the private path with your actual approved
manifest; never commit it):

```powershell
$privateManifest = Join-Path $env:LOCALAPPDATA "PhotoIdentity\Temp\wi0155\clip-01\selected-frames.json"

dotnet run --project tools/PhotoIdentity.VideoFaceFeasibility -c Release -- `
    --manifest $privateManifest `
    --root (Get-Location).Path
```

The summary is suitable for sharing **only if free of other identifying
information**. Keep the original video, selected frames and review notes
private. No real-media pilot is requested for this first PR. Do not run
against the maintained catalogue or modify Photo Identity settings.

## Review plan before recommending production work

Use a few deliberately selected frames and record privately:
the number of visible faces versus detections, obvious misses, repeated
detections and ambiguous faces; overall elapsed time; whether the
selected frame still offers a useful person view. Only later consider an
approved, **read-only** private known-person reference comparison.
No identity labels, inferred names or biometric evidence are to be
published in source control.

Keep the experimental tool on `wi-0155` while checkpoints accumulate.
A human decision will determine whether any subset should be ported
to production and merged into `main`.
