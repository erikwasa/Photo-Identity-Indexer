# Build context

## Current milestone

**M08 — Multi-model local evaluation**

Status: `in_progress`

## Current work

- **WI-0030 — Run a multi-model local comparison**

WI-0019 completed local human verification on Windows on 2026-08-01 after PR #52 merged. The pinned SFace INT8 model passed exact installation checks, processed the same immutable revision as the FP32 baseline, coexisted by exact model ID/hash and left canonical people, labels and review history unchanged.

The active branch is `agent/WI-0030-multi-model-comparison`.

## Completed gates

- The 450–550-image baseline pilot passed restart/resume, cross-device review, matcher invariants, deterministic export/evaluation, backup and restore.
- Queue-aware Faces review combines continuous loading, exact-model suggestion ordering, automatic advance, any-person correction, create-and-assign, person audit and preview-first grouped acceptance.
- Windows and Pixel corrective verification passed without horizontal overflow or lost queue navigation.
- Published review smoke protects routes, mutation/audit invariants, privacy boundaries and a multi-page disposable fixture.
- The selectable `sface-2021dec-int8` candidate is pinned, locally installed and verified against a real immutable revision.
- Baseline and candidate embeddings coexist while sharing one canonical catalogue and human review history.

## Active WI-0030 slice

- Back up the accepted pilot catalogue and retain the same immutable source scope.
- Process the complete corpus with `sface-2021dec-int8` into a separate output root while using the existing catalogue.
- Regenerate suggestions independently for exact FP32 and INT8 model revisions.
- Verify the browser makes the active suggestion revision unmistakable and cannot overwrite another revision's suggestions or human decisions.
- Export and evaluate both models with the same dataset ID, pipeline version, split seed, source scope and split settings.
- Compare detector counts, identification metrics, unknown rejection, confusion, throughput, storage and operator review effort.
- Review representative disagreements without using held-out test results to tune thresholds.
- Record a privacy-safe recommendation, remaining uncertainty and whether M11 needs a larger evaluation set.

## Remaining WI-0030 gate

1. complete candidate processing over the accepted corpus;
2. prove same-split deterministic exports and reports for both exact revisions;
3. verify exact-model distinction in the Windows and Pixel review workflow;
4. review representative disagreements and aggregate practical impact; and
5. retain only privacy-safe comparison evidence and the final recommendation.

## Recently completed

- **WI-0019 — Add a second model adapter** was human-verified locally on Windows on 2026-08-01.
- **WI-0033 — Accelerate the human review workflow** was human-verified on Windows and Pixel on 2026-08-01.
- **WI-0029 — Run a 500-image local acceptance pilot** was human-verified on 2026-07-30.
- **WI-0028 — Export reviewed catalogues to model-lab** was human-verified against the private reviewed catalogue on 2026-07-30.

Only privacy-safe conclusions are retained in the repository. Private photos, names, crops, embeddings, databases, raw manifests, reports and local paths remain local.

## Delivery objective

Prove as much of the product as possible without Azure:

1. repeat the accepted corpus with the second model and record a recommendation;
2. exercise practical collection queries;
3. rewrite and independently validate the operator and architecture documentation; and
4. resume Azure execution only when access is available.

## Relevant planning files

- `docs/delivery/local-first-plan.md`
- `docs/delivery/milestones/M08-second-model.md`
- `docs/delivery/work-items/WI-0019-second-model.md`
- `docs/delivery/work-items/WI-0030-multi-model-comparison.md`
- `docs/models/candidate-models.md`
- `docs/operations/local-evaluation.md`
- `docs/delivery/status/work-items.yaml`
- `docs/delivery/status/milestones.yaml`

## Validation

```powershell
dotnet test
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
./verify-review.ps1 -Mode Smoke -Configuration Release
```
