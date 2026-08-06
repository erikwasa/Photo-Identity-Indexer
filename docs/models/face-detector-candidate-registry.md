# Face detector candidate registry

This registry is planning evidence for [WI-0037](../delivery/work-items/WI-0037-detector-candidate.md). It does not activate WI-0037, approve a model, install weights or change the current YuNet pipeline.

The screening was recorded on 2026-08-06 so that a governed alternative-detector evaluation can start without repeating the initial market and provenance review if multi-scale YuNet remains below the M16 target.

## Activation boundary

Use this registry only when WI-0036 has completed and the full-image-plus-tiles YuNet pipeline still fails the complete M16 gate.

Before running any candidate on the private sample, the selected candidate must have:

- an exact model binary selected from an immutable upstream revision;
- locally calculated byte size and SHA-256 recorded in a checked-in manifest;
- separately documented code, model-weight and training-data terms;
- pinned preprocessing, input tensor, output tensor, score and non-maximum-suppression semantics;
- a local Windows runtime path that fails closed when the exact model is unavailable;
- five usable alignment points for both eyes, nose and both mouth corners, without weakening `sface-five-point-v1`; and
- deterministic comparison against the unchanged WI-0034 photos and frozen ground truth.

Published WIDER FACE figures below are prioritisation signals only. They were produced with different image sizes, augmentation and hardware, and are not substitutes for the private M16 comparison.

## Qualification states

- **Conditional candidate** — technically suitable, but a stated licence, model-artifact or provenance condition must be resolved before implementation.
- **Reserve** — technically plausible, but should not consume an evaluation cycle until higher-ranked candidates fail or become unavailable.
- **Hold** — material governance or integration concerns make implementation premature.
- **Screened out** — current evidence does not justify a full WI-0037 run.

## Prioritised registry

| Priority | Candidate | Alignment and runtime fit | Principal blocker | State |
|---:|---|---|---|---|
| 1 | SCRFD-10G_KPS | Five keypoints; documented ONNX Runtime path; strongest efficient-detector signal | InsightFace-provided pretrained models are restricted to non-commercial research unless separately licensed | Conditional candidate |
| 2 | CenterFace ONNX | Five landmarks; compact upstream ONNX files; straightforward CPU-oriented decoder | The repository is MIT, but model-weight and training-data rights are not stated separately enough for automatic promotion | Conditional candidate |
| 3 | YOLO5Face-m | Five-point landmark head; export path; strong single-scale signal | GPL-3.0 implementation and externally hosted weights without separate weight terms | Hold |
| 4 | RetinaFace-R50 | Five landmarks; ONNX ecosystem support; strong difficult-face signal | Same InsightFace pretrained-model restriction as SCRFD, plus higher cost and substantial overlap with SCRFD | Reserve |

## Candidate 1: SCRFD-10G_KPS

**Current decision:** first technical choice if the pretrained-model terms are acceptable for the intended use or a separate licence is obtained.

### Screened source

- repository revision: [`deepinsight/insightface@7fadd420c2351d0ffa8cac403421c1a3ed733365`](https://github.com/deepinsight/insightface/tree/7fadd420c2351d0ffa8cac403421c1a3ed733365)
- model-zoo evidence: [`model_zoo/README.md`](https://github.com/deepinsight/insightface/blob/7fadd420c2351d0ffa8cac403421c1a3ed733365/model_zoo/README.md)
- detector implementation evidence: [`detection/scrfd`](https://github.com/deepinsight/insightface/tree/7fadd420c2351d0ffa8cac403421c1a3ed733365/detection/scrfd)
- licence policy: [`README.md`](https://github.com/deepinsight/insightface/blob/7fadd420c2351d0ffa8cac403421c1a3ed733365/README.md)

### Upstream signal

The upstream model zoo reports `SCRFD_10G_KPS` at 95.40 easy, 94.01 medium and 82.80 hard WIDER FACE AP using single-scale VGA evaluation. It identifies 4.23 million parameters, approximately 10 GFLOPs at VGA and five predicted facial keypoints.

### Advantages

- The five-keypoint variant maps naturally to the current eyes, nose and mouth-corners alignment contract.
- InsightFace documents ONNX Runtime as its current inference backend and supports loading detection-only ONNX models.
- The candidate offers a meaningful architectural change from YuNet while retaining a conventional resize, score, box, landmark and NMS adapter boundary.
- A smaller `SCRFD-2.5G_KPS` variant remains available if the 10G candidate clears recall but fails the Windows runtime or review-effort constraint.

### Risks and blockers

- InsightFace states that its code is MIT but that the training data, annotations and models trained with those data are available for non-commercial research only. The policy explicitly covers both manually and automatically downloaded pretrained models.
- The registry does not yet identify an approved exact ONNX binary. A package model, converted graph or separately licensed artifact must not be treated as interchangeable.
- The multi-head decoder and dynamic input behavior require new tests for tensor order, stride handling, landmark order and deterministic suppression.

### Required before activation

1. Confirm that the project's intended use is permitted by the stated pretrained-model terms, or obtain a separate licence covering the exact detector weights.
2. Select one exact five-keypoint ONNX artifact and pin its source URL, upstream revision, size and SHA-256.
3. Record input dimensions or dynamic-shape policy, colour order, scaling, padding, output names, score semantics and landmark order.
4. Run a bounded Windows CPU smoke benchmark before processing the full private sample.

## Candidate 2: CenterFace ONNX

**Current decision:** best fallback when permissive source licensing is prioritised, subject to an explicit model-weight and training-data review.

### Screened source

- repository revision: [`Star-Clouds/CenterFace@b82ec0c4844e89fd5a0305986aed9bdf33c72585`](https://github.com/Star-Clouds/CenterFace/tree/b82ec0c4844e89fd5a0305986aed9bdf33c72585)
- repository licence: [`LICENSE`](https://github.com/Star-Clouds/CenterFace/blob/b82ec0c4844e89fd5a0305986aed9bdf33c72585/LICENSE)
- primary ONNX artifact: [`models/onnx/centerface.onnx`](https://github.com/Star-Clouds/CenterFace/blob/b82ec0c4844e89fd5a0305986aed9bdf33c72585/models/onnx/centerface.onnx), 7,532,772 bytes in the screened revision
- batch-normalisation-merged alternative: [`models/onnx/centerface_bnmerged.onnx`](https://github.com/Star-Clouds/CenterFace/blob/b82ec0c4844e89fd5a0305986aed9bdf33c72585/models/onnx/centerface_bnmerged.onnx), 7,304,518 bytes in the screened revision
- paper: [CenterFace: Joint Face Detection and Alignment Using Face as Point](https://arxiv.org/abs/1911.03599)

### Upstream signal

The repository reports 92.2 easy, 91.1 medium and 78.2 hard WIDER FACE validation AP for single inference on the original image. Its higher 93.2, 92.1 and 87.3 test figures use multi-scale and flip augmentation and therefore should not be compared directly with a normal single-pass archive run. The paper describes joint box and five-landmark prediction.

### Advantages

- The upstream repository includes compact ONNX files rather than requiring a local export before the first smoke test.
- Five landmarks can satisfy the existing SFace alignment inputs without adding a second landmark model.
- The anchor-free heatmap, scale, offset and landmark outputs are conceptually bounded and suitable for a dedicated ONNX Runtime adapter.
- The repository is MIT licensed and includes OpenCV and Python inference examples.

### Risks and blockers

- The repository licence does not separately state whether the committed pretrained ONNX files and their training-data-derived rights are covered on the same terms. Under this project's governance, repository-level MIT labelling alone is not sufficient evidence for production promotion.
- The repository has seen little maintenance since 2020, increasing the chance of old operator, dynamic-shape or preprocessing assumptions.
- The decoder is unrelated to the current YuNet parser and requires independent validation of heatmap thresholds, stride, scale/offset reconstruction, landmarks and suppression.

### Required before activation

1. Resolve whether the exact committed ONNX file may be used and redistributed under the intended project use, and document the WIDER FACE training-data limitation separately.
2. Choose either the standard or batch-normalisation-merged graph; do not treat them as one revision.
3. Calculate and record SHA-256 locally, even though the Git object and byte size are already pinned by the upstream revision.
4. Confirm tensor compatibility with the project's ONNX Runtime version on Windows and freeze all decoder semantics.

## Candidate 3: YOLO5Face-m

**Current decision:** hold until GPL integration and pretrained-weight provenance are explicitly accepted.

### Screened source

- repository revision: [`deepcam-cn/yolov5-face@152c688d551aefb973b7b589fb0691c93dab3564`](https://github.com/deepcam-cn/yolov5-face/tree/152c688d551aefb973b7b589fb0691c93dab3564)
- repository licence: [`LICENSE`](https://github.com/deepcam-cn/yolov5-face/blob/152c688d551aefb973b7b589fb0691c93dab3564/LICENSE)
- exporter: [`export.py`](https://github.com/deepcam-cn/yolov5-face/blob/152c688d551aefb973b7b589fb0691c93dab3564/export.py)
- paper: [YOLO5Face: Why Reinventing a Face Detector](https://arxiv.org/abs/2105.12931)

### Upstream signal

The repository reports `YOLOv5m` at 95.30 easy, 93.76 medium and 85.28 hard WIDER FACE AP under single-scale VGA inference, with approximately 21.063 million parameters and 18.146 GFLOPs. The paper describes a five-point landmark regression head.

### Advantages

- Strong difficult-face signal and a materially different detector family.
- Five landmarks preserve the SFace alignment contract.
- The repository provides multiple model sizes and an export path, allowing a derived ONNX graph to be governed explicitly.

### Risks and blockers

- The implementation is GPL-3.0, which requires an explicit compatibility and distribution decision before code is copied or adapted.
- Pretrained weights are linked through external storage and no separate weight licence is apparent in the screened model table.
- A locally exported ONNX file would become a derived model revision. PyTorch version, exporter revision, opset, simplification and resulting bytes would all be part of immutable identity.
- The medium model is materially larger than CenterFace and SCRFD-10G_KPS and may increase archive runtime and review latency.

### Required before activation

1. Approve the GPL-3.0 integration approach and define whether the adapter can be independently implemented from documented tensor semantics.
2. Establish defensible rights for the exact pretrained weights.
3. Pin the source weights and the complete export toolchain before creating the ONNX manifest.
4. Use only after higher-ranked, less encumbered candidates fail or become unavailable.

## Candidate 4: RetinaFace-R50

**Current decision:** reserve behind SCRFD rather than an automatic second InsightFace run.

### Screened source

- repository revision and licence policy: [`deepinsight/insightface@7fadd420c2351d0ffa8cac403421c1a3ed733365`](https://github.com/deepinsight/insightface/tree/7fadd420c2351d0ffa8cac403421c1a3ed733365)
- model-zoo evidence: [`model_zoo/README.md`](https://github.com/deepinsight/insightface/blob/7fadd420c2351d0ffa8cac403421c1a3ed733365/model_zoo/README.md)
- paper: [RetinaFace: Single-stage Dense Face Localisation in the Wild](https://arxiv.org/abs/1905.00641)

### Upstream signal

The InsightFace model zoo reports RetinaFace-R50 at 96.5 easy, 95.6 medium and 90.4 hard WIDER FACE AP using multi-scale testing. RetinaFace predicts five facial landmarks, but the reported result is not a single-scale runtime estimate.

### Advantages

- Mature detector with strong evidence for small, posed and occluded faces.
- Five-landmark output and an established InsightFace ONNX ecosystem.
- Useful as a category-specific reserve if SCRFD unexpectedly misses a material archive category.

### Risks and blockers

- The pretrained weights carry the same InsightFace non-commercial-research restriction as SCRFD.
- The R50 model is heavier than the preferred SCRFD variants.
- Evaluating both SCRFD and RetinaFace automatically would spend review effort on two closely related upstream ecosystems before trying a less redundant alternative.

### Required before activation

Use only when SCRFD is licensed and evaluated but leaves a material failure category that RetinaFace plausibly addresses. Pin a distinct exact ONNX binary and do not reuse SCRFD thresholds, preprocessing or decoder assumptions.

## Screened-out options

### MediaPipe BlazeFace full-range

Screened source: [`google-ai-edge/mediapipe@e180fc110069f30f69d66a1b1389700b85e93224`](https://github.com/google-ai-edge/mediapipe/tree/e180fc110069f30f69d66a1b1389700b85e93224) and the [Face Detector documentation](https://developers.google.com/edge/mediapipe/solutions/vision/face_detector).

The detector is compact and provides six keypoints, but those points are the eyes, nose tip, one mouth centre and two ear tragions. It does not provide both mouth corners required by `sface-five-point-v1`. Its primary runtime and model distribution path is MediaPipe/TFLite rather than the project's existing ONNX Runtime path, and the full-range model is described for camera-oriented faces within roughly five metres.

Do not spend a full M16 comparison run on BlazeFace unless WI-0037 is explicitly expanded to include a second governed landmark model and a TFLite runtime. That would be a larger pipeline experiment, not a simple detector replacement.

### Ultra-Light-Fast Generic Face Detector RFB-640

Screened source: [`Linzaer/Ultra-Light-Fast-Generic-Face-Detector-1MB@dffdddda9794a50607cba8f318507a28c1c27cab`](https://github.com/Linzaer/Ultra-Light-Fast-Generic-Face-Detector-1MB/tree/dffdddda9794a50607cba8f318507a28c1c27cab).

The repository is MIT licensed, supports ONNX and reports Windows inference, but the detector output is oriented around boxes and scores rather than the five landmarks required by the current aligner. Its reported RFB-640 single-scale WIDER FACE hard AP is 0.579, substantially below the prioritised candidates.

Do not spend a full M16 run on UltraFace unless the product objective changes from recall improvement to extreme model-size or edge-runtime reduction. Adding a separate landmark model would also make the comparison materially more complex.

## Recommended activation order

When WI-0037 becomes necessary:

1. Re-check upstream licence and artifact availability because these facts can change.
2. Activate `SCRFD-10G_KPS` only when the exact weight terms are acceptable.
3. Otherwise activate CenterFace only after the committed ONNX weight rights are resolved.
4. Use YOLO5Face-m only after an explicit GPL and weight-provenance decision.
5. Keep RetinaFace-R50 as a targeted reserve rather than running it automatically.
6. Stop at the first candidate that passes the complete M16 gate; do not continue model shopping without a documented unresolved gap.

## WI-0037 implementation checklist

For the selected candidate:

- create a new detector manifest rather than editing the YuNet identity;
- retain the original source photos and frozen ground truth;
- persist exact detector ID, hash and pipeline configuration in every run;
- add unit tests for tensor interpretation, landmark order, source-coordinate mapping, clipping and deterministic NMS;
- add integration coverage for CLI selection, resume and exact-model failure behavior;
- record overall recall, five-plus-face recall, false and duplicate detections, material category outcome, runtime and review effort;
- keep detector evidence separate from canonical identity decisions; and
- promote only through WI-0038 after human review.

See [Model manifests and governance](model-governance.md), [Recognition and identity matching](../architecture/identity-matching.md) and [M16 face detection recall](../delivery/milestones/M16-detector-recall.md).
