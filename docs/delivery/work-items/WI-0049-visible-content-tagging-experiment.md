---
id: WI-0049
title: Experiment with visible-content image tagging
milestone: M19
status_source: ../status/work-items.yaml
depends_on: [WI-0042]
related_adrs: []
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Imaging.OpenCv, PhotoIdentity.Persistence.Sqlite]
---

# WI-0049: Experiment with visible-content image tagging

## Objective

Evaluate practical local approaches for tagging whole photos with visible content such as beach, party, playing volleyball or watching television, and produce evidence for whether and how this should become a production capability.

## Why

Identity alone is not enough for useful photo-library discovery. Semantic tags could enable richer browsing and collections, but model quality, taxonomy, runtime, storage, privacy and redistribution implications should be measured before committing to an architecture.

## In scope

- Define a small representative private evaluation sample and useful tag vocabulary/activity examples.
- Evaluate one or more local image-semantic approaches without sending private photos to external services.
- Measure practical precision/usefulness, missed concepts, confusing concepts, runtime and storage footprint.
- Compare fixed-vocabulary tagging with more flexible semantic/caption-style approaches where feasible.
- Record model provenance, licence/redistribution constraints and hardware/runtime requirements.
- Prototype a neutral representation for model-produced image-tag evidence if needed to test integration.
- Produce a recommendation: reject, continue experimenting, or select an approach for a later production implementation.

## Out of scope

- Requiring a production tagging model to ship from this work item.
- Building the final smart-collection UI.
- Uploading the private archive to a third-party vision API.

## Acceptance criteria

- [ ] A bounded representative tag experiment can be reproduced locally.
- [ ] The experiment includes object/scene and activity-style concepts relevant to the intended library use.
- [ ] Quality, runtime and storage findings are recorded without committing private photo content.
- [ ] Candidate model provenance/licensing and redistribution constraints are documented.
- [ ] A clear production recommendation and next implementation boundary are recorded.

## Verification requirements

Automated smoke tests for any prototype pipeline plus maintainer review of privacy-safe aggregate experiment results on private photos.

## Completion notes

- Files changed:
- Trade-offs:
- Deferred work: production tag generation and collection integration unless explicitly selected as part of follow-on work
- Commands run:
