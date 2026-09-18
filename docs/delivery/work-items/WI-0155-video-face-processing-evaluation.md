---
id: WI-0155
title: Evaluate face discovery and identity processing inside video
milestone: M30
status_source: ../status/work-items.yaml
depends_on: [WI-0151]
related_adrs: []
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Recognition.Onnx, PhotoIdentity.Api, PhotoIdentity.Persistence.Postgres, docs]
---

# WI-0155: Evaluate face discovery and identity processing inside video

## Objective

Evaluate whether sampled video frames can provide useful face evidence without exploding compute, storage or duplicate review work.

## Why

Doing the same identity processing as photos is not a simple container extension because adjacent frames repeatedly contain the same faces.

## In scope

- Prototype bounded frame sampling.
- Measure yield, duplicate-frame burden, compute and storage.
- Evaluate a temporal video-face/track derived concept.
- Keep experimental evidence derived/versioned.
- Record a go/no-go and smallest production scope.

## Out of scope

- Making video face processing mandatory for basic support.
- Live-camera recognition.
- Weakening governed assignment boundaries.

## Acceptance criteria

- [ ] Evaluation reports aggregate yield/duplication/cost.
- [ ] The design avoids hundreds of review items for one continuous face track.
- [ ] A production recommendation or explicit no-go is documented.

## Verification requirements

Controlled local experiment and maintainer review; no-go is valid.

## Completion notes

- Files changed:
- Trade-offs:
- Deferred work:
- Commands run:
