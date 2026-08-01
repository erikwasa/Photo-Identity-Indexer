# Build context

## Current milestone

**M14 — Collection-ready API**

Status: `in_progress`

## Current work

**WI-0025 — Add collection-ready queries** remains active.

PRs #57–#59 established confirmed, suggestion-backed and explicit review-state collection queries. PR #60 added the first local `/collections` workspace. Its build passed, but the first real Windows acceptance attempt identified usability blockers: the people list was too long, checkbox alignment was poor, scan-observation dates were misleading, and text-only manifest cards were not useful for browsing photos.

The active correction is `agent/WI-0025-collection-usability-content`.

It replaces the people grid with a searchable checkbox dropdown, removes catalogue-observation dates from the browser, streams locally available photos through opaque revision URLs, and renders responsive photo cards without exposing source paths. The API retains date filtering for programmatic consumers because `asset_revisions.observed_at_utc` is catalogue observation time rather than capture time.

## Next concrete step

1. Run the full Release build, integration suite, living-document validation and published Windows smoke path in GitHub Actions.
2. Re-run `/collections` on Windows and Pixel against the accepted private catalogue.
3. Record any/all counts, representative results, photo loading, selector usability and no-horizontal-overflow evidence.
4. Complete WI-0025 only after device acceptance, neutral-consumer verification and pilot count checks all pass.

## Completed gates

- The 450–550-image baseline pilot passed restart/resume, cross-device review, matcher invariants, deterministic export/evaluation, backup and restore.
- Queue-aware Faces review combines continuous loading, exact-model suggestion ordering, automatic advance, any-person correction, create-and-assign, person audit and preview-first grouped acceptance.
- Windows and Pixel corrective verification passed without horizontal overflow or lost queue navigation.
- Published review smoke protects routes, mutation/audit invariants, privacy boundaries and a multi-page disposable fixture.
- The selectable `sface-2021dec-int8` candidate is pinned, locally installed and verified against a real immutable revision.
- Baseline and candidate embeddings coexist while sharing one canonical catalogue and human review history.
- The same-corpus FP32-versus-INT8 workflow passed exact-provenance, same-source, same-detector-count, deterministic-export and same-split checks.
- A private manual review of 20 representative faces found both revisions correct in every case and no material practical difference.

## WI-0030 outcome

WI-0030 and M08 completed on 2026-08-01.

The private comparison kept `yunet-2023mar-fp32`, SFace five-point alignment, the immutable source scope, dataset ID, pipeline version, split seed and split settings fixed while comparing:

- baseline: `sface-2021dec-fp32`;
- candidate: `sface-2021dec-int8`.

Exact-model suggestions remained distinguishable and could not overwrite one another or alter people, assignments, rejections or append-only audit history. The operator retained the detailed workbook and raw comparison artefacts outside Git.

The recommendation is to retain `sface-2021dec-fp32` as the current default embedding model. INT8 remains a governed candidate, but it did not demonstrate a material identification or review-quality advantage on the accepted private corpus. No larger local evaluation is required before proceeding to collection-ready queries. Final production model selection remains deferred to M11 and later Azure consistency, cost and broader-diversity evidence.

## Delivery objective

Prove as much of the product as possible without Azure:

1. exercise practical collection queries against the accepted catalogue;
2. rewrite and independently validate the operator and architecture documentation; and
3. resume Azure execution only when access is available.

## Relevant planning files

- `docs/delivery/local-first-plan.md`
- `docs/delivery/milestones/M14-collection-api.md`
- `docs/delivery/work-items/WI-0025-collection-api.md`
- `docs/delivery/status/work-items.yaml`
- `docs/delivery/status/milestones.yaml`

## Validation

```powershell
dotnet test
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
./verify-review.ps1 -Mode Smoke -Configuration Release
```
