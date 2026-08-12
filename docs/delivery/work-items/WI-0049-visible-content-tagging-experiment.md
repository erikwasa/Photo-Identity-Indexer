---
id: WI-0049
title: Experiment with visible-content image tagging
milestone: M19
status_source: ../status/work-items.yaml
depends_on: [WI-0056]
related_adrs: []
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Imaging.OpenCv, PhotoIdentity.Recognition.Onnx, PhotoIdentity.Persistence.Sqlite]
---

# WI-0049: Experiment with visible-content image tagging

## Objective

Evaluate practical local approaches for producing automatic visible-content tag evidence for whole photos, using the canonical tag representation established by WI-0056, and produce evidence for whether and how automatic tagging should become a production capability.

## Why

Identity and manual tags alone are not enough for scalable discovery across a large archive. Automatic semantic evidence could enable richer browsing and collections, but model quality, vocabulary behavior, runtime, storage, privacy, packaging and redistribution implications should be measured before committing to an architecture.

Manual assignments are the human-owned source of truth. Automatic output is model evidence with exact provenance and must never overwrite a manual assignment.

## In scope

- Define a small representative private evaluation sample and useful canonical tag vocabulary/activity examples.
- Reuse maintainer-assigned tags from WI-0056 as evaluation evidence where appropriate without committing private photo content or private labels.
- Evaluate one or more local image-semantic approaches without sending private photos to external services.
- Use controlled-vocabulary image/text similarity as the first integration baseline because it maps directly onto canonical tags and can fit the existing ONNX/C# runtime shape.
- Compare a purpose-built image tagger when feasible if it offers materially better object/scene/activity coverage at acceptable runtime and packaging cost.
- Treat generative captioning as optional comparative evidence rather than a required production path.
- Measure practical precision/usefulness, missed concepts, confusing concepts, runtime and storage footprint.
- Record model provenance, licence/redistribution constraints and hardware/runtime requirements.
- Prototype model-produced tag evidence separately from manual assignment history, including exact model revision and score/confidence provenance.
- Produce a recommendation: reject automatic tagging, continue experimenting, or select an approach for a later production implementation.

## Candidate families to investigate

- CLIP/OpenCLIP-style image/text similarity for a bounded canonical vocabulary and activity phrases.
- RAM/RAM++-style purpose-built image tagging if a practical local packaging/runtime path can be demonstrated.
- Florence-2-style captioning only if captions materially improve discovery beyond canonical-tag scoring enough to justify generative runtime and integration complexity.

Candidate names are an experiment starting point, not a production-model decision.

## Out of scope

- Requiring a production automatic tagging model to ship from this work item.
- Replacing or mutating manual tag assignments.
- Building the final smart-collection UI.
- Uploading the private archive to a third-party vision API.
- Generating tag descriptions through an external LLM using private photo content.

## Acceptance criteria

- [ ] A bounded representative tag experiment can be reproduced locally.
- [ ] The experiment includes object/scene and activity-style concepts relevant to the intended library use.
- [ ] At least one controlled-vocabulary approach is measured against the canonical tag representation.
- [ ] Quality, runtime and storage findings are recorded without committing private photo content or private tag labels.
- [ ] Candidate model provenance/licensing, packaging and redistribution constraints are documented.
- [ ] Prototype automatic evidence cannot overwrite manual assignments and records exact model/score provenance.
- [ ] A clear production recommendation and next implementation boundary are recorded.

## Verification requirements

Automated smoke tests for any prototype pipeline plus maintainer review of privacy-safe aggregate experiment results on private photos.

## Completion notes

- Files changed:
- Trade-offs:
- Deferred work: production automatic tag generation and collection integration unless explicitly selected as part of follow-on work
- Commands run:
