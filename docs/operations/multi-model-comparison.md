# Multi-model comparison workflow

This is the authoritative runbook for comparing two or more embedding-model revisions over one immutable source scope and one canonical human review history.

Use the [local operator guide](local-operator-guide.md) first to create, process and review the baseline catalogue. The comparison workspace, source snapshot, database backup, model outputs, raw manifests, reports and manual notes are private and must remain outside Git.

## Comparison boundary

A valid comparison keeps these inputs fixed:

- source root and immutable asset revisions;
- detector model ID and exact hash;
- face-alignment protocol;
- people, confirmed assignments, rejections and append-only review history;
- evaluation dataset ID, pipeline version, split seed, split counts and threshold sweep; and
- manual-review procedure.

Only the embedding-model revision changes.

The accepted first comparison uses:

| Role | Baseline | Candidate |
|---|---|---|
| Detector | `yunet-2023mar-fp32` | `yunet-2023mar-fp32` |
| Embedder | `sface-2021dec-fp32` | `sface-2021dec-int8` |
| Alignment | `sface-five-point-v1` | `sface-five-point-v1` |

A model revision means the model ID plus its exact SHA-256 hash and preprocessing contract. Scores and thresholds from different revisions are not interchangeable.

## Automated workflow

Run `Invoke-MultiModelComparison.ps1` rather than manually repeating batch, matcher and evaluation commands. The workflow supports Windows PowerShell 5.1 and PowerShell 7.

The script:

- validates pinned detector and embedder manifests and installed file hashes;
- optionally installs models and runs the normal local and review preflight gates;
- creates a content-hashed source snapshot;
- backs up the SQLite catalogue and optional WAL/SHM sidecars;
- processes the same catalogue and source scope with every configured embedder;
- resumes persisted runs without changing their selected models;
- asserts identical immutable revision scope and detector-derived face counts;
- regenerates suggestions for each exact embedder revision twice and rejects unstable results;
- exports and evaluates every model twice and rejects nondeterministic bytes;
- asserts identical source, gallery, validation and held-out test splits;
- rejects comparative throughput when measured timing is unavailable; and
- writes aggregate metrics, storage measurements and exact model provenance into one private comparison workspace.

The script does not decide whether a model is acceptable. Windows/Pixel inspection, representative disagreement review and the final recommendation remain human gates.

## 1. Create a private configuration

Copy the checked-in example outside the repository:

```powershell
Copy-Item `
  .\docs\operations\examples\multi-model-comparison.example.json `
  C:\PhotoIdentityPilot\multi-model-comparison.json

notepad C:\PhotoIdentityPilot\multi-model-comparison.json
```

Set:

- `sourcePath` to the accepted private source scope;
- `databasePath` to the canonical reviewed catalogue;
- `workspacePath` to a new directory outside both the repository and source tree;
- `detectorModelId` to the fixed detector revision;
- `models` to the baseline and candidates; and
- `evaluation` to the accepted dataset, pipeline, seed, split counts and threshold sweep.

The default example already declares:

```json
{
  "detectorModelId": "yunet-2023mar-fp32",
  "models": [
    {
      "name": "baseline",
      "modelId": "sface-2021dec-fp32"
    },
    {
      "name": "candidate-int8",
      "modelId": "sface-2021dec-int8"
    }
  ]
}
```

Add another governed candidate by appending another name and checked-in model ID. Every ID must resolve to a manifest under `models/manifests` with the correct detector or embedder role.

Do not create a separate candidate database. All revisions must coexist in one canonical catalogue so they share people and review history while keeping model-derived embeddings and suggestions separate.

## 2. Stop writers and run the comparison

Stop the review host, CLI workers and any process writing to the catalogue. Then run from the repository root:

```powershell
.\Invoke-MultiModelComparison.ps1 `
  -ConfigPath C:\PhotoIdentityPilot\multi-model-comparison.json `
  -InstallModels `
  -RunPreflight
```

Expected success signals include:

- every model file matches its pinned size and SHA-256;
- Release build, tests, documentation validation and review smoke checks pass;
- the source snapshot and stopped-state catalogue backup are created;
- each run reports its configured detector and embedder IDs;
- the candidate reuses the same immutable source and detector scope;
- baseline and candidate embeddings coexist rather than overwrite one another;
- exact-model suggestion regeneration is deterministic;
- evaluation manifests and reports are deterministic; and
- `comparison-summary.json` and `manual-verification.md` are written.

ONNX Runtime can write optimization warnings to native stderr while returning a successful exit code. The workflow records those warnings and uses the native process exit code to determine success.

## 3. Resume an interrupted comparison

Run the same configuration with `-Resume`:

```powershell
.\Invoke-MultiModelComparison.ps1 `
  -ConfigPath C:\PhotoIdentityPilot\multi-model-comparison.json `
  -Resume
```

Completed phases are reused. If a batch stopped before workflow state was written, the script can recover exactly one persisted run under the corresponding private model output. It refuses automatic recovery when multiple possible runs make the intended run ambiguous.

Resume revalidates:

- source snapshot identity;
- exact model manifests and installed hashes;
- immutable processing scope;
- detector counts; and
- evaluation split identity.

To abandon the comparison, stop all writers, restore the database and optional sidecars from the workspace backup, remove the incomplete workspace and restart without `-Resume`.

## 4. Inspect the machine-generated evidence

The configured workspace contains:

- a content-hashed source snapshot;
- a stopped-state catalogue backup;
- per-model batch output and logs;
- raw evaluation manifests and reports;
- exact model and split provenance;
- `comparison-summary.json`; and
- `manual-verification.md`.

View the aggregate summary without copying private content into the repository:

```powershell
$workspace = "C:\PhotoIdentityPilot\comparisons\sface-fp32-vs-int8"
$summary = Get-Content `
  (Join-Path $workspace "comparison-summary.json") -Raw | ConvertFrom-Json

$summary | Format-List *
```

Compare only like-for-like evidence:

- identical immutable revision and detector-derived face counts;
- exact embedder IDs and hashes;
- validation-selected thresholds for each revision;
- held-out identification and unknown-rejection results;
- confusion and close-second-person cases;
- measured processing and embedding throughput;
- model-file and derived-storage sizes; and
- deterministic manifest and report hashes.

A lower threshold or a different score distribution is not intrinsically better. Each revision selects and reports thresholds under the same evaluation procedure.

## 5. Complete the human gates

Open the generated `manual-verification.md`, publish the review application against the configured catalogue, and complete:

1. **Windows exact-model verification** — select each exact model revision, confirm provenance and verify unchanged canonical people and review state.
2. **Pixel verification** — confirm portrait/landscape layout, selector usability, navigation and no horizontal overflow.
3. **Representative disagreement review** — classify sampled differences as useful, neutral or harmful under both exact revisions.
4. **Operational comparison** — consider throughput, storage and review effort alongside quality metrics.
5. **Recommendation** — record the selected default, remaining uncertainty and whether broader evaluation is justified.

Suggestions are advisory. No threshold or comparison result may create, replace or remove a canonical human assignment automatically.

Do not commit private photos, names, face IDs, SQLite files, snapshots, raw manifests, reports, embeddings, local paths or per-person confusion rows.

## 6. Accepted FP32-versus-INT8 outcome

The accepted private same-corpus comparison kept detector, source scope, alignment, dataset, seed and review history fixed. A manual review of 20 representative faces found both SFace revisions correct in every case, with no material practical identification or review-quality advantage for INT8.

Retain `sface-2021dec-fp32` as the current default embedder. Keep `sface-2021dec-int8` as a governed candidate for later runtime, Azure-consistency, cost or broader-diversity evidence.

This recommendation does not invalidate or delete candidate embeddings and reports. Final production model selection remains a later decision after optional Azure and broader-corpus evidence.

## 7. Cleanup and retention

Keep the comparison workspace only as long as its private evidence remains useful. Confirm the accepted catalogue backup before removing temporary source snapshots, transfer artefacts or candidate outputs.

Removing a candidate ONNX file prevents future inference with that revision but does not alter persisted people, assignments, rejections, append-only review history or baseline embeddings.

## Workflow self-test

Run after changing the script or configuration contract:

```powershell
powershell.exe -NoProfile `
  -File .\Invoke-MultiModelComparison.ps1 -SelfTest

pwsh -NoProfile `
  -File .\Invoke-MultiModelComparison.ps1 -SelfTest
```

The self-test verifies PowerShell 5.1-compatible relative paths, directory sizing, manifest roles and successful native commands that write warning text to stderr. Repository CI runs it under Windows PowerShell 5.1 and PowerShell 7.

## Related references

- [Local operator guide](local-operator-guide.md)
- [Local evaluation workflow](local-evaluation.md)
- [Baseline models](../models/baseline-models.md)
- [Candidate models](../models/candidate-models.md)
- [Model manifests and governance](../models/model-governance.md)
- [Recognition and identity matching](../architecture/identity-matching.md)
- [Glossary](../glossary.md)
