# Build context

## Current milestones

Two independent milestones are active:

- **M15 — Operator documentation and system guide**: `in_progress`
- **M16 — Face detection recall**: `in_progress`

## Current work

**WI-0032 — Validate documentation from a clean setup** remains active under the human maintainer. Its clean-environment and trusted-network checklist remains authoritative for M15.

**WI-0042 — Add bounded archive hydration and review proxies** is implementing Slice 1 on `agent/WI-0042-proxy-measurement` in PR #105. The slice adds an exact versioned review-proxy profile contract, deterministic OpenCV JPEG rendering, durable proxy profile/completion metadata separate from detector/embedder analysis completion, and the `archive proxy measure` operator path. The measurement command accepts explicit candidate settings, stores generated derivatives outside the supplied source root and reports privacy-safe aggregate storage statistics without selecting a permanent proxy default.

The next concrete WI-0042 step after Slice 1 is merged and runnable locally is the fixed private 100-image tuning measurement described in `docs/operations/review-proxy-measurement.md`: compare at least two exact proxy candidates, inspect a representative subset for browsing/review usability, retain only aggregate results, then run the selected exact profile against the unchanged 560-image pilot corpus for scale validation. WI-0041 remains blocked until the broader bounded-storage workflow is implemented and locally verified.

**WI-0037 — Evaluate another face detector** is active under the AI agent. PR #85 completed the WI-0036 transition and initial CenterFace qualification; runnable CenterFace work continues on `agent/WI-0037-centerface-adapter`.

WI-0036 completed on 2026-08-07. The multi-scale confidence-0.9 YuNet candidate failed the complete M16 gate despite improving on the single-pass confidence-0.9 baseline and single-pass confidence `0.8`. The explicit confidence-0.7 multi-scale follow-up returned more than 100 false or duplicate detections, so confidence `0.6` was intentionally not run. No YuNet threshold or multi-scale configuration is approved for rollout.

Detailed recall, category and review evidence remains private. The repository records only privacy-safe workflow conclusions and the disqualifying aggregate false/duplicate result.

## M16 CenterFace implementation direction

The maintainer locally verified the pinned CenterFace artifact on 2026-08-07:

- source revision `Star-Clouds/CenterFace@b82ec0c4844e89fd5a0305986aed9bdf33c72585`;
- byte size `7,532,772`;
- Git blob SHA-1 `1487d5fe214feb569865b225216b24c8f4ef1050`; and
- SHA-256 `77e394b51108381b4c4f7b4baf1c64ca9f4aba73e5e803b2636419578913b5fe`.

The active branch adds the immutable `centerface-2019-fp32` manifest and a dedicated ONNX Runtime adapter. It does not force the dynamic model into a false fixed-`640x640` contract. Instead the manifest freezes a bounded dynamic policy: source long edge at most `1600` before rounding each dimension up to a multiple of `32`, RGB float32, scale `1.0`, zero mean and direct bilinear resize.

The adapter requires outputs `537`, `538`, `539` and `540`, decodes heatmap/scale/offset/five-landmark tensors at stride four, uses deterministic IoU `0.30` NMS and explicitly maps native right-eye, left-eye, nose, right-mouth and left-mouth points into the unchanged `sface-five-point-v1` contract.

Local batch processing now selects the detector adapter by exact model ID. Existing YuNet behavior is unchanged; CenterFace supports only the `single-pass` batch pipeline. Synthetic tests cover manifest provenance, dynamic preprocessing, RGB channel order, decoder geometry, confidence semantics, NMS, landmark mapping and malformed tensor shapes.

The agent environment cannot execute a clean .NET checkout because outbound Git/DNS access is unavailable. Do not treat the branch as runtime-verified until the Windows procedure in `docs/operations/centerface-detector-runs.md` passes.

The first governed full candidate is predeclared as CenterFace confidence `0.5`, single-pass, manifest-bound maximum long edge `1600`, SFace FP32 and padding `0.25`. Do not tune confidence during the disposable smoke test or before reviewing the complete first M16 comparison.

## Remaining WI-0037 gate before private evaluation

1. Windows restore/build/test and living-document checks pass.
2. `centerface-2019-fp32` installs through the normal model verifier.
3. A separate disposable smoke set confirms real ONNX Runtime dynamic-shape inference.
4. Human inspection confirms plausible detector boxes and non-mirrored SFace aligned crops.
5. The maintainer accepts the documented MIT/model-weight interpretation and unresolved WIDER FACE training-data limitation for local evaluation.

Only then process the unchanged fixed 100-photo M16 sample.

## M15 validation progress

The maintainer ran the first automated validation phase on Windows:

- documentation registry and link validation passed;
- generated-document checking passed;
- the comparison self-test passed under Windows PowerShell `5.1.26100.8875`; and
- PowerShell 7 was not installed, so the literal `pwsh` command failed before the script could run.

That result exposed a documentation inconsistency rather than a product failure. The checklist now treats Windows PowerShell 5.1 as sufficient locally and PowerShell 7 as optional when it is not installed.

The maintainer also reported executing the baseline build, test, model-installation and review-smoke commands. Their final pass/fail states must be included in the WI-0032 completion summary.

## Remaining M15 gate

Only independent clean-environment and trusted-network human verification remains. `docs/delivery/work-items/WI-0032-documentation-validation.md` is the authoritative checklist.

Any documentation defect found during validation must be corrected and merged before WI-0032 and M15 complete.

## Completed gates

- The 450–550-image baseline pilot passed restart/resume, cross-device review, matcher invariants, deterministic export/evaluation, backup and restore.
- Queue-aware Faces review combines continuous loading, exact-model suggestion ordering, automatic advance, correction, audit and preview-first grouped acceptance.
- Published review smoke protects routes, mutation/audit invariants, privacy boundaries and a multi-page disposable fixture.
- SFace FP32 and INT8 coexist under exact provenance while sharing one canonical catalogue and human review history.
- The same-corpus comparison passed source, detector-count, deterministic-export and split-equality checks.
- A private manual review of 20 representative faces found both revisions correct with no material practical difference; FP32 remains the local default.
- Collection queries and the neutral manifest passed automated validation plus private Windows/Pixel verification.
- The confidence-0.9 YuNet baseline and all governed single-pass confidence candidates through `0.5` were reviewed against the same frozen 100-photo ground truth; threshold tuning was insufficient.
- Governed multi-scale YuNet candidates at confidence `0.9` and `0.7` were evaluated; neither met the complete gate, and the `0.7` candidate produced more than 100 false or duplicate detections.

## Relevant planning files

- `docs/delivery/milestones/M15-documentation.md`
- `docs/delivery/work-items/WI-0032-documentation-validation.md`
- `docs/delivery/milestones/M16-detector-recall.md`
- `docs/delivery/work-items/WI-0037-detector-candidate.md`
- `docs/delivery/work-items/WI-0042-bounded-archive-storage.md`
- `docs/operations/review-proxy-measurement.md`
- `docs/models/centerface-2019-qualification.md`
- `docs/models/face-detector-candidate-registry.md`
- `docs/operations/centerface-detector-runs.md`
- `docs/delivery/status/work-items.yaml`
- `docs/delivery/status/milestones.yaml`

## Automated validation

```powershell
./build.ps1
./test.ps1
./verify-local.ps1 -InstallModels
./verify-review.ps1 -Mode Smoke -Configuration Release
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
powershell.exe -NoProfile -File ./Invoke-MultiModelComparison.ps1 -SelfTest

if (Get-Command pwsh -ErrorAction SilentlyContinue) {
    pwsh -NoProfile -File ./Invoke-MultiModelComparison.ps1 -SelfTest
}
```
