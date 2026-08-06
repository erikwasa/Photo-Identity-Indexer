---
id: WI-0037
title: Evaluate another face detector
milestone: M16
status_source: ../status/work-items.yaml
depends_on: [WI-0036]
affected_modules: [Models, PhotoIdentity.Recognition.Onnx, Evaluation]
---

# WI-0037: Evaluate another face detector

## Objective

Evaluate a governed detector candidate after the fixed and multi-scale YuNet options remained below the M16 target.

## Activation

WI-0036 completed on 2026-08-07 without an acceptable YuNet configuration:

- multi-scale confidence `0.9` improved on relevant earlier YuNet runs but failed the complete gate; and
- multi-scale confidence `0.7` returned more than 100 false or duplicate detections, so confidence `0.6` was intentionally not run.

WI-0037 is active on `agent/WI-0037-centerface-qualification`.

## First candidate decision

The first qualification target is the upstream **CenterFace ONNX** model committed as `models/onnx/centerface.onnx` at `Star-Clouds/CenterFace@b82ec0c4844e89fd5a0305986aed9bdf33c72585`.

CenterFace is selected ahead of SCRFD for this first implementation increment because:

- it provides five facial landmarks and a direct ONNX artifact suitable for the existing ONNX Runtime deployment path;
- its repository carries an MIT licence without the explicit non-commercial pretrained-model restriction stated by InsightFace for SCRFD weights; and
- its compact graph offers a bounded CPU-oriented candidate before considering heavier or more encumbered alternatives.

This selection does **not** approve the model for production or redistribution. The root repository licence, exact committed model artifact and WIDER FACE training-data limitations must be recorded separately. Promotion remains blocked if the model-weight rights cannot be defended for the intended use.

See the [face detector candidate registry](../../models/face-detector-candidate-registry.md) for the screened alternatives and trade-offs.

## First implementation increment

Before processing the private sample:

1. pin the exact `centerface.onnx` binary, byte size and SHA-256 in a new immutable detector manifest;
2. record the upstream repository revision, root licence, model-weight interpretation and training-data limitation;
3. freeze the preprocessing and decoder contract from the pinned upstream implementation;
4. validate the graph and tensor contract under the project's ONNX Runtime version;
5. implement and test the CenterFace adapter without changing `sface-five-point-v1`; and
6. run a bounded Windows CPU smoke test before authorising the full 100-photo comparison.

The pinned upstream reference implementation rounds input dimensions up to multiples of 32, creates an RGB float32 tensor with scale `1.0` and zero mean, and decodes heatmap, scale, offset and five-landmark outputs at stride four before NMS. These semantics must be independently verified against the exact ONNX graph rather than copied by assumption.

## Scope

- Select a candidate with an acceptable licence, provenance and local runtime path.
- Pin exact model identity, hash, preprocessing and output semantics.
- Adapt landmarks or alignment inputs without weakening the SFace contract.
- Compare the candidate on the exact WI-0034 sample.
- Include recall by category, false detections, runtime and review effort.

## Acceptance criteria

- [ ] Candidate licensing and training-data limitations are documented.
- [ ] Exact model and pipeline provenance is immutable.
- [ ] The comparison uses unchanged source photos and ground truth.
- [ ] No score automatically becomes a canonical person label.
- [ ] A human-reviewed recommendation identifies the first acceptable pipeline.

## Evaluation boundary

Do not run CenterFace on the complete private sample until the exact artifact manifest, adapter tests and Windows smoke verification are complete.

When the candidate becomes runnable, keep unchanged:

- all 100 source photos and source hashes;
- the frozen face-level ground truth and countable-face rule;
- comparison IoU and category metadata;
- SFace model, alignment protocol and padding; and
- the canonical reviewed catalogue.

Use a new isolated candidate catalogue, output directory and log. Persist exact detector ID, hash, confidence, input-shape policy, preprocessing, decoder and NMS provenance.

## Gate

The first candidate meeting the complete M16 target continues to WI-0038. If CenterFace fails or cannot clear governance, record the remaining gap and select the next candidate from the registry before expanding the search.
