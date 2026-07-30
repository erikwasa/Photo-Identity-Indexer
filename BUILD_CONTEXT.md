# Build context

## Current milestone

**M06 — Local evaluation and acceptance**

Status: `in_progress`

## Current work

- **WI-0033 — Accelerate the human review workflow**

Queue-aware details review is merged. The next slice is on `agent/WI-0033-suggestion-gallery` and adds a dedicated exact-model suggestion workspace for scanning, ordering and accepting top matches without opening every face.

## Implemented WI-0033 slices

- Enter and the Pixel mobile keyboard action submit person creation through the same guarded path as the Add button.
- Gallery and progress links preserve review state, processing run, exact suggestion model revision, deterministic sort and a safe relative return target.
- The API returns server-calculated Previous and Next face IDs plus queue position and total.
- Accepting a suggestion captures the next eligible face before mutation and advances without returning to the gallery or skipping work.
- The Suggestions workspace returns rank-one pending matches, scores, margins and exact model provenance in one paged response.
- Suggestion review can be ordered by suggested person, high or low score margin, score, missing suggestion or creation time with stable tie-breaking.
- Clear matches can be accepted directly from cards; ambiguous matches open an ordered quick-details queue.
- Integration coverage protects exact-model requirements, scoped ordering, mutation-stable navigation and privacy-limited responses.

## Remaining WI-0033 slices

1. add per-person assigned-face audit;
2. add preview-first grouped suggestion acceptance with linked audit actions;
3. extend published-application smoke coverage across the completed workflow; and
4. measure a fresh 50–100-face queue on Windows and Pixel.

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
