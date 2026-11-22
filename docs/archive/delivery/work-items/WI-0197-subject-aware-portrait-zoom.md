---
id: WI-0197
title: Prototype automatic subject-aware portrait enlargement
milestone: M38
status_source: PhotoIdentity.Docs
depends_on: [WI-0196]
related_adrs: []
affected_modules: [PhotoIdentity.Web, PhotoIdentity.Integration.Tests, docs]
---

# WI-0197: Prototype automatic subject-aware portrait enlargement

## Objective

Implement a deterministic, reversible subject-aware framing planner to enlarge suitable portraits on landscape screens while never knowingly clipping recognized face regions.

## In scope

- Pure normalized photo/viewport crop policy with bounded scale, translation, margins, and deterministic output per inputs.
- Reuse identity-free slideshow face geometry; distinguish missing/unverified detection evidence from confirmed empty detections.
- Protect every reliably located face (not only the largest), with conservative refusal for scattered group portraits, edge faces, tightly framed subjects or unreliable coordinates.
- Handle EXIF orientation, 9:16, 3:4, square, landscape, unusual photo/viewport aspect ratios and high-DPI screens.
- Use source-pixel-density budget to reject enlargements that would be visibly too soft on TV; no new image files.
- Explain plan/fallback with non-personal reason codes.

## Out of scope

User-facing TV Focus settings, sequence animation, image edits, extra inference and new persisted crop metadata.

## Acceptance criteria

- [ ] Synthetic geometry tests show no known face is cropped and every crop retains the defined safety margin.
- [ ] Multiple/group, edge/tight, missing, malformed and unverified cases fall back to Standard contain.
- [ ] Reliable zero-face evidence is distinct from absent evidence; no aggressive speculative subject selection.
- [ ] Aspect/viewport/source-resolution checks produce bounded deterministic, testable plans.
- [ ] Existing subtle motion policy and Standard presentation remain unchanged.

## Verification

Automated framing tests plus private side-by-side sample review from WI-0196.

## Completion notes

- Files changed:
- Trade-offs:
- Deferred work:
- Commands run:
