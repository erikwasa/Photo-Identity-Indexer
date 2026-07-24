---
id: WI-0009
title: Implement SFace embeddings
milestone: M01
status_source: ../status/work-items.yaml
depends_on: [WI-0005, WI-0008]
affected_modules: [PhotoIdentity.Recognition.Onnx]
---

# WI-0009: Implement SFace embeddings

## Objective

Add the SFace ONNX embedder with documented preprocessing, output validation, L2 normalisation and cosine similarity tests.

## Acceptance criteria

- [ ] Embeddings contain finite values and expected dimensions.
- [ ] Normalised vector norms meet tolerance.
- [ ] Same-person fixtures score above selected different-person fixtures.
- [ ] Repeated CPU inference is stable within tolerance.
