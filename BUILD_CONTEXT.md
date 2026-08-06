# Build context

## Current milestones

Two independent milestones are active:

- **M15 — Operator documentation and system guide**: `in_progress`
- **M16 — Face detection recall**: `in_progress`

## Current work

**WI-0032 — Validate documentation from a clean setup** remains active under the human maintainer. Its clean-environment and trusted-network checklist remains authoritative for M15.

**WI-0036 — Add multi-scale YuNet detection** is active under the AI agent on `agent/WI-0036-multiscale-yunet`.

The maintainer completed the governed M16 confidence sweep on 2026-08-06. The immutable confidence-0.9 baseline and isolated confidence `0.8`, `0.7`, `0.6` and `0.5` candidates all failed the complete M16 gate. Detailed metrics and category evidence remain private. WI-0035 is therefore complete, and threshold tuning is not approved for rollout.

## M16 implementation direction

The first WI-0036 increment adds an opt-in `full-image-plus-tiles` YuNet pipeline while preserving `single-pass` as the compatibility default.

The implementation provides:

- an aspect-ratio-preserving full-image pass;
- deterministic overlapping source-pixel tiles;
- letterboxed pass preprocessing;
- mapping of boxes and five landmarks into original-image normalised coordinates;
- deterministic global non-maximum suppression across all passes;
- durable pipeline, tile, overlap and merge provenance in processing-run configuration; and
- automated coverage for planning, mapping, suppression, ordering and configuration compatibility.

The next M16 step after merge is to process the unchanged private 100-photo set at confidence `0.9` using the governed multi-scale defaults, attach that run to the frozen baseline ground truth, review only surfaced exceptions, record runtime and review effort, and assess the unchanged complete M16 gate.

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
- The confidence-0.9 YuNet baseline and all governed confidence candidates through `0.5` were reviewed against the same frozen 100-photo ground truth; threshold tuning was insufficient.

## Relevant planning files

- `docs/delivery/milestones/M15-documentation.md`
- `docs/delivery/work-items/WI-0032-documentation-validation.md`
- `docs/delivery/milestones/M16-detector-recall.md`
- `docs/delivery/work-items/WI-0036-multiscale-yunet.md`
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
