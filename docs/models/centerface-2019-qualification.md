# CenterFace 2019 ONNX qualification

Status date: 2026-08-07

State: **exact artifact verified and adapter implemented; Windows runtime smoke required before the private M16 sample**

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
| Format/runtime | ONNX / ONNX Runtime |

The maintainer ran [`models/inspect-centerface-candidate.ps1`](../../models/inspect-centerface-candidate.ps1) on Windows on 2026-08-07. The immutable download matched the expected byte size and Git blob identity and produced the SHA-256 above. The checked-in [`centerface-2019-fp32` manifest](../../models/manifests/centerface-2019-fp32.json) now pins those exact bytes, and the inspector itself now rejects any later SHA-256 mismatch.

## Governed input policy

CenterFace's upstream implementation accepts source-dependent input dimensions. Treating it as a fixed `640x640` model would incorrectly hide preprocessing changes under one model identity.

The first governed project revision therefore records:

- `640x640` as a reference manifest size, not a forced runtime tensor size;
- RGB float32 input, scale `1.0`, zero mean;
- source long edge bounded to `1600` pixels before multiple rounding;
- each bounded dimension rounded up to a multiple of `32`;
- direct bilinear resize to that runtime tensor, matching the upstream `blobFromImage(..., crop=false)` resize behavior; and
- the resulting input-shape policy in the checked-in model manifest.

Rounding can make the final runtime tensor dimension slightly greater than `1600`; the `1600` limit applies before rounding to the required multiple of `32`.

Changing the maximum edge, resize rule or multiple later is a new governed pipeline revision and must not be presented as the same evaluation configuration.

## Decoder contract

The pinned upstream Python implementation:

- requests outputs `537`, `538`, `539` and `540` as heatmap, scale, offset and landmarks;
- decodes heatmap positions at stride four;
- exponentiates the two scale channels and multiplies by four;
- treats scale channel zero as height and channel one as width;
- treats offset channel zero as vertical and channel one as horizontal;
- decodes five landmark pairs relative to each recovered box; and
- applies non-maximum suppression at IoU `0.30`.

The project adapter freezes those semantics and adds deterministic candidate ordering and suppression. It fails closed when required outputs are missing, have unexpected shapes or are not float32.

## Alignment compatibility

CenterFace emits five points. The project mapping is frozen as:

1. anatomical right eye;
2. anatomical left eye;
3. nose;
4. anatomical right mouth corner; and
5. anatomical left mouth corner.

The upstream decoder establishes the five-point tensor layout but does not label the anatomy in code. The anatomical interpretation is independently corroborated by the DeepFace CenterFace adapter, which explicitly names the same point order. The project maps these native points into `NormalizedFaceLandmarks` and leaves `sface-five-point-v1` unchanged.

Synthetic tests lock that mapping and its box/landmark math. A Windows smoke run must still visually confirm plausible aligned crops before the private 100-photo comparison; this protects against a formally consistent but semantically wrong landmark interpretation.

## Detector selection and provenance

The local batch worker now chooses a detector adapter by exact detector model ID:

- `yunet-2023mar-fp32` retains the existing single-pass and multi-scale behavior;
- `centerface-2019-fp32` uses the CenterFace adapter and only permits `single-pass`.

The durable batch configuration records detector model ID, confidence and pipeline, while persisted face observations and results record the detector model hash. The CenterFace input policy is defined by the checked-in manifest and adapter implementation rather than duplicated into the older batch configuration schema.

For this governed evaluation, the operator runbook therefore records the exact repository commit and re-verifies the installed model immediately before processing. Keep that commit and manifest unchanged when resuming the candidate. A future preprocessing-policy change requires a new governed candidate and must not reuse this result as if it were identical.

## Licence and training-data boundary

The pinned repository contains a root MIT licence covering the supplied "Software" and does not state a separate exception for the committed ONNX file. The manifest deliberately records the weight licence as `LicenseRef-CenterFace-Repository-MIT-Provisional` rather than asserting a definitive standalone pretrained-weight licence.

The upstream project reports WIDER FACE evaluation and the associated paper describes WIDER FACE training. This project does not assert a WIDER FACE dataset licence or a right to train or redistribute derived weights.

Local evaluation may proceed only under the maintainer's acceptance of this documented uncertainty. Production promotion or redistribution remains blocked if the weight or training-data boundary cannot be defended for the intended use.

## Qualification checklist

- [x] The immutable download matches `7,532,772` bytes.
- [x] The calculated Git blob SHA-1 matches `1487d5fe214feb569865b225216b24c8f4ef1050`.
- [x] The SHA-256 `77e394b51108381b4c4f7b4baf1c64ca9f4aba73e5e803b2636419578913b5fe` is recorded in the manifest.
- [x] A bounded dynamic input-shape policy is pinned rather than pretending the graph is fixed `640x640`.
- [x] Resize, colour order, scale and multiple-of-32 semantics are frozen.
- [x] Heatmap, scale, offset, landmark decoding, thresholding and deterministic NMS have synthetic unit coverage.
- [x] The proposed landmark mapping is explicit and covered by tests.
- [ ] Repository build and tests pass on the supported Windows toolchain.
- [ ] ONNX Runtime loads the exact graph and accepts a bounded dynamic input on Windows.
- [ ] The required outputs are observed with the expected float32 shapes during real inference.
- [ ] Human smoke review confirms plausible boxes and `sface-five-point-v1` aligned crops.
- [ ] The maintainer accepts the documented licence and training-data boundary for local evaluation.

## Remaining gate before the 100-photo comparison

Run the bounded smoke procedure in [`docs/operations/centerface-detector-runs.md`](../operations/centerface-detector-runs.md). Do not tune the confidence threshold using the smoke images; it is a functional/runtime check only.

If the smoke succeeds, the first governed private candidate is:

- detector `centerface-2019-fp32`;
- confidence `0.5`;
- detector pipeline `single-pass`;
- manifest-bound maximum long edge `1600`, rounded to multiples of `32`;
- SFace `sface-2021dec-fp32` and padding `0.25`; and
- the unchanged WI-0034 100-photo set and frozen ground truth.

Review that complete candidate before approving any threshold change.
