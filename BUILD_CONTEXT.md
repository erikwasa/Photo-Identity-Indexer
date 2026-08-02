# Build context

## Current milestone

**M15 — Operator documentation and system guide**

Status: `in_progress`

## Current work

**WI-0031 — Rewrite operator and architecture documentation** is in its final housekeeping pass on `agent/general-housekeeping`.

M14 and WI-0025 completed on 2026-08-02. The collection API, responsive Windows/Pixel workspace, fixed thumbnails and version-1 neutral manifest passed automated and private-catalogue acceptance.

PR #64 delivered the concise README, authoritative local operator guide and documentation routing. PR #65 delivered the exact-revision evaluation and multi-model runbooks, implemented-system architecture, aligned model/persistence guidance and shared glossary. Build #406 and multi-model workflow #6 passed before PR #65 merged.

The final housekeeping pass removes temporary completed-work-item artifacts and makes the PowerShell entry points describe their permanent supported behavior:

- remove the retired `docs/delivery/verification` WI-0033 procedure;
- remove the one-off WI-0033 review-session reporter;
- decouple `verify-review.ps1` from completed work-item output and report fields;
- generalize multi-model comparison reports and generated manual checklists;
- make build, test and model-install wrappers Release-first with explicit native exit-code handling; and
- retain `verify-local.ps1` and the published review smoke script after confirming their output is current and work-item-neutral.

## Next concrete step

1. Run the main build and multi-model workflow against the housekeeping branch.
2. Require Release build, all tests, documentation validation, generated-document checks, review smoke and Windows PowerShell verification to pass.
3. Confirm no repository references remain to the deleted WI-0033 guide or reporter.
4. Move WI-0031 to `in_review` with PR and workflow evidence.
5. Merge the housekeeping slice.
6. Start WI-0032 to exercise the instructions from a clean Windows setup and trusted-network Pixel path.

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
- All WI-0031 documentation acceptance criteria are checked.

## Delivery objective

1. maintain the accepted local processing, review, evaluation and collection workflows;
2. rewrite and independently validate the operator and architecture documentation; and
3. resume Azure execution only after documentation validation and when access is available.

## Relevant planning files

- `docs/delivery/milestones/M15-documentation.md`
- `docs/delivery/work-items/WI-0031-documentation-rewrite.md`
- `docs/delivery/work-items/WI-0032-documentation-validation.md`
- `docs/delivery/status/work-items.yaml`
- `docs/delivery/status/milestones.yaml`

## Validation

```powershell
dotnet test
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
./verify-review.ps1 -Mode Smoke -Configuration Release
powershell.exe -NoProfile -File ./Invoke-MultiModelComparison.ps1 -SelfTest
pwsh -NoProfile -File ./Invoke-MultiModelComparison.ps1 -SelfTest
```
