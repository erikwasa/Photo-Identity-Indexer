---
id: WI-0155
title: Evaluate face discovery and identity processing inside video
milestone: M30
status_source: ../status/work-items.yaml
depends_on: [WI-0151, WI-0188]
related_adrs: []
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Recognition.Onnx, PhotoIdentity.Api, PhotoIdentity.Persistence.Postgres, docs]
---

# WI-0155: Evaluate face discovery and identity processing inside video

## Objective

Evaluate whether sampled video frames can provide useful face evidence without exploding compute, storage or duplicate review work.

## Why

Doing the same identity processing as photos is not a simple container extension because adjacent frames repeatedly contain the same faces.

## In scope

- Prototype bounded, timestamped frame sampling inside the isolated WI-0188 video lane.
- Measure yield, duplicate-frame burden, compute and storage.
- Evaluate face tracks with start/end timestamps and representative high-quality observations to avoid one review item per near-identical frame.
- Separately measure face detection, within-clip tracking and *known-person* identity suggestions using existing confirmed canonical identities, retaining existing assignment/review rules.
- Keep experimental evidence derived/versioned.
- Record a go/no-go and smallest production scope.

## Out of scope

- Making video face processing mandatory for basic support.
- Live-camera recognition.
- Weakening governed assignment boundaries.

## Acceptance criteria

- [ ] Evaluation reports recall, known-person matching accuracy/false-positive rate, unknown outcomes, duplicate reduction and compute/storage cost on representative private clips.
- [ ] The design avoids hundreds of review items for one continuous face track.
- [ ] A governed, explicit opt-in production recommendation or no-go is documented, without unreviewed canonical identity assignments.

## Verification requirements

Controlled local experiment and maintainer review; no-go is valid.

## Completion notes

- Files changed:
- Trade-offs:
- Deferred work:
- Commands run:
