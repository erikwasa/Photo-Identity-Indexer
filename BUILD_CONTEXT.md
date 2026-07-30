# Build context

## Current milestone

**M06 — Local evaluation and acceptance**

Status: `in_progress`

## Current work

- **WI-0033 — Accelerate the human review workflow**

The first delivery slice is in progress on `agent/WI-0033-queue-navigation`. It replaces pointer-only person creation with native form submission and turns face details into a continuous, scope-aware review queue.

## Implemented in the current slice

- Enter and the Pixel mobile keyboard action submit person creation through the same guarded path as the Add button.
- Gallery and progress links preserve review state, processing run, exact suggestion model revision, deterministic sort and a safe relative return target.
- The API returns server-calculated Previous and Next face IDs plus queue position and total.
- Accepting a suggestion captures the next eligible face before mutation and advances without returning to the gallery or skipping work.
- Integration coverage protects scope intersection, mutation-stable navigation, invalid-sort rejection and privacy-limited responses.

## Remaining WI-0033 slices

1. expose top suggestions and suggestion-aware ordering in the gallery;
2. add per-person assigned-face audit;
3. add preview-first grouped suggestion acceptance with linked audit actions; and
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