# Build context

## Current milestone

**M06 — Local evaluation and acceptance**

Status: `ready`

## Next ready work

- **WI-0033 — Accelerate the human review workflow**

The 500-image baseline pilot passed, but sustained review was too click-heavy on Windows and Pixel. WI-0033 is the explicit fix-before-proceeding gate before adding a second model or collection-ready queries.

## Recently completed

- **WI-0029 — Run a 500-image local acceptance pilot** was human-verified on 2026-07-30. Batch restart/resume, cross-device review, matcher invariants, deterministic export/evaluation, aggregate measurements, backup, restore and cleanup passed on a private representative subset.
- **WI-0028 — Export reviewed catalogues to model-lab** was human-verified against the private reviewed catalogue on 2026-07-30.
- **WI-0027 — Complete the local review workflow** was human-verified on Windows and Pixel on 2026-07-30.

Only privacy-safe conclusions are retained in the repository. Private photos, names, crops, embeddings, databases, raw manifests, reports and local paths remain local.

## Accepted pilot finding

The product is functionally correct, but review is too slow because:

- creating a person requires a pointer/touch action rather than native form submission;
- accepting a suggestion reloads the same details page instead of advancing;
- details pages do not preserve queue position or provide Previous and Next navigation;
- gallery cards do not expose top suggestions;
- assigned faces cannot be audited by person; and
- bulk review is not organised around suggestion groups.

Elapsed review time was not captured, so WI-0033 includes a fresh 50–100-face throughput measurement on both device types.

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

## Validation for this planning change

```powershell
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
```
