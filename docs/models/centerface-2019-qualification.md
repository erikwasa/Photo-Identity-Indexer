# CenterFace 2019 ONNX qualification

Status date: 2026-08-07

State: **exact artifact verified; corrected OpenCV runtime validated; local governance boundary accepted; governed 100-photo candidate passed M16 and is selected for WI-0038 rollout**

This record is the completed qualification evidence for [WI-0037](../delivery/work-items/WI-0037-detector-candidate.md). Model bytes remain outside Git.

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

The candidate remained unchanged throughout runtime qualification and the complete M16 comparison: detector `centerface-2019-fp32`, confidence `0.5`, `single-pass`, RGB float32 scale `1.0` zero mean, source long edge bounded to `1600` before multiple-of-32 rounding, IoU `0.30` NMS, SFace `sface-2021dec-fp32`, padding `0.25`, and `sface-five-point-v1` alignment.

Any changed confidence, preprocessing, NMS, resize rule or landmark mapping is a different governed candidate.

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

This failed the visual smoke gate and blocked the fixed 100-photo M16 sample until the runtime defect was corrected.

## OpenCV network-lifetime finding and correction

An independent CenterFace adapter in DeepFace explicitly notes that the model produces problematic results from the second call if the model is not flushed, and therefore rebuilds the CenterFace model for each image. The project implementation had likewise retained one `OpenCvSharp.Dnn.Net` for the full detector lifetime, so a batch reused the same native network across changing source images and dimensions.

The observed smoke-3 sequence — a plausible image followed by hundreds of nonsensical detections — was consistent with that documented failure mode. PR #90 changed the project adapter to store only the model path and create/dispose a fresh OpenCV `Net` inside each inference call. Model bytes, preprocessing, confidence, decoder, NMS and landmark mapping remained unchanged.

Windows CI for the corrected PR head passed. The maintainer then repeated the same five-image disposable smoke at the unchanged governed settings and reported that the face outputs matched the source images consistently on every image. The repeat run ID was not supplied to the repository record, so no identifier is invented here.

This repeat cleared the cross-image runtime-stability smoke gate. It was functional and visual qualification evidence only; it was not used to tune confidence.

## Alignment compatibility

CenterFace emits five landmarks. The project maps them into the unchanged five-point SFace contract, and synthetic tests cover the mapping and box/landmark math.

The corrected repeat smoke no longer showed the gross cross-image corruption seen when one OpenCV network instance was reused. The complete 100-photo candidate subsequently passed the detector gate. WI-0038 still treats geometry/landmark reconciliation as a rollout invariant because detector replacement can change occurrence order and face population even when detector quality is acceptable.

## Licence and training-data boundary

The pinned repository contains a root MIT licence covering supplied software and does not state a separate exception for the committed ONNX file. The manifest records the weight licence as `LicenseRef-CenterFace-Repository-MIT-Provisional` rather than asserting a definitive standalone pretrained-weight licence.

The upstream project reports WIDER FACE evaluation and the associated paper describes WIDER FACE training. This project does not assert a WIDER FACE dataset licence or a right to train or redistribute derived weights.

On 2026-08-07 the maintainer explicitly accepted this documented uncertainty for **local evaluation and the private local rollout work that follows it**. This is a governance decision for the local project, not a claim that the pretrained-weight or training-data rights have been independently resolved. Redistribution remains blocked unless that boundary can be defended for the intended use.

## Governed 100-photo result

After the runtime smoke and local governance checks, the maintainer completed the unchanged WI-0034 100-photo comparison at the predeclared CenterFace settings and reported that the candidate **passed the complete M16 gate**.

Only the privacy-safe decision is retained here:

- overall recall met the `90%` target;
- five-plus-face recall met the `85%` target;
- false plus duplicate detections remained within the limit of `10`; and
- no material archive-workflow failure category remained.

Detailed counts, filenames, source paths, geometry and manual review decisions remain private.

PR #92 introduced a narrow neutral candidate outcome for legitimate faces that were intentionally outside the frozen countable-face scope. Neutral cannot increase recall and is not a false or duplicate, so this correction does not weaken the recall gate or retroactively expand the fixed ground truth.

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
- [x] Per-image OpenCV `Net` isolation passed Windows CI.
- [x] Repeat smoke produced stable face outputs across all five disposable images.
- [x] The maintainer explicitly accepted the documented licence/training-data boundary for local evaluation and private local rollout work.
- [x] The unchanged governed 100-photo candidate passed the complete M16 detector gate.

## Rollout boundary

CenterFace confidence `0.5` single-pass is selected for [WI-0038](../delivery/work-items/WI-0038-detector-rollout.md).

Selection is not permission to replace existing face occurrences by ordinal. The canonical catalogue currently preserves identity assignments and review history by `face_occurrence_id`, so WI-0038 must reconcile old and new detections using geometry and landmarks, create new occurrences for genuinely new faces, route ambiguous mappings through review and retain a rollback path before full-archive processing.
