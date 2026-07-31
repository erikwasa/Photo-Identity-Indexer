# Build context

## Current milestone

**M06 — Local evaluation and acceptance**

Status: `in_progress`

## Current work

- **WI-0033 — Accelerate the human review workflow**

The first real Windows and Pixel verification completed on 2026-07-31 and confirmed that manual review is improved. It also exposed correctness defects and mobile overflow. The active corrective branch is `agent/WI-0033-unified-review-ui`.

## Implemented WI-0033 slices

- Enter and the Pixel mobile keyboard action submit person creation through the same guarded path as the Add button.
- Queue-aware details preserve review state, processing run, exact suggestion model revision, deterministic sort and a safe relative return target.
- Accepting a suggestion captures the next eligible face before mutation and advances without returning to the gallery or skipping work.
- Rank-one suggestion person, score and margin are available in the paged query without per-card requests.
- Suggestion review supports ordering by suggested person, margin, score, missing suggestion or creation time with stable tie-breaking.
- Audit pages every active assignment for one person and provides advisory exact-model disagreement signals.
- Preview-first grouped acceptance writes normal assignment actions and linked suggestion-acceptance actions atomically.
- Published smoke covers route/API invariants, linked audit rows and privacy-limited responses.
- `record-review-session.ps1` creates privacy-safe per-device metrics below `.artifacts`.

## Active corrective slice

- Normalize missing query values so Audit details and initial Progress loading cannot pass null to URL encoding.
- Restore accepted suggestions to pending atomically when their assignment is undone.
- Add any-person assignment and inline person creation to face details.
- Consolidate ordinary review, suggestion sorting and grouped suggestion acceptance into Faces.
- Append results continuously instead of using numbered pages in the primary review workspace.
- Remove visible image names, face ordinals, selection text and model hashes from review cards.
- Redirect legacy Suggestions, Bulk suggestions and quick-details URLs into the unified workflow.
- Prevent full hashes or long private image names from creating horizontal overflow on Pixel.

## Remaining WI-0033 gate

1. merge the corrective slice after green build, tests, documentation checks and published smoke;
2. rerun the affected Windows and Pixel interaction checks, especially Audit links, Progress initial load, accept→undo restoration, manual assignment/person creation and portrait overflow;
3. retain only privacy-safe aggregate evidence; and
4. mark WI-0033 complete only after the corrective verification passes.

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
