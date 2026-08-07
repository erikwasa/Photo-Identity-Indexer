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

WI-0037 was activated through PR #85. PR #86 added the first runnable CenterFace adapter and passed Windows CI. PR #87 made durable batch failure reasons visible after the first real-model smoke exposed a runtime incompatibility.

## First candidate decision

The first qualification target is the upstream **CenterFace ONNX** model committed as `models/onnx/centerface.onnx` at `Star-Clouds/CenterFace@b82ec0c4844e89fd5a0305986aed9bdf33c72585`.

CenterFace is selected ahead of SCRFD for this first implementation increment because:

- it provides five facial landmarks and a compact local ONNX artifact;
- its repository carries an MIT licence without the explicit non-commercial pretrained-model restriction stated by InsightFace for SCRFD weights; and
- it offers a bounded CPU-oriented candidate before considering heavier or more encumbered alternatives.

This selection does **not** approve the model for production or redistribution. The project records the repository MIT licence as provisional weight evidence and separately records that WIDER FACE training-data rights are not asserted. Production promotion remains blocked if those boundaries cannot be defended for the intended use.

See the [face detector candidate registry](../../models/face-detector-candidate-registry.md) for the screened alternatives and trade-offs.

## Exact artifact verification

The maintainer ran `models/inspect-centerface-candidate.ps1` on Windows on 2026-08-07. The exact artifact was verified as:

- byte size `7,532,772`;
- Git blob SHA-1 `1487d5fe214feb569865b225216b24c8f4ef1050`; and
- SHA-256 `77e394b51108381b4c4f7b4baf1c64ca9f4aba73e5e803b2636419578913b5fe`.

Those bytes remain pinned by the immutable `centerface-2019-fp32` manifest.

## Runtime smoke finding

The first five-image disposable smoke run used ONNX Runtime with the predeclared confidence `0.5`, `single-pass` pipeline and bounded multiple-of-32 input policy. Durable run `fbe99826-96ce-44af-b64f-3e6a3b8d93b1` failed all five jobs before detector output.

The stored errors showed that the pinned ONNX artifact declares static input metadata equivalent to `10 x 3 x 32 x 32`, so ONNX Runtime rejected the intended project inputs such as `1 x 3 x 1216 x 1600` and `1 x 3 x 1280 x 1280`.

This is a runtime-contract failure, not evidence about CenterFace recall or confidence. Do not count it as a failed M16 detector candidate and do not change confidence because of it.

The pinned upstream CenterFace Python reference executes the same ONNX graph through OpenCV DNN with source-dependent dimensions rounded to multiples of `32`. The project therefore keeps the exact model bytes, preprocessing, outputs, decoder, NMS, landmark mapping and confidence while changing only the execution runtime from ONNX Runtime to `opencv-dnn`.

## Runnable implementation increment

The current implementation includes:

- an explicit fixed-versus-dynamic input-shape contract without changing existing fixed YuNet or SFace manifests;
- a CenterFace model manifest with a `dynamic-multiple-of` policy, multiple `32` and pre-round maximum long edge `1600`;
- RGB float32 preprocessing with scale `1.0`, zero mean and bounded source-dependent tensor dimensions;
- OpenCV DNN execution of the pinned ONNX graph, matching the upstream reference runtime for variable image sizes;
- required outputs `537`, `538`, `539` and `540`;
- deterministic heatmap, scale, offset and five-landmark decoding at stride four;
- deterministic IoU `0.30` NMS;
- explicit native landmark mapping into the unchanged `sface-five-point-v1` contract;
- exact detector-adapter selection by model ID in the local batch worker; and
- refusal to combine CenterFace with the YuNet-only `full-image-plus-tiles` pipeline.

Synthetic tests cover manifest provenance, dynamic-size preprocessing, BGR-to-RGB conversion, bounded tensor sizing, decoder geometry, landmark order, strict confidence thresholding, NMS and malformed output shapes.

See [CenterFace qualification](../../models/centerface-2019-qualification.md) and the [CenterFace detector runbook](../../operations/centerface-detector-runs.md).

## First governed candidate

After the corrected Windows smoke gate passes, the first complete M16 candidate remains predeclared as:

- detector `centerface-2019-fp32`;
- runtime `opencv-dnn`;
- confidence `0.5`;
- pipeline `single-pass`;
- manifest-bound source long edge `1600` before multiple-of-32 rounding;
- deterministic CenterFace NMS `0.30`;
- embedder `sface-2021dec-fp32`;
- padding `0.25`; and
- the unchanged WI-0034 source photos, hashes, frozen ground truth, countable-face rule, comparison IoU and category metadata.

The corrected smoke must use a fresh catalogue/output root but may use the same disposable five-image smoke set because no detector parameter is being selected from those images. Review the complete confidence-`0.5` M16 result before approving any follow-up configuration.

## Scope

- Select a candidate with an acceptable licence, provenance and local runtime path.
- Pin exact model identity, hash, preprocessing and output semantics.
- Adapt landmarks or alignment inputs without weakening the SFace contract.
- Compare the candidate on the exact WI-0034 sample.
- Include recall by category, false detections, runtime and review effort.

## Acceptance criteria

- [x] Candidate licensing and training-data limitations are documented.
- [x] Exact model and first-candidate preprocessing provenance is pinned by model hash, checked-in manifest and implementation revision.
- [ ] The comparison uses unchanged source photos and ground truth.
- [x] No score automatically becomes a canonical person label.
- [ ] A human-reviewed recommendation identifies the first acceptable pipeline.

## Evaluation boundary

Do not run CenterFace on the complete private sample until:

1. the current OpenCV DNN correction branch builds and tests successfully on the supported Windows toolchain;
2. the exact model installs and re-verifies through the manifest verifier;
3. OpenCV DNN completes bounded source-dependent inference on the disposable smoke images;
4. human smoke review confirms plausible boxes and aligned five-point crops; and
5. the maintainer accepts the documented licence/training-data uncertainty for local evaluation.

When the candidate becomes runnable, use a new isolated candidate catalogue, output directory and log. The durable run configuration records detector model ID, confidence and pipeline, while persisted detections/results carry the detector model hash. Record the exact repository commit with the private candidate log because runtime and bounded input semantics live in the checked-in manifest and adapter implementation.

## Gate

The first candidate meeting the complete M16 target continues to WI-0038. If CenterFace fails after a valid full comparison or cannot clear governance, record the remaining gap and select the next candidate from the registry before expanding the search.
