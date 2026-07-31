# Build context

## Current milestone

**M06 — Local evaluation and acceptance**

Status: `in_progress`

## Current work

- **WI-0033 — Accelerate the human review workflow**

The first real Windows and Pixel verification completed on 2026-07-31 and confirmed that manual review is improved. PR #50 merged the unified continuous review UI and the first defect corrections. The active focused follow-up is `agent/WI-0033-details-auto-advance`, based on additional interactive verification findings.

## Implemented WI-0033 slices

- Enter and the Pixel mobile keyboard action submit person creation through the same guarded path as the Add button.
- Queue-aware details preserve review state, processing run, exact suggestion model revision, deterministic sort and a safe relative return target.
- Accepting a suggestion captures the next eligible face before mutation and advances without returning to the gallery or skipping work.
- Rank-one suggestion person, score and margin are available in the paged query without per-card requests.
- Suggestion review supports ordering by suggested person, margin, score, missing suggestion or creation time with stable tie-breaking.
- Audit pages every active assignment for one person and provides advisory exact-model disagreement signals.
- Preview-first grouped acceptance writes normal assignment actions and linked suggestion-acceptance actions atomically.
- Faces now combines ordinary review, suggestion ordering and grouped suggestion acceptance with continuous loading.
- Published smoke covers route/API invariants, linked audit rows and privacy-limited responses.
- `record-review-session.ps1` creates privacy-safe per-device metrics below `.artifacts`.

## Active focused follow-up

- Capture queue navigation before manual assignment and advance to the captured next face after success.
- Treat new-person creation in details as create-and-assign, then advance without leaving the current face unresolved.
- Use the shorter confirmation text `Created <name>` when details remain on the same face.
- Constrain expanded active-model and ranked-suggestion provenance so long hashes cannot create Pixel horizontal scrolling.
- Increase spacing between the top-suggestion action and detector-confidence bar.
- Expand the disposable interactive fixture beyond one 40-card page and make smoke assert that continuous loading can be exercised.

## Remaining WI-0033 gate

1. merge the focused follow-up after green build, tests, documentation checks and published smoke;
2. rerun manual assignment, create-and-assign, continuous loading and Pixel expanded-provenance checks;
3. retain only privacy-safe aggregate evidence; and
4. mark WI-0033 complete only after the focused corrective verification passes.

## Recently completed

- **WI-0029 — Run a 500-image local acceptance pilot** was human-verified on 2026-07-30. Batch restart/resume, cross-device review, matcher invariants, deterministic export/evaluation, aggregate measurements, backup, restore and cleanup passed on a private representative subset.
- **WI-0028 — Export reviewed catalogues to model-lab** was human-verified against the private reviewed catalogue on 2026-07-30.
- **WI-0027 — Complete the local review workflow** was human-verified on Windows and Pixel on 2026-07-30.

Only privacy-safe conclusions are retained in the repository. Private photos, names, crops, embeddings, databases, raw manifests, reports and local paths remain local.

## Delivery objective

Prove as much of the product as possible without Azure:

1. close the baseline review-throughput gap;
2. add a second model and repeat the same corpus;
3. exercise practical collection queries;
4. rewrite and independently validate the operator and architecture documentation; and
5. resume Azure execution only when access is available.

## Relevant planning files

- `docs/delivery/local-first-plan.md`
- `docs/delivery/milestones/M06-evaluation.md`
- `docs/delivery/work-items/WI-0033-review-throughput.md`
- `docs/delivery/verification/WI-0033-manual-verification.md`
- `docs/delivery/milestones/M08-second-model.md`
- `docs/delivery/milestones/M15-documentation.md`
- `docs/delivery/status/work-items.yaml`
- `docs/delivery/status/milestones.yaml`

## Validation

```powershell
dotnet test
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
./verify-review.ps1 -Mode Smoke -Configuration Release
```
