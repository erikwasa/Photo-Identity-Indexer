# Build context

## Current milestone

**M15 — Operator documentation and system guide**

Status: `in_progress`

## Current work

**WI-0031 — Rewrite operator and architecture documentation** is active on `agent/WI-0031-documentation-rewrite`.

M14 and WI-0025 completed on 2026-08-02. PRs #57–#63 delivered confirmed and exact-model advisory collection queries, explicit any/all and review-state semantics, the responsive Windows/Pixel collection workspace, fixed 480 × 320 server-generated thumbnails, and the complete version-1 neutral manifest. GitHub Actions build #401 passed, and the operator retained detailed private-catalogue counts and representative-result evidence outside Git.

The first WI-0031 slice replaces the stale README direction with one current start-here path and adds `docs/operations/local-operator-guide.md` as the authoritative end-to-end local workflow. The documentation index now treats evaluation, persistence, model and architecture documents as specialized references rather than competing runbooks.

## Next concrete step

1. Rewrite the multi-model runbook around installing, processing and comparing exact FP32 and INT8 revisions while retaining the accepted FP32 recommendation.
2. Reconcile architecture documentation for executables, module boundaries, canonical data, derived artefacts, model revisions, append-only review history and optional Azure scale-out.
3. Add a shared glossary for catalogue, revision, occurrence, exemplar, suggestion, assignment, model revision and bundle.
4. Remove or redirect stale and duplicated guidance.
5. Run `PhotoIdentity.Docs validate`, `generate --check` and the normal CI workflow.
6. Move WI-0031 to review only after all rewrite acceptance criteria are satisfied; WI-0032 will then validate the instructions from a clean Windows setup and trusted-network Pixel path.

## Completed gates

- The 450–550-image baseline pilot passed restart/resume, cross-device review, matcher invariants, deterministic export/evaluation, backup and restore.
- Queue-aware Faces review combines continuous loading, exact-model suggestion ordering, automatic advance, any-person correction, create-and-assign, person audit and preview-first grouped acceptance.
- Windows and Pixel corrective verification passed without horizontal overflow or lost queue navigation.
- Published review smoke protects routes, mutation/audit invariants, privacy boundaries and a multi-page disposable fixture.
- The selectable `sface-2021dec-int8` candidate is pinned, locally installed and verified against a real immutable revision.
- Baseline and candidate embeddings coexist while sharing one canonical catalogue and human review history.
- The same-corpus FP32-versus-INT8 workflow passed exact-provenance, same-source, same-detector-count, deterministic-export and same-split checks.
- A private manual review of 20 representative faces found both revisions correct in every case and no material practical difference.
- Collection queries and the neutral manifest passed automated validation plus private Windows/Pixel and pilot-catalogue verification.

## Delivery objective

Prove as much of the product as possible without Azure:

1. maintain the accepted local processing, review, evaluation and collection workflows;
2. rewrite and independently validate the operator and architecture documentation; and
3. resume Azure execution only after documentation validation and when access is available.

## Relevant planning files

- `docs/delivery/local-first-plan.md`
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
