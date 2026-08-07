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

WI-0037 was activated through PR #85. PR #86 added the first runnable CenterFace adapter, PR #87 surfaced durable batch failure reasons, PR #88 moved the pinned graph to the upstream-compatible OpenCV DNN runtime, PR #89 corrected N-D tensor marshalling, and PR #90 isolated OpenCV network state per image after human smoke review exposed cross-image corruption. PR #91 recorded the corrected repeat-smoke pass.

## Candidate decision

The selected qualification target is the upstream **CenterFace ONNX** model committed as `models/onnx/centerface.onnx` at `Star-Clouds/CenterFace@b82ec0c4844e89fd5a0305986aed9bdf33c72585`.

CenterFace was selected ahead of SCRFD for this evaluation because:

- it provides five facial landmarks and a compact local ONNX artifact;
- its repository carries an MIT licence without the explicit non-commercial pretrained-model restriction stated by InsightFace for SCRFD weights; and
- it offered a bounded CPU-oriented candidate before considering heavier or more encumbered alternatives.

The project records the repository MIT licence as provisional weight evidence and separately records that WIDER FACE training-data rights are not asserted. On 2026-08-07 the maintainer explicitly accepted that documented uncertainty for **local evaluation** and separately instructed WI-0038 local rollout engineering to proceed. That instruction does not establish a right to redistribute the pretrained weights and does not remove the recorded production/redistribution caveat.

See the [face detector candidate registry](../../models/face-detector-candidate-registry.md) for the screened alternatives and trade-offs.

## Exact artifact verification

The maintainer ran `models/inspect-centerface-candidate.ps1` on Windows on 2026-08-07. The exact artifact was verified as:

- byte size `7,532,772`;
- Git blob SHA-1 `1487d5fe214feb569865b225216b24c8f4ef1050`; and
- SHA-256 `77e394b51108381b4c4f7b4baf1c64ca9f4aba73e5e803b2636419578913b5fe`.

Those bytes remain pinned by the immutable `centerface-2019-fp32` manifest.

## Runtime qualification

The disposable five-image smoke exposed three implementation/runtime issues without changing the candidate configuration:

1. ONNX Runtime rejected the graph's stale static input metadata.
2. OpenCV DNN executed the graph but the first adapter could not marshal four-dimensional outputs correctly.
3. After marshalling was corrected, reuse of one OpenCV `Net` across images caused severe cross-image corruption: a plausible first result was followed by hundreds of nonsensical detections on later images.

PR #90 changed CenterFace to create and dispose a fresh OpenCV `Net` for every image. The exact model bytes, confidence `0.5`, preprocessing, decoder, NMS, landmark mapping, SFace and padding remained unchanged.

Windows CI for the corrected PR head passed. The maintainer then repeated the same five disposable images and reported that face outputs matched the source images consistently on every image. The repeat run ID was not supplied to the repository evidence and is therefore not invented.

The runtime-stability smoke gate was cleared without using the disposable smoke set for threshold selection.

## Runnable implementation

The selected implementation includes:

- an explicit fixed-versus-dynamic input-shape contract without changing existing fixed YuNet or SFace manifests;
- a CenterFace model manifest with a `dynamic-multiple-of` policy, multiple `32` and pre-round maximum long edge `1600`;
- RGB float32 preprocessing with scale `1.0`, zero mean and bounded source-dependent tensor dimensions;
- OpenCV DNN execution of the pinned ONNX graph, matching the upstream reference runtime for variable image sizes;
- fresh OpenCV network state for each image inference;
- required outputs `537`, `538`, `539` and `540`;
- deterministic heatmap, scale, offset and five-landmark decoding at stride four;
- deterministic IoU `0.30` NMS;
- explicit native landmark mapping into the unchanged `sface-five-point-v1` contract;
- exact detector-adapter selection by model ID in the local batch worker; and
- refusal to combine CenterFace with the YuNet-only `full-image-plus-tiles` pipeline.

Synthetic tests cover manifest provenance, dynamic-size preprocessing, BGR-to-RGB conversion, bounded tensor sizing, decoder geometry, landmark order, strict confidence thresholding, NMS and malformed output shapes.

See [CenterFace qualification](../../models/centerface-2019-qualification.md) and the [CenterFace detector runbook](../../operations/centerface-detector-runs.md).

## Governed M16 candidate

The complete M16 comparison used the predeclared candidate without parameter changes:

- detector `centerface-2019-fp32`;
- runtime `opencv-dnn` with per-image network isolation;
- confidence `0.5`;
- pipeline `single-pass`;
- manifest-bound source long edge `1600` before multiple-of-32 rounding;
- deterministic CenterFace NMS `0.30`;
- embedder `sface-2021dec-fp32`;
- padding `0.25`; and
- the unchanged WI-0034 source photos, hashes, frozen ground truth, countable-face rule, comparison IoU and category metadata.

PR #92 added a narrow `Neutral` comparison outcome for legitimate face detections that were intentionally outside the frozen countable-face scope. Neutral resolves review workload but does not add a matched face and therefore cannot improve recall; it is also excluded from the false-plus-duplicate penalty. This preserves the fixed counting rule while avoiding an artificial penalty for useful out-of-scope face detections.

## Final evaluation decision

On 2026-08-07 the maintainer completed the governed private 100-photo CenterFace comparison and reported that it **passed the complete M16 gate**. Detailed private recall, category and review data remain outside Git.

The privacy-safe conclusion is:

- overall recall met or exceeded `90%`;
- recall on photos with at least five countable faces met or exceeded `85%`;
- false plus duplicate detections remained at or below `10` after human review under the fixed counting rule; and
- no material archive-workflow failure category remained.

CenterFace confidence `0.5` single-pass is therefore the first accepted detector pipeline from M16 and advances to [WI-0038](WI-0038-detector-rollout.md) for safe local-catalogue rollout engineering.

This selection does not permit ordinal-based replacement of existing reviewed face occurrences. WI-0038 must preserve people, assignments, rejections and append-only review history while reconciling the changed face population by geometry and landmarks.

## Scope

- Select a candidate with an acceptable documented local-evaluation boundary, provenance and runtime path.
- Pin exact model identity, hash, preprocessing and output semantics.
- Adapt landmarks or alignment inputs without weakening the SFace contract.
- Compare the candidate on the exact WI-0034 sample.
- Include recall by category, false detections, runtime and review effort in the private evidence.

## Acceptance criteria

- [x] Candidate licensing and training-data limitations are documented.
- [x] Exact model and first-candidate preprocessing provenance is pinned by model hash, checked-in manifest and implementation revision.
- [x] The comparison uses unchanged source photos and ground truth.
- [x] No score automatically becomes a canonical person label.
- [x] A human-reviewed recommendation identifies the first acceptable pipeline.

## Completion boundary

WI-0037 is complete. Preserve the private comparison, candidate catalogue, exported summaries and run log as M16 evidence.

Any broader redistribution or deployment decision remains subject to the recorded pretrained-weight/training-data uncertainty. The next engineering boundary is WI-0038: create a versioned detector-pipeline identity, reconcile detections without ordinal-only remapping, route ambiguity/new faces through review, reprocess the pilot safely and retain a rollback path.
