# Build context

## Current milestone

**M15 — Operator documentation and system guide**

Status: `in_progress`

## Current work

**WI-0032 — Validate documentation from a clean setup** is active under the human maintainer. The current documentation correction is on `agent/WI-0032-powershell-optional`.

WI-0031 completed on 2026-08-02 after PRs #64–#66 merged. PR #67 then closed the implementation item, started WI-0032 and added the clean-environment validation checklist.

## Validation progress

The maintainer ran the first automated validation phase on Windows:

- documentation registry and link validation passed;
- generated-document checking passed;
- the comparison self-test passed under Windows PowerShell `5.1.26100.8875`; and
- PowerShell 7 was not installed, so the literal `pwsh` command failed before the script could run.

That result exposed a documentation inconsistency rather than a product failure. The checklist described PowerShell 7 or Windows PowerShell 5.1 as sufficient, but later invoked both executables unconditionally.

The correction now states:

- at least one supported PowerShell edition is required;
- Windows PowerShell 5.1 is sufficient for local validation;
- PowerShell 7 is optional locally;
- every installed supported edition must pass; and
- repository CI remains responsible for continuously testing both editions.

The maintainer also reported executing the baseline build, test, model-installation and review-smoke commands. Their final pass/fail states must be included in the WI-0032 completion summary.

## Remaining milestone gate

Only independent clean-environment and trusted-network human verification remains. `docs/delivery/work-items/WI-0032-documentation-validation.md` is the authoritative checklist.

The remaining work is to:

1. merge the optional-PowerShell documentation correction;
2. confirm pass/fail for the baseline build, tests, model installation and review smoke;
3. process a representative 450–550-image private set, including interruption, status and resume;
4. verify review, suggestion, evaluation, collection and neutral-manifest workflows on Windows;
5. verify the browser workflow on Pixel over a trusted private network;
6. validate deterministic export/evaluation, stopped-state backup/restore and documented recovery paths;
7. inspect the multi-model procedure, using Windows PowerShell 5.1 and PowerShell 7 only when installed;
8. perform the second reading pass and correct every confusing or hidden prerequisite; and
9. return only the privacy-safe summary template from WI-0032.

Any documentation defect found during validation must be corrected and merged before WI-0032 and M15 complete.

## Completed gates

- The 450–550-image baseline pilot passed restart/resume, cross-device review, matcher invariants, deterministic export/evaluation, backup and restore.
- Queue-aware Faces review combines continuous loading, exact-model suggestion ordering, automatic advance, correction, audit and preview-first grouped acceptance.
- Published review smoke protects routes, mutation/audit invariants, privacy boundaries and a multi-page disposable fixture.
- SFace FP32 and INT8 coexist under exact provenance while sharing one canonical catalogue and human review history.
- The same-corpus comparison passed source, detector-count, deterministic-export and split-equality checks.
- A private manual review of 20 representative faces found both revisions correct with no material practical difference; FP32 remains the local default.
- Collection queries and the neutral manifest passed automated validation plus private Windows/Pixel verification.
- The README and local operator guide provide one current PowerShell-first path.
- The architecture and model documentation describe implemented behavior rather than roadmap-era plans.
- All WI-0031 acceptance criteria and automated validation gates are complete.

## Delivery objective

1. maintain the accepted local processing, review, evaluation and collection workflows;
2. independently validate the operator and architecture documentation from a clean setup; and
3. resume Azure execution only after documentation validation and when access is available.

## Relevant planning files

- `docs/delivery/milestones/M15-documentation.md`
- `docs/delivery/work-items/WI-0031-documentation-rewrite.md`
- `docs/delivery/work-items/WI-0032-documentation-validation.md`
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
