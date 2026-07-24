---
id: WI-0003
title: Define core identifiers and contracts
milestone: M00
status_source: ../status/work-items.yaml
depends_on: [WI-0002]
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Core.Tests]
---

# WI-0003: Define core identifiers and contracts

## Objective

Create application-owned identifiers, geometry, landmarks, embeddings, model descriptors and recognition/source contracts without infrastructure types.

## Acceptance criteria

- [x] Strong IDs cannot be interchanged accidentally.
- [x] Geometry validates dimensions and coordinate spaces.
- [x] IoU and vector behaviour are unit-tested.
- [x] Core has no EF Core, OpenCV, ONNX Runtime, Azure SDK or Graph dependency.

## Implemented surface

- strongly typed entity, model and alignment identifiers
- separate pixel and normalised geometry types
- bounding-box conversion and intersection-over-union
- five-point face landmarks
- immutable neutral image buffers
- immutable embedding vectors with cosine similarity
- model descriptors with hashes and model metadata
- source, staging, decoding, detection, alignment, embedding and matching ports

## Verification

Pull request [#3](https://github.com/erikwasa/Photo-Identity-Indexer/pull/3) contains the implementation.

GitHub Actions run [30130764843](https://github.com/erikwasa/Photo-Identity-Indexer/actions/runs/30130764843) successfully restored, built and tested the solution on Windows with .NET 10.

Human review and merge are still required before this work item is completed.
