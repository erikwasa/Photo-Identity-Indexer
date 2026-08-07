# CenterFace 2019 ONNX qualification

Status date: 2026-08-07

State: **exact artifact verified; ONNX Runtime incompatibility reproduced; OpenCV DNN inference reached real outputs; N-D output marshalling correction awaiting repeat smoke**

This record is the active qualification evidence for [WI-0037](../delivery/work-items/WI-0037-detector-candidate.md). Model bytes remain outside Git.

## Exact artifact

| Property | Value |
|---|---|
| Model ID | `centerface-2019-fp32` |
| Role | Face detection with five landmarks |
| Upstream repository | `Star-Clouds/CenterFace` |
| Source revision | `b82ec0c4844e89fd5a0305986aed9bdf33c72585` |
| Upstream path | `models/onnx/centerface.onnx` |
| Upstream Git blob SHA-1 | `1487d5fe214feb569865b225216b24c8f4ef1050` |
| Byte size | `7,532,772` bytes |
| SHA-256 | `77e394b51108381b4c4f7b4baf1c64ca9f4aba73e5e803b2636419578913b5fe` |
| Format/runtime | ONNX / OpenCV DNN |

The maintainer ran [`models/inspect-centerface-candidate.ps1`](../../models/inspect-centerface-candidate.ps1) on Windows on 2026-08-07. The immutable download matched the expected byte size and Git blob identity and produced the SHA-256 above. The checked-in [`centerface-2019-fp32` manifest](../../models/manifests/centerface-2019-fp32.json) pins those exact bytes.

## Runtime compatibility findings

The first disposable five-image smoke run used ONNX Runtime at confidence `0.5`, `single-pass`, with the governed dynamic multiple-of-32 input policy. All five jobs failed before detector output was produced. The durable run ID was `fbe99826-96ce-44af-b64f-3e6a3b8d93b1`.

The persisted errors showed that the pinned ONNX graph declares static input metadata equivalent to `10 x 3 x 32 x 32`. ONNX Runtime therefore rejected the intended project inputs such as `1 x 3 x 1216 x 1600`, `1 x 3 x 1280 x 1280` and other bounded photo-dependent shapes.

This failure is a runtime-contract incompatibility, not detector-quality evidence. It does not justify changing confidence, preprocessing or the private evaluation sample.

The pinned upstream CenterFace Python reference loads this same ONNX artifact through OpenCV DNN, rounds image dimensions up to multiples of `32`, builds an RGB float32 blob at the resulting dimensions and requests outputs `537`, `538`, `539` and `540`. The project therefore kept the exact model bytes and governed preprocessing/decoder semantics but switched the execution runtime from ONNX Runtime to OpenCV DNN.

The second disposable five-image smoke run used OpenCV DNN with durable run ID `8a74c35e-e214-47e6-ad47-176bebc6d7e3`. OpenCV DNN accepted the governed photo-dependent inputs and reached real CenterFace output `537` on all five jobs. Every job then failed in the managed adapter with `CenterFace output '537' could not be read as float32 data.`

That second failure exposed an output-marshalling bug rather than a graph or detector-quality failure. OpenCvSharp's `Mat.GetArray<T>` convenience path sizes its destination from the two-dimensional `Rows * Cols` view, while CenterFace DNN outputs are four-dimensional `[N,C,H,W]` tensors. PR #89 replaces that 2-D convenience path with explicit N-D shape validation and contiguous float-buffer copying. The exact model, runtime, preprocessing, decoder and confidence remain unchanged.

Both failed smoke catalogues remain retained as runtime/adapter evidence. Do not rewrite either as a successful or partially successful detector-quality run.

## Governed input policy

CenterFace's upstream implementation accepts source-dependent input dimensions. Treating it as a fixed `640x640` model would incorrectly hide preprocessing changes under one model identity.

The first governed project revision records:

- `640x640` as a reference manifest size, not a forced runtime tensor size;
- RGB float32 input, scale `1.0`, zero mean;
- source long edge bounded to `1600` pixels before multiple rounding;
- each bounded dimension rounded up to a multiple of `32`;
- direct bilinear resize to that runtime tensor; and
- the resulting input-shape policy in the checked-in model manifest.

Rounding can make the final runtime tensor dimension slightly greater than `1600`; the `1600` limit applies before rounding to the required multiple of `32`.

Changing the maximum edge, resize rule or multiple later is a new governed pipeline revision and must not be presented as the same evaluation configuration.

## Decoder contract

The pinned upstream implementation:

- requests outputs `537`, `538`, `539` and `540` as heatmap, scale, offset and landmarks;
- decodes heatmap positions at stride four;
- exponentiates the two scale channels and multiplies by four;
- treats scale channel zero as height and channel one as width;
- treats offset channel zero as vertical and channel one as horizontal;
- decodes five landmark pairs relative to each recovered box; and
- applies non-maximum suppression at IoU `0.30`.

The project adapter freezes those semantics and adds deterministic candidate ordering and suppression. It fails closed when required outputs are missing, have unexpected shapes or cannot be read as float32.

## Alignment compatibility

CenterFace emits five points. The project mapping is frozen as:

1. anatomical right eye;
2. anatomical left eye;
3. nose;
4. anatomical right mouth corner; and
5. anatomical left mouth corner.

The project maps these native points into `NormalizedFaceLandmarks` and leaves `sface-five-point-v1` unchanged. Synthetic tests lock the mapping and box/landmark math. A Windows smoke run must still visually confirm plausible aligned crops before the private 100-photo comparison.

## Detector selection and provenance

The local batch worker chooses a detector adapter by exact detector model ID:

- `yunet-2023mar-fp32` retains the existing single-pass and multi-scale behavior;
- `centerface-2019-fp32` uses the CenterFace adapter through OpenCV DNN and only permits `single-pass`.

The exact CenterFace ONNX SHA-256 is unchanged by the runtime and marshalling corrections. The manifest runtime field records `opencv-dnn`; a future change back to another execution runtime is a governed pipeline change and must not be silently compared as if identical.

The durable batch configuration records detector model ID, confidence and pipeline, while persisted face observations and results record the detector model hash. Record the exact repository commit with each private candidate run because runtime/preprocessing semantics are defined by the checked-in manifest and adapter implementation.

## Licence and training-data boundary

The pinned repository contains a root MIT licence covering the supplied "Software" and does not state a separate exception for the committed ONNX file. The manifest deliberately records the weight licence as `LicenseRef-CenterFace-Repository-MIT-Provisional` rather than asserting a definitive standalone pretrained-weight licence.

The upstream project reports WIDER FACE evaluation and the associated paper describes WIDER FACE training. This project does not assert a WIDER FACE dataset licence or a right to train or redistribute derived weights.

Local evaluation may proceed only under the maintainer's acceptance of this documented uncertainty. Production promotion or redistribution remains blocked if the weight or training-data boundary cannot be defended for the intended use.

## Qualification checklist

- [x] The immutable download matches `7,532,772` bytes.
- [x] The calculated Git blob SHA-1 matches `1487d5fe214feb569865b225216b24c8f4ef1050`.
- [x] The SHA-256 `77e394b51108381b4c4f7b4baf1c64ca9f4aba73e5e803b2636419578913b5fe` is recorded in the manifest.
- [x] A bounded dynamic input-shape policy is pinned rather than pretending the candidate is fixed `640x640`.
- [x] Resize, colour order, scale and multiple-of-32 semantics are frozen.
- [x] Heatmap, scale, offset, landmark decoding, thresholding and deterministic NMS have synthetic unit coverage.
- [x] The proposed landmark mapping is explicit and covered by tests.
- [x] The original implementation branch passed repository build/tests and documentation checks on Windows CI before the real-model smoke.
- [x] The first real ONNX Runtime smoke reproduced a static-input incompatibility on all five disposable images and preserved the errors durably.
- [x] The OpenCV DNN runtime correction passed Windows CI.
- [x] OpenCV DNN loads the exact graph and reaches real output `537` at the governed photo-dependent input dimensions.
- [ ] The N-D output-marshalling correction passes Windows CI.
- [ ] All required outputs are copied with the expected float32 `[N,C,H,W]` shapes during real inference.
- [ ] Human smoke review confirms plausible boxes and `sface-five-point-v1` aligned crops.
- [ ] The maintainer accepts the documented licence and training-data boundary for local evaluation.

## Remaining gate before the 100-photo comparison

Repeat the bounded smoke procedure in [`docs/operations/centerface-detector-runs.md`](../operations/centerface-detector-runs.md) using a **fresh** smoke database/output root while keeping the same disposable smoke images and confidence `0.5`. Do not reuse either failed smoke catalogue and do not tune the confidence threshold from the smoke images.

If the corrected OpenCV DNN smoke succeeds, the first governed private candidate remains:

- detector `centerface-2019-fp32`;
- confidence `0.5`;
- detector pipeline `single-pass`;
- manifest-bound maximum long edge `1600`, rounded to multiples of `32`;
- SFace `sface-2021dec-fp32` and padding `0.25`; and
- the unchanged WI-0034 100-photo set and frozen ground truth.

Review that complete candidate before approving any threshold change.
