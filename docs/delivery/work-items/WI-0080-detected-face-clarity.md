---
id: WI-0080
title: Make the detected face unambiguous in review images
milestone: M21
status_source: ../status/work-items.yaml
depends_on: [WI-0038, WI-0058]
related_adrs: []
affected_modules: [PhotoIdentity.Api, PhotoIdentity.Imaging.OpenCv, PhotoIdentity.Persistence.Sqlite, PhotoIdentity.Web, PhotoIdentity.Worker]
---

# WI-0080: Make the detected face unambiguous in review images

## Priority

**High.** Investigate after the critical included-folder synchronization issue.

## Problem statement

A face-detection/review image can sometimes contain **two visible faces**. When that happens, the current presentation does not make it sufficiently clear which face occurrence the detector actually selected and which embedding/suggestion/review action belongs to.

This creates an operator-confidence problem even if the persisted bounding box and identity evidence are technically correct: the maintainer should never have to guess which person in the displayed image is the review target.

## Investigation objective

Trace the exact geometry available from detection through derivative/proxy generation and review rendering, reproduce representative ambiguous examples, and select a presentation that unambiguously identifies the detected face without damaging image quality or review throughput.

No UX solution is selected by this document until the maintainer approves one of the treatments compared below.

## Investigation questions

- Which image is currently displayed in Face Review and Face Details for one face occurrence: aligned crop, expanded context crop, review proxy region, or another derivative?
- Is the original detector bounding box/landmark geometry still available in the API/UI contract at render time?
- Under what crop-expansion or alignment conditions can a neighboring face enter the displayed review image?
- Does ambiguity occur only for close/group faces or also because the displayed derivative is substantially wider than the detected region?
- Would the clearest operator cue be a target outline/box, a dimmed non-target area, tighter crop, target marker, or another treatment?
- Can the cue remain clear at gallery thumbnail size as well as the larger Face Details image?
- How should accessibility work so the target is not communicated by color alone?
- Should the visual indication be rendered dynamically from detection coordinates or burned into a dedicated derivative? Prefer preserving reusable source derivatives unless evidence favors otherwise.

## Safety and semantic constraints

- The cue must identify the existing detected face occurrence; it must not change identity evidence merely for presentation.
- Do not overwrite or mutate original photos.
- Do not create a second face occurrence just because another face is visible in the context crop.
- Preserve the current high-quality face derivative work from WI-0058.
- Preserve review ordering, assignment, suggestion and audit semantics.

## Static investigation — 2026-08-26

### What is displayed today

Face Review gallery cards and Face Details both render `ReviewFaceResponse.ImageUrl`. The browser contract currently contains the image URL, photo name, face ordinal, confidence and review/person state, but **no face bounding box or landmark geometry**.

`ReviewFacePreviewResolver` prefers the durable face-review derivative. When that derivative is unavailable it loads the persisted latest face observation geometry and renders the same contextual crop from the privacy-safe whole-photo review proxy. The target bounding box is therefore still authoritative server-side, but it is discarded before the browser renders the image.

### Why a second face can appear

`OpenCvReviewFaceRenderer` intentionally creates a face-centered square review image with:

```text
ContextScale = 2.2
```

The crop side is `max(face width, face height) × 2.2`, centered on the detected face and shifted only as necessary to remain inside the photo. This gives useful hair/head/background context for human review, but a nearby face can legitimately fall inside the contextual square.

The durable derivative profile explicitly records this policy as:

```text
face-review-v1-context2.2-max960-q90
```

So the ambiguous second face is not evidence that the wrong occurrence was persisted. It is an expected consequence of the contextual review crop when faces are close enough together.

### Geometry mapping feasibility

The persisted detector bounding box can be mapped deterministically into the rendered derivative:

1. convert the normalized source-photo bounding box to source pixels;
2. apply the same 2.2× square `CalculateCrop` geometry used by `OpenCvReviewFaceRenderer`;
3. subtract the crop origin from the face coordinates;
4. divide by crop width/height to produce a normalized target rectangle in derivative coordinates.

The derivative is resized uniformly and is never upscaled, so these normalized target coordinates remain valid at gallery and Face Details display sizes.

This means the existing JPEG derivative can stay reusable and unmodified. The API can expose a small normalized target rectangle alongside `ImageUrl`, and the Web UI can render the cue dynamically.

## Candidate treatments

### Option A — tighter context crop

Reduce the 2.2× context scale so less neighboring content is visible.

**Advantages**

- Small implementation surface in the renderer.
- No overlay geometry/UI contract required.

**Disadvantages**

- Removes useful review context preserved by WI-0058.
- Does not guarantee uniqueness when faces overlap or are very close.
- Requires a new durable derivative profile and regeneration/backfill if existing stored images are to change.
- Solves ambiguity indirectly rather than identifying the target explicitly.

**Assessment:** not preferred as the primary fix.

### Option B — dynamic target outline/marker on the existing derivative

Expose the target bounding rectangle in normalized derivative coordinates and render it over the existing image in the browser.

Suggested presentation:

- a high-contrast rectangular outline around the detected face;
- emphasized corner brackets so the cue survives thumbnail scaling;
- a compact `Target`/`Detected face` label or equivalent non-color cue;
- optional subtle dimming outside the target rectangle if the outline alone is insufficient in real photos;
- accessible image text that explicitly states that the marked rectangle is the detected/review target.

**Advantages**

- Directly answers which visible face is being reviewed.
- Reuses current high-quality durable derivatives unchanged.
- Works at gallery and Face Details sizes from the same normalized geometry.
- Can be adjusted in CSS without regenerating biometric/review derivatives.
- Does not alter recognition evidence or crop semantics.

**Disadvantages**

- Requires an explicit API/Web contract addition for target geometry.
- Needs careful CSS for very small/edge-touching faces and accessibility testing.

**Assessment:** preferred.

### Option C — burn the target cue into a new derivative profile

Generate a new marked JPEG derivative containing the outline/marker in pixels.

**Advantages**

- Every consumer automatically receives an unmistakably marked image.
- No browser overlay positioning logic.

**Disadvantages**

- Requires a new derivative profile and regeneration/backfill.
- Mixes presentation with reusable review pixels.
- Harder to evolve marker appearance/accessibility without regenerating files.
- Creates another derivative variant for a problem that can be solved from retained geometry.

**Assessment:** not preferred unless dynamic overlay proves unreliable.

## Recommended direction for maintainer approval

Select **Option B — dynamic target overlay**, while preserving the current 2.2× contextual derivative.

Implementation shape after approval:

1. Extract/share the contextual-crop calculation so API geometry mapping and image rendering cannot drift.
2. Add a privacy-safe normalized target rectangle to the review response contract. Do not expose source paths or original pixels.
3. Create one reusable Web component for a review image plus target overlay rather than duplicating positioning logic across surfaces.
4. Use it first in the standard Face Review gallery and Face Details; also apply it to suggestion review cards that display the same review face image where practical so target semantics stay consistent.
5. Keep the cue visible without relying on color alone: outline + corner geometry + accessible target wording.
6. Add geometry unit tests and Web/integration contract coverage for a context crop where a second face would be visible.
7. Verify visually with at least one private real-catalogue example without committing the photo.

No product-code implementation should begin until the maintainer approves this treatment.

## Investigation acceptance criteria

- [ ] At least one representative image with two visible faces in the review image is captured/reproduced without committing private photo data.
- [x] The exact target face bounding box geometry and its coordinate space are traced from persisted detection through derivative generation. The browser contract currently drops that geometry.
- [x] The reason the second face becomes visible in the review image is understood: the intentional 2.2× contextual square can include nearby faces.
- [x] Candidate target-indication approaches are compared for gallery-size clarity, Face Details clarity, accessibility and implementation complexity.
- [ ] The selected approach makes the target unmistakable without requiring the operator to infer it from suggestion text or face position.
- [ ] The maintainer approves the visual treatment before implementation.
- [x] The eventual implementation plan includes regression coverage for a context crop containing more than one visible face.

## Source finding

During the 2026-08-26 maintainer verification, the maintainer reported a separate high-priority usability issue: some face-detection images visibly contain two faces, making it unclear which one is the detected/reviewed face. This is a new review-clarity issue rather than a failure of the already-verified M19/M20 navigation or image-quality acceptance checks.
