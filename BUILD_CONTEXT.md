# Build context

## Current milestone

**M08 — Multi-model local evaluation**

Status: `in_progress`

## Current work

- **WI-0019 — Add a second model adapter**

WI-0033 completed human verification on Windows and Pixel on 2026-08-01. The review workflow works as intended, the operator confirmed that sustained manual review is improved, and privacy-sensitive timing reports remain outside Git.

The active branch is `agent/WI-0019-second-embedder`.

## Completed baseline gate

- The 450–550-image local pilot passed restart/resume, cross-device review, matcher invariants, deterministic export/evaluation, backup and restore.
- Queue-aware Faces review combines continuous loading, exact-model suggestion ordering, automatic advance, any-person correction, create-and-assign, person audit and preview-first grouped acceptance.
- Windows and Pixel corrective verification passed without horizontal overflow or lost queue navigation.
- Published review smoke protects routes, mutation/audit invariants, privacy boundaries and a multi-page disposable fixture.
- Local review-time and interaction measurements were captured for both devices and retained outside the repository.

## Active WI-0019 slice

- Add the pinned `sface-2021dec-int8` candidate manifest with immutable source, size, SHA-256, input/output contract, alignment and licence records.
- Persist explicit detector and embedder model IDs in every new batch run while defaulting older saved runs to YuNet FP32 and SFace FP32.
- Add `--detector-model` and `--embedder-model` to `batch start`; resume always reloads the saved exact selection.
- Resolve production manifests by selected model ID and validate the required role before opening model files.
- Reuse unchanged face-occurrence/crop natural keys while storing baseline and candidate embeddings under distinct model ID/hash keys.
- Prove through integration coverage that candidate processing leaves people, confirmed labels and review actions unchanged.
- Document installation, processing, matcher regeneration and same-split evaluation commands.

## Remaining WI-0019 gate

1. merge the selectable-candidate slice after green build, tests and documentation checks;
2. install the pinned candidate model locally;
3. process at least one immutable revision with `--embedder-model sface-2021dec-int8` and confirm exact-model coexistence; and
4. retain only privacy-safe success/failure evidence.

The full baseline-versus-candidate corpus comparison belongs to WI-0030.

## Recently completed

- **WI-0033 — Accelerate the human review workflow** was human-verified on Windows and Pixel on 2026-08-01.
- **WI-0029 — Run a 500-image local acceptance pilot** was human-verified on 2026-07-30.
- **WI-0028 — Export reviewed catalogues to model-lab** was human-verified against the private reviewed catalogue on 2026-07-30.
- **WI-0027 — Complete the local review workflow** was human-verified on Windows and Pixel on 2026-07-30.

Only privacy-safe conclusions are retained in the repository. Private photos, names, crops, embeddings, databases, raw manifests, reports and local paths remain local.

## Delivery objective

Prove as much of the product as possible without Azure:

1. add a second model and repeat the same corpus;
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
