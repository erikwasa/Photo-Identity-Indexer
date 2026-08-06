# CenterFace 2019 ONNX qualification

Status date: 2026-08-07

State: **selected for exact-artifact qualification; not authorised for the private M16 sample**

This record starts the first implementation increment for [WI-0037](../delivery/work-items/WI-0037-detector-candidate.md). It pins the upstream source and expected Git object while keeping the model binary outside Git.

## Selected upstream artifact

| Property | Value |
|---|---|
| Proposed model ID | `centerface-2019-fp32` |
| Role | Face detection with five landmarks |
| Upstream repository | `Star-Clouds/CenterFace` |
| Source revision | `b82ec0c4844e89fd5a0305986aed9bdf33c72585` |
| Upstream path | `models/onnx/centerface.onnx` |
| Upstream Git blob SHA-1 | `1487d5fe214feb569865b225216b24c8f4ef1050` |
| Expected byte size | `7,532,772` bytes |
| Format/runtime target | ONNX / ONNX Runtime |
| Exact SHA-256 | Pending local byte verification |

The repository automation available while preparing this record could read Git metadata and source text but could not download arbitrary binary bytes. It therefore does not claim an SHA-256 or graph inspection that it did not perform.

Run [`models/inspect-centerface-candidate.ps1`](../../models/inspect-centerface-candidate.ps1) on Windows to download the immutable artifact, verify its byte size and Git blob identity, and print the locally calculated SHA-256. Record that value in the eventual detector manifest only after the command succeeds.

## Pinned preprocessing and decoder evidence

The upstream Python implementation at the same revision:

- rounds source width and height up to multiples of 32;
- creates an input with OpenCV `blobFromImage` using scale `1.0`, zero mean, `swapRB=true` and no crop;
- requests outputs `537`, `538`, `539` and `540` as heatmap, scale, offset and landmarks;
- decodes heatmap positions at stride four;
- exponentiates the two scale channels and multiplies by four;
- applies the offset channels to recover box centres;
- decodes five landmark pairs relative to each recovered box; and
- applies non-maximum suppression at IoU `0.30`.

These statements are source-code evidence, not yet a frozen application contract. The exact ONNX graph and produced tensors must independently confirm them before implementation is accepted.

## Alignment compatibility

CenterFace predicts five landmark pairs, which is structurally compatible with `sface-five-point-v1`. Structural compatibility is not enough: the adapter must verify the anatomical ordering of right eye, left eye, nose, right mouth corner and left mouth corner before any embeddings are generated.

Do not silently reorder landmarks based only on visual plausibility. Freeze the verified mapping in tests and model provenance.

## Licence and training-data boundary

The pinned repository contains a root MIT licence covering the supplied "Software" and does not state a separate exception for the committed ONNX file. The project records that as provisional model-weight evidence, not as legal advice or automatic production approval.

The upstream project reports WIDER FACE evaluation and the associated paper describes WIDER FACE training. This project does not assert a WIDER FACE dataset licence or a right to train or redistribute derived weights.

Before production promotion or redistribution, the maintainer must accept the model-weight interpretation and the remaining training-data limitation. An unresolved boundary blocks promotion even when local evaluation succeeds.

## Qualification checklist

- [ ] The immutable download matches `7,532,772` bytes.
- [ ] The calculated Git blob SHA-1 matches `1487d5fe214feb569865b225216b24c8f4ef1050`.
- [ ] The locally calculated SHA-256 is recorded.
- [ ] ONNX Runtime loads the exact graph on the supported Windows runtime.
- [ ] Input and output names, shapes and numeric types are recorded from the graph.
- [ ] Resize, colour order, scale and padding semantics are frozen.
- [ ] Heatmap, scale, offset and landmark decoding are covered by deterministic tests.
- [ ] Landmark anatomical order is verified against `sface-five-point-v1`.
- [ ] Licence and training-data limitations are accepted for the intended use.
- [ ] A bounded Windows CPU smoke test passes before the 100-photo run.

## Current blockers

1. The current model-manifest schema records fixed positive input width and height, while the upstream implementation uses dimensions derived from each source image. The governed input-shape policy must be selected before creating the manifest.
2. Exact graph metadata and SHA-256 still require local binary inspection.
3. Landmark order requires explicit verification.
4. The adapter, unit coverage and durable detector-selection provenance do not yet exist.
5. The Windows CPU smoke test has not run.

## Decision

Proceed with exact-artifact verification and adapter design. Do not process the private M16 sample and do not treat `centerface-2019-fp32` as an installable governed model until every blocker above is closed.
