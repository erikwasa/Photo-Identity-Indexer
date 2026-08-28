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


## Maintainer decision — Option B

On **2026-08-26**, the maintainer approved **Option B: preserve the existing 2.2× contextual review derivative and add a dynamic target overlay**.

Implementation contract:

1. Keep the existing high-quality contextual derivative and recognition evidence unchanged.
2. Map the persisted detector bounding box into normalized contextual-derivative coordinates using the same crop calculation as `OpenCvReviewFaceRenderer`; do not maintain separate approximate geometry math.
3. Extend the privacy-safe review response with an optional normalized `TargetBox`.
4. Render a reusable browser overlay on normal Face Review, suggestion review cards, and Face Details.
5. The cue must combine a high-contrast rectangle, strong corner brackets, visible **Target** text, and an accessible non-color description.
6. Do not burn the marker into JPEG derivatives or modify originals.
7. If usable target geometry is absent, omit the overlay rather than inventing coordinates.
8. Regression coverage must use a contextual crop capable of containing another visible face/face-like region and prove that the response still identifies only the persisted target detection.

## Implementation slice

PR #209 now implements Option B. The renderer exposes the exact contextual-crop target mapping, review repositories retain the latest persisted bounding-box JSON, and both normal/suggestion review responses expose an optional normalized target rectangle. A reusable `FaceTargetOverlay` renders the target cue without modifying stored pixels.

Regression coverage seeds a neighboring face-like region inside the contextual crop and verifies that Face Details and the normal review gallery return the same normalized target rectangle for the persisted detection.

The PR remains pending until required CI is green and the maintainer verifies at least one private real-catalogue example where two faces are visible in the contextual review image.

## Maintainer verification finding — 2026-08-28

Initial real-catalogue verification against merged PR #209 **failed**. The maintainer confirmed that no
Face Review gallery image or Face Details image showed a target overlay. Face Details also showed no
value for **Photo dimensions** on the tested catalogue.

The failure is an existing-catalogue compatibility gap rather than a need to rerun recognition:

- the permanent archive scan/verification paths historically create `asset_revisions` with
  `width`/`height` left `NULL`;
- the face observation still contains normalized `bounding_box_json`, and durable review derivatives
  and whole-photo review proxies already exist;
- PR #209 calculated `TargetBox` only when `asset_revisions.width` and `height` were populated, so
  existing rows returned `TargetBox = null` and the Web UI correctly omitted the overlay;
- PR #209's integration seed supplied explicit 2400 × 1600 revision dimensions, so it did not cover
  the real-catalogue shape.

### Corrective slice

Branch `agent/WI-0080-existing-catalogue-target-overlay` corrects the compatibility gap without
re-analysis or derivative regeneration:

1. carry the owning asset revision ID through normal and suggestion review records;
2. prefer true persisted photo dimensions for target mapping when available;
3. when those dimensions are absent and the observation is already normalized, use the configured
   whole-photo review proxy's persisted dimensions **only as an aspect-ratio geometry surrogate**;
4. keep `PhotoWidth`/`PhotoHeight` unchanged, so proxy dimensions are never presented as original
   photo dimensions;
5. never reinterpret legacy pixel-space bounding boxes with proxy dimensions;
6. batch review-proxy metadata lookup for a review page; and
7. add integration coverage where original revision dimensions are null while both Face Details and
   the normal gallery must return the same non-null target box with zero original hydration.

Acceptance remains pending required CI and a repeat packaged real-catalogue visual check. The overlay
is expected for every face occurrence with usable geometry, regardless of whether another face was
detected in the contextual image.
