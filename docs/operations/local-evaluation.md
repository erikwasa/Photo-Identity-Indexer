# Local evaluation workflow

This runbook describes the intended local acceptance path. Sections labelled **available now** can be exercised on the current `main` branch. Sections labelled **planned gate** require the listed work item before the formal 500-image pilot begins.

## 1. Prepare an isolated workspace — available now

Use paths outside the photo source root so outputs cannot be scanned as inputs.

```powershell
$root = "C:\PhotoIdentityPilot"
$source = Join-Path $root "source"
$output = Join-Path $root "outputs"
$db = Join-Path $root "catalogue.db"
$publish = Join-Path $root "review-app"

New-Item -ItemType Directory -Force -Path $source,$output | Out-Null
```

Copy or stage 450–550 representative private images into `$source`. Do not place private files under the repository.

## 2. Verify the installation — available now

```powershell
./verify-local.ps1 -InstallModels
```

Expected success signals include model hash verification, Release build success, passing tests and valid living documentation.

## 3. Process the subset — available now

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

## 4. Publish and run the browser application — available now

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

The current application supports gallery filters, person creation, assignment, rejection, undo and details. Stop the process before `Pop-Location`.

## 5. Complete the sustained-review features — planned gate WI-0027

The formal pilot waits for:

- ranked suggestions in the browser;
- accept/reject suggestion actions;
- person rename and merge;
- safe bulk review;
- run and model-revision filters; and
- progress summaries suitable for hundreds of images.

No threshold may create a label automatically.

## 6. Export evaluation data — planned gate WI-0028

The current `evaluate` command accepts a prepared manifest, but `main` does not yet generate that manifest from the reviewed catalogue. WI-0028 adds a deterministic catalogue export so the operator does not manually copy embeddings or identity identifiers.

After that gate, run:

```powershell
dotnet run --project src/PhotoIdentity.Cli -- `
  evaluate `
  --dataset "C:\PhotoIdentityPilot\model-lab\baseline.json" `
  --output "C:\PhotoIdentityPilot\model-lab\baseline-report.json"
```

Validation chooses thresholds; the held-out test split only reports final metrics.

## 7. Capture pilot evidence — WI-0029

Record privacy-safe aggregate evidence:

- image, revision, face and review-state counts;
- unsupported or unavailable media counts;
- batch and inference throughput;
- database and artefact-store sizes;
- review time and repetitive-action observations;
- suggestion precision, unknown rejection and confusion summaries;
- defects, severity and disposition;
- backup and restore result.

Do not commit the database, images, crops, embeddings, names or real reports.

## 8. Repeat with a candidate model — WI-0019 and WI-0030

Process the same immutable revisions with the baseline and candidate model. Never overwrite baseline results. Use the same reviewed people and the same fixed evaluation split, and make the selected model revision explicit in both UI and reports.

## 9. Cleanup and retention

Keep the canonical pilot database and privacy-reviewed aggregate notes only as long as they remain useful. Remove temporary publish directories, transfer archives and redundant derived outputs after confirming backups. Original photos must remain unchanged throughout the workflow.
