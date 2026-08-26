# Build context

This file is intentionally a short handoff for the next development or verification session. It should describe only the current focus, the next concrete step and the small set of documents needed to continue.

Formal work-item lifecycle status and evidence are resolved by `PhotoIdentity.Docs` from the current registry plus archived terminal history.

## Current focus

M21 — Reliability and recognition quality is active.

**WI-0079 is complete.** PR #208 merged after workflow #1259 and real-catalogue verification. The no-change repeat sync fell from 37.83 seconds to 0.988 seconds by batching persistence and reusing metadata-stable verified revisions instead of re-hashing all local files.

The active item is now **WI-0080 — Make the detected face unambiguous in review images**.

Static investigation found that review images intentionally use the durable `face-review-v1-context2.2-max960-q90` derivative: a square crop centered on the detected face with a 2.2× context scale. A nearby second face can therefore legitimately appear inside the same review image.

The authoritative detector bounding box still exists server-side and can be deterministically mapped into the review derivative, but `ReviewFaceResponse` currently exposes only `ImageUrl` and review metadata, so the browser cannot identify the target face visually.

The preferred candidate is a **dynamic target overlay** on the existing derivative: expose a privacy-safe normalized target rectangle in derivative coordinates and render a high-contrast outline/corner marker plus a non-color target cue in the Web UI. This preserves the existing high-quality contextual crop and avoids regenerating/burning presentation into stored derivatives.

The maintainer approved **Option B** on 2026-08-26: preserve the contextual derivative and add a dynamic target overlay. Implementation is active on PR #209.

WI-0081 remains the next M21 item after WI-0080 unless new evidence changes priority. WI-0076/PR #200 remains separate archive-throughput work.

## Next concrete step

1. Complete required CI for PR #209.
2. Verify the packaged build on a private real-catalogue example where two faces are visible in the same contextual review image.
3. Confirm the **Target** outline/corner cue is unmistakable in the gallery and Face Details and does not obscure review actions.
4. If maintainer verification passes, record evidence, complete WI-0080 and continue to WI-0081.

## Relevant files

- `docs/delivery/milestones/M21-reliability-recognition-quality.md`
- `docs/delivery/work-items/WI-0080-detected-face-clarity.md`
- `docs/delivery/work-items/WI-0081-suggestion-accuracy-degradation.md`
- `src/PhotoIdentity.Imaging.OpenCv/OpenCvReviewFaceRenderer.cs`
- `src/PhotoIdentity.Worker/ArchiveFaceReviewDerivativeWriter.cs`
- `src/PhotoIdentity.Api/ReviewFacePreviewResolver.cs`
- `src/PhotoIdentity.Web/ReviewContracts.cs`
- `src/PhotoIdentity.Web/Components/FaceCard.razor`
- `src/PhotoIdentity.Web/Pages/FaceDetails.razor`
- `docs/delivery/status/work-items.yaml`
- `AGENTS.md`

## Repository validation

```powershell
./build.ps1
./test.ps1
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
./verify-review.ps1 -Mode Smoke -Configuration Release
```
