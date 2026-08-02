# Build context

## Current milestone

**M15 — Operator documentation and system guide**

Status: `in_progress`

## Current work

**WI-0031 — Rewrite operator and architecture documentation** is active on `agent/WI-0031-architecture-glossary`.

M14 and WI-0025 completed on 2026-08-02. The collection API, responsive Windows/Pixel workspace, fixed thumbnails and version-1 neutral manifest passed automated and private-catalogue acceptance.

PR #64 delivered the first WI-0031 slice: a concise README, one authoritative local operator guide and a documentation index that separates the end-to-end command sequence from specialized references.

The current slice completes the remaining rewrite acceptance criteria:

- exact-revision single-model evaluation guidance;
- an automated, resumable FP32-versus-INT8 comparison runbook with the accepted FP32 recommendation;
- reconciled application, module, data, matching and portable-compute architecture;
- aligned baseline, candidate and model-governance pages;
- updated SQLite backup, locking and accepted-resume guidance; and
- a shared glossary plus explicit documentation routing.

## Next concrete step

1. Run the full GitHub Actions workflow.
2. Require `PhotoIdentity.Docs validate` and `generate --check` to pass with no stale links or generated files.
3. Review the documentation diff for privacy, command consistency and duplicated guidance.
4. Move WI-0031 to `in_review` with PR/CI evidence after validation passes.
5. Merge the documentation rewrite.
6. Start WI-0032 to exercise the instructions from a clean Windows setup and trusted-network Pixel path.

## Completed gates

- The 450–550-image baseline pilot passed restart/resume, cross-device review, matcher invariants, deterministic export/evaluation, backup and restore.
- Queue-aware Faces review combines continuous loading, exact-model suggestion ordering, automatic advance, correction, audit and preview-first grouped acceptance.
- Published review smoke protects routes, mutation/audit invariants, privacy boundaries and a multi-page disposable fixture.
- SFace FP32 and INT8 coexist under exact provenance while sharing one canonical catalogue and human review history.
- The same-corpus comparison passed source, detector-count, deterministic-export and split-equality checks.
- A private manual review of 20 representative faces found both revisions correct with no material practical difference; FP32 remains the local default.
- Collection queries and the neutral manifest passed automated validation plus private Windows/Pixel verification.
- The README and local operator guide now provide one current PowerShell-first path.
- The architecture and model documentation now describe implemented behavior rather than roadmap-era plans.

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
```
