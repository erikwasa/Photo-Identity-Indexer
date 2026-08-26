---
id: WI-0080
title: Make the detected face unambiguous in review images
milestone: M21
status_source: ../status/work-items.yaml
depends_on: [WI-0038, WI-0058]
related_adrs: []
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Web, PhotoIdentity.Worker]
---

# WI-0080: Make the detected face unambiguous in review images

## Priority

**High.** Investigate after the critical included-folder synchronization issue.

## Problem statement

A face-detection/review image can sometimes contain **two visible faces**. When that happens, the current presentation does not make it sufficiently clear which face occurrence the detector actually selected and which embedding/suggestion/review action belongs to.

This creates an operator-confidence problem even if the persisted bounding box and identity evidence are technically correct: the maintainer should never have to guess which person in the displayed image is the review target.

## Investigation objective

Trace the exact geometry available from detection through derivative/proxy generation and review rendering, reproduce representative ambiguous examples, and select a presentation that unambiguously identifies the detected face without damaging image quality or review throughput.

No UX solution is selected by this document. Candidate approaches should be compared in the follow-up investigation before implementation.

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

## Investigation acceptance criteria

- [ ] At least one representative image with two visible faces in the review image is captured/reproduced without committing private photo data.
- [ ] The exact target face bounding box/landmark geometry and its coordinate space are traced from persisted detection to the rendered review image.
- [ ] The reason the second face becomes visible in the review image is understood.
- [ ] Candidate target-indication approaches are compared for gallery-size clarity, Face Details clarity, accessibility and implementation complexity.
- [ ] The selected approach makes the target unmistakable without requiring the operator to infer it from suggestion text or face position.
- [ ] The maintainer approves the visual treatment before implementation.
- [ ] The eventual implementation plan includes regression coverage for a context crop containing more than one visible face.

## Source finding

During the 2026-08-26 maintainer verification, the maintainer reported a separate high-priority usability issue: some face-detection images visibly contain two faces, making it unclear which one is the detected/reviewed face. This is a new review-clarity issue rather than a failure of the already-verified M19/M20 navigation or image-quality acceptance checks.
