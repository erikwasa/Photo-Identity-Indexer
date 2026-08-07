# CenterFace 2019 ONNX qualification

Status date: 2026-08-07

State: **exact artifact verified; runtime executes; visual smoke failed; per-image OpenCV network isolation awaiting repeat smoke**

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

The maintainer verified the immutable download on Windows with `models/inspect-centerface-candidate.ps1`. The checked-in manifest pins these exact bytes.

## Governed candidate

The first candidate remains unchanged throughout runtime debugging: detector `centerface-2019-fp32`, confidence `0.5`, `single-pass`, RGB float32 scale `1.0` zero mean, source long edge bounded to `1600` before multiple-of-32 rounding, IoU `0.30` NMS, SFace `sface-2021dec-fp32`, padding `0.25`, and `sface-five-point-v1` alignment.

Do not change confidence, preprocessing, NMS or landmark mapping while diagnosing runtime correctness. A changed value is a separate governed candidate.

## Upstream contract

The pinned upstream Python implementation rounds input dimensions up to multiples of `32`, uses `cv2.dnn.blobFromImage` with scale `1.0`, zero mean, `swapRB=True` and no crop, requests outputs `537`, `538`, `539`, `540`, thresholds with strict `score > threshold`, decodes at stride four, and applies NMS at IoU `0.30`.

The project decoder freezes those semantics and adds deterministic ordering.

## Runtime findings

### Smoke 1 — ONNX Runtime static-input incompatibility

Run `fbe99826-96ce-44af-b64f-3e6a3b8d93b1` failed all five disposable images before detector output because the pinned ONNX graph exposes stale static input metadata equivalent to `10 x 3 x 32 x 32`. ONNX Runtime rejected the intended photo-dependent tensors.

This was a runtime-contract incompatibility, not detector-quality evidence. Execution moved to OpenCV DNN, matching the pinned upstream reference while retaining exact model bytes and candidate parameters.

### Smoke 2 — N-D output marshalling bug

Run `8a74c35e-e214-47e6-ad47-176bebc6d7e3` reached real CenterFace output `537` on all five jobs, then failed while copying four-dimensional OpenCV tensors through a two-dimensional `Mat.GetArray<T>` path.

PR #89 replaced that path with explicit N-D shape validation and contiguous float-buffer copying. No candidate parameter changed.

### Smoke 3 — processing completed, visual review failed

Run `84f6f779-5a56-4e85-8d41-ee8569dce4d2` completed all five jobs at the unchanged settings: `5` succeeded and `0` failed.

Human review nevertheless found severe cross-image instability:

- one group photo with eight people produced eight detected faces;
- one photo with two people produced `593` detections, with some generated crops upside down;
- one photo with one person produced `633` detections; and
- one photo with four people produced only one unusable/indecipherable face crop.

This decisively fails the visual smoke gate. Do not process the fixed 100-photo M16 sample from this implementation and do not interpret the counts as a threshold trade-off.

## OpenCV network-lifetime finding

An independent CenterFace adapter in DeepFace explicitly notes that the model produces problematic results from the second call if the model is not flushed, and therefore rebuilds the CenterFace model for each image. The project implementation had likewise retained one `OpenCvSharp.Dnn.Net` for the full detector lifetime, so a batch reused the same native network across changing source images and dimensions.

The observed sequence — a plausible image followed by hundreds of nonsensical detections — is consistent with that documented failure mode. The current correction stores only the model path and creates/disposes a fresh OpenCV `Net` inside each inference call. Model bytes, preprocessing, confidence, decoder, NMS and landmark mapping remain unchanged.

This is still a hypothesis until the same five-image smoke is repeated successfully and visually reviewed.

## Alignment compatibility

CenterFace emits five landmarks. The project currently maps them as anatomical right eye, anatomical left eye, nose, anatomical right mouth corner and anatomical left mouth corner. Synthetic tests cover the mapping and box/landmark math.

Because smoke 3 produced corrupted detections after an initially plausible result, the upside-down crops are not sufficient evidence by themselves to change landmark mapping. Re-evaluate alignment only after network-state isolation removes cross-image corruption.

## Licence and training-data boundary

The pinned repository contains a root MIT licence covering supplied software and does not state a separate exception for the committed ONNX file. The manifest records the weight licence as `LicenseRef-CenterFace-Repository-MIT-Provisional` rather than asserting a definitive standalone pretrained-weight licence.

The upstream project reports WIDER FACE evaluation and the associated paper describes WIDER FACE training. This project does not assert a WIDER FACE dataset licence or a right to train or redistribute derived weights.

Local evaluation may proceed only under the maintainer's acceptance of this documented uncertainty. Production promotion or redistribution remains blocked if the weight or training-data boundary cannot be defended for the intended use.

## Qualification checklist

- [x] Exact model byte size, Git blob SHA-1 and SHA-256 are pinned.
- [x] A bounded dynamic multiple-of-32 input policy is recorded.
- [x] Upstream preprocessing, output names, decoder math and NMS semantics are documented.
- [x] Synthetic tests cover preprocessing, decoder geometry, threshold semantics and landmark mapping.
- [x] ONNX Runtime static-input incompatibility was reproduced and retained as evidence.
- [x] OpenCV DNN executes the exact graph at governed photo-dependent dimensions.
- [x] N-D output marshalling is corrected and covered by regression testing.
- [x] A complete five-image batch can execute without terminal processing errors.
- [x] Human visual review of smoke 3 was performed and failed.
- [x] Smoke 3 blocks the 100-photo comparison.
- [ ] Per-image OpenCV `Net` isolation passes Windows CI.
- [ ] Repeat smoke produces plausible detection counts across all five disposable images.
- [ ] Repeat smoke aligned crops show sensible, non-corrupted eye/nose/mouth geometry.
- [ ] The maintainer accepts the documented licence/training-data boundary for local evaluation.

## Stop rule

Do **not** run the fixed 100-photo M16 comparison yet. First repeat the same five disposable images on the per-image-network implementation, using a fresh database/output root and unchanged confidence `0.5`.

If the repeated smoke still produces hundreds of detections or corrupted crops, stop and investigate preprocessing/output semantics before any threshold experiment. If it is stable, visually validate boxes and aligned crops before authorising the private 100-photo sample.
