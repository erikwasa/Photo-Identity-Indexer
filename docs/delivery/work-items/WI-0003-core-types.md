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

## Verification

Pull request [#3](https://github.com/erikwasa/Photo-Identity-Indexer/pull/3) was merged after human review.

GitHub Actions run [30131013371](https://github.com/erikwasa/Photo-Identity-Indexer/actions/runs/30131013371) restored, built and tested the final pull-request head on Windows with .NET 10.

Merge commit: `93d58f0de6afbcbcc32fc265a7c1ca79e0941ed6`.
