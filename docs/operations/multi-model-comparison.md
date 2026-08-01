# Reproducible multi-model comparison

Use `Invoke-MultiModelComparison.ps1` for WI-0030 and later local embedder comparisons. It replaces the long copy-and-paste procedure with one private JSON configuration and one resumable command.

The workflow supports Windows PowerShell 5.1 and PowerShell 7. It deliberately avoids `[System.IO.Path]::GetRelativePath()`, which is unavailable in the .NET Framework runtime used by Windows PowerShell 5.1.

## Automated boundary

The script:

- validates pinned detector and embedder manifests and installed file hashes;
- optionally installs models and runs the existing local/review preflight gates;
- creates a content-hashed source snapshot with a PowerShell 5.1-compatible relative-path helper;
- backs up the SQLite catalogue and WAL/SHM sidecars;
- processes the same catalogue and source with two or more embedders;
- asserts identical immutable revision scope and detector counts;
- regenerates exact-model suggestions twice and rejects unstable results;
- exports and evaluates every model twice and rejects nondeterministic bytes;
- asserts identical source, gallery, validation and held-out test splits;
- rejects comparative throughput when export timing fallback was used;
- writes aggregate metrics, storage and exact model provenance below one private workspace.

Windows/Pixel visual checks, representative disagreement judgments and the final recommendation remain human gates.

## Configure

Copy the example outside the repository:

```powershell
Copy-Item `
  .\docs\operations\examples\multi-model-comparison.example.json `
  C:\PhotoIdentityPilot\wi-0030.json

notepad C:\PhotoIdentityPilot\wi-0030.json
```

Set `sourcePath`, `databasePath` and a new `workspacePath` outside the source tree. Preserve the accepted dataset ID, pipeline version, split seed, split counts and threshold sweep.

Add more models by appending entries:

```json
{
  "name": "candidate-example",
  "modelId": "another-pinned-embedder-id"
}
```

Every model ID must have a checked-in manifest under `models/manifests`.

## Run

Stop any process writing to the catalogue, then run from the repository root:

```powershell
.\Invoke-MultiModelComparison.ps1 `
  -ConfigPath C:\PhotoIdentityPilot\wi-0030.json `
  -InstallModels `
  -RunPreflight
```

The configured workspace receives the source snapshot, database backup, per-model outputs and logs, raw manifests and reports, `comparison-summary.json`, and `manual-verification.md`. Keep the whole workspace outside Git.

## Resume

After an interruption, run:

```powershell
.\Invoke-MultiModelComparison.ps1 `
  -ConfigPath C:\PhotoIdentityPilot\wi-0030.json `
  -Resume
```

Completed per-model state is reused. The source snapshot, immutable processing scope, detector counts and evaluation split are checked again.

## Complete the human gates

Open the generated `manual-verification.md`, publish the review application against the configured catalogue, and complete:

1. Windows exact-model selection, provenance and unchanged canonical review-state checks.
2. Pixel portrait selector, navigation and no-horizontal-scroll checks.
3. Representative disagreement review using useful, neutral and harmful dispositions.
4. A privacy-safe recommendation, remaining uncertainty and larger-evaluation decision.

Do not commit private photos, names, face IDs, SQLite files, source snapshots, raw manifests, reports, embeddings, local paths or per-person confusion rows.

## Compatibility self-test

Run after changing the workflow:

```powershell
powershell.exe -NoProfile -File .\Invoke-MultiModelComparison.ps1 -SelfTest
pwsh -NoProfile -File .\Invoke-MultiModelComparison.ps1 -SelfTest
```

Repository CI runs the Windows PowerShell self-test so the original `GetRelativePath` failure cannot regress unnoticed.
