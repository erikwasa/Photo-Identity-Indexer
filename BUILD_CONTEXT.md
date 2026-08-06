# Build context

## Current milestones

Two independent milestones are active:

- **M15 — Operator documentation and system guide**: `in_progress`
- **M16 — Face detection recall**: `in_progress`

## Current work

**WI-0032 — Validate documentation from a clean setup** remains active under the human maintainer. Its clean-environment and trusted-network checklist remains authoritative for M15.

**WI-0037 — Evaluate another face detector** is active under the AI agent on `agent/WI-0037-centerface-qualification`.

WI-0036 completed on 2026-08-07. The multi-scale confidence-0.9 YuNet candidate failed the complete M16 gate despite improving on the single-pass confidence-0.9 baseline and single-pass confidence `0.8`. The explicit confidence-0.7 multi-scale follow-up returned more than 100 false or duplicate detections, so confidence `0.6` was intentionally not run. No YuNet threshold or multi-scale configuration is approved for rollout.

Detailed recall, category and review evidence remains private. The repository records only privacy-safe workflow conclusions and the disqualifying aggregate false/duplicate result.

## M16 implementation direction

CenterFace ONNX is the first WI-0037 qualification target.

The selected upstream artifact is `models/onnx/centerface.onnx` from `Star-Clouds/CenterFace@b82ec0c4844e89fd5a0305986aed9bdf33c72585`.

It is being qualified before SCRFD because:

- it provides five landmarks compatible with the current SFace alignment contract;
- the upstream repository includes a compact ONNX graph and an MIT root licence; and
- InsightFace attaches an explicit non-commercial restriction to its supplied SCRFD pretrained models.

CenterFace is not yet approved for the private 100-photo evaluation. The active increment must pin exact bytes and SHA-256, record model-weight and WIDER FACE limitations separately, verify graph and decoder semantics under the project's ONNX Runtime version, implement the adapter and pass a bounded Windows CPU smoke test.

The upstream reference behavior rounds input dimensions up to multiples of 32, creates an RGB float32 tensor with scale `1.0` and zero mean, and decodes heatmap, scale, offset and five-landmark outputs at stride four before NMS. The exact graph must independently confirm these assumptions.

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
- `docs/delivery/work-items/WI-0036-multiscale-yunet.md`
- `docs/delivery/work-items/WI-0037-detector-candidate.md`
- `docs/models/face-detector-candidate-registry.md`
- `docs/operations/multiscale-detector-runs.md`
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
