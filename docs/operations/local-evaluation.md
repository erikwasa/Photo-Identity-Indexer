# Local evaluation workflow

This runbook describes the local acceptance path for the baseline model and reviewed catalogue.

## 1. Prepare an isolated workspace

Use paths outside the photo source root so outputs cannot be scanned as inputs.

```powershell
$root = "C:\PhotoIdentityPilot"
$source = Join-Path $root "source"
$output = Join-Path $root "outputs"
$db = Join-Path $root "catalogue.db"
$publish = Join-Path $root "review-app"
$evaluation = Join-Path $root "model-lab"

New-Item -ItemType Directory -Force -Path $source,$output,$evaluation | Out-Null
```

Copy or stage 450–550 representative private images into `$source`. Do not place private files under the repository.

## 2. Verify the installation

```powershell
./verify-local.ps1 -InstallModels
./verify-review.ps1 -Mode Smoke -Configuration Release
```

Expected success signals include model hash verification, Release build success, passing tests, valid living documentation and passing disposable review-application smoke checks.

## 3. Process the subset

```powershell
dotnet run --project src/PhotoIdentity.Cli -- `
  batch start `
  --database $db `
  --source $source `
  --output $output
```

Record the printed run ID. Exercise status and resume before accepting the pilot:

```powershell
dotnet run --project src/PhotoIdentity.Cli -- `
  batch status --database $db --run RUN_ID

dotnet run --project src/PhotoIdentity.Cli -- `
  batch resume --database $db --run RUN_ID
```

Use `--max-attempts COUNT` on start or resume when intentionally proving bounded restart and resume behavior.

## 4. Publish and run the browser application

```powershell
Remove-Item $publish -Recurse -Force -ErrorAction SilentlyContinue

dotnet publish `
  .\src\PhotoIdentity.Api\PhotoIdentity.Api.csproj `
  --configuration Release `
  --output $publish

$env:PhotoIdentity__DatabasePath = $db
Push-Location $publish
dotnet .\PhotoIdentity.Api.dll --urls "http://0.0.0.0:5080"
```

Use `http://localhost:5080` on Windows. On a trusted private network, use the computer's LAN address from the Pixel and restrict any firewall rule to the intended private profile. The application is unauthenticated and must not be exposed to an untrusted network.

Exercise individual assignment, rejection and undo; ranked suggestion review; person rename and merge; preview-first bulk actions; and combined progress filters. No threshold may create a label automatically.

## 5. Regenerate ranked identity suggestions

Stop or leave the review host running, then regenerate suggestions from another PowerShell window against the same catalogue. Supply the exact embedding model ID and SHA-256 revision used by the processed faces.

```powershell
$embedder = Get-Content `
  .\models\manifests\sface-2021dec-fp32.json -Raw | ConvertFrom-Json

dotnet run --project src/PhotoIdentity.Cli -- `
  match regenerate `
  --database $db `
  --embedder-id $embedder.modelId `
  --embedder-hash $embedder.sha256
```

The command prints the exact model revision plus target and suggestion counts. It rebuilds ranked suggestions from current human-confirmed exemplars, preserves rejected face-person exclusions and never creates or changes canonical labels.

During acceptance testing, reject at least one suggestion, run regeneration again and verify that the rejected pair does not reappear. Also verify that accepted assignments and append-only review history remain unchanged.

## 6. Export evaluation data

```powershell
$detector = Get-Content `
  .\models\manifests\yunet-2023mar-fp32.json -Raw | ConvertFrom-Json

$manifest = Join-Path $evaluation "baseline.json"
$report = Join-Path $evaluation "baseline-report.json"

dotnet run --project src/PhotoIdentity.Cli -- `
  evaluate export `
  --database $db `
  --output $manifest `
  --dataset-id private-baseline-v1 `
  --pipeline-version local-pipeline-v1 `
  --detector-id $detector.modelId `
  --detector-hash $detector.sha256 `
  --embedder-id $embedder.modelId `
  --embedder-hash $embedder.sha256 `
  --seed private-baseline-split-v1 `
  --run RUN_ID

dotnet run --project src/PhotoIdentity.Cli -- `
  evaluate `
  --dataset $manifest `
  --output $report
```

Validation chooses thresholds; the held-out test split only reports final metrics. Repeat both commands with unchanged inputs and compare SHA-256 hashes to prove deterministic manifest and report bytes.

## 7. Capture pilot evidence — WI-0029

Record privacy-safe aggregate evidence:

- image, revision, face and review-state counts;
- unsupported or unavailable media counts;
- batch and inference throughput;
- database and artefact-store sizes;
- review time and repetitive-action observations;
- suggestion precision, unknown rejection and confusion summaries;
- matcher regeneration counts and rejected-pair preservation;
- defects, severity and disposition;
- backup and restore result.

Do not commit the database, images, crops, embeddings, names, real manifests or real reports.

## 8. Repeat with a candidate model — WI-0019 and WI-0030

Process the same immutable revisions with the baseline and candidate model. Never overwrite baseline results. Use the same reviewed people and the same fixed evaluation split, and make the selected model revision explicit in both UI and reports.

## 9. Cleanup and retention

Keep the canonical pilot database and privacy-reviewed aggregate notes only as long as they remain useful. Remove temporary publish directories, transfer archives and redundant derived outputs after confirming backups. Original photos must remain unchanged throughout the workflow.
