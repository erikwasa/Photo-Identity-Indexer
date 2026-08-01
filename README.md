# Photo Identity Indexer

A private, local-first system for detecting and identifying people in a personal photo archive.

The Windows computer is the trusted control plane. It owns the SQLite catalogue, people, human review history and derived artefacts; runs the CLI, worker, API and browser UI; and can perform the complete functional workflow without Azure. Optional Azure compute is limited to explicit portable processing bundles and never authenticates to personal OneDrive.

## Current direction

The evaluation harness from WI-0017 is complete. Delivery is now intentionally focused on local acceptance because Azure resources will be unavailable for a period.

The next ready items are:

- **WI-0027 — Complete the local review workflow**: expose ranked suggestions, person maintenance, bulk review, progress and model-revision filters in the Windows/Pixel browser UI.
- **WI-0028 — Export reviewed catalogues to model-lab**: produce reproducible gallery, validation and held-out test manifests from the SQLite catalogue.

After those items, the project will run a 450–550 image baseline pilot, add a second model, repeat the same corpus for comparison, exercise collection queries, and rewrite and validate the documentation. Azure work is deferred until that local phase is complete and access is available.

Read the [local-first delivery plan](docs/delivery/local-first-plan.md) for the complete sequence and acceptance boundaries.

## Start here

- [Documentation index](docs/index.md)
- [Local evaluation workflow](docs/operations/local-evaluation.md)
- [Local-first delivery plan](docs/delivery/local-first-plan.md)
- [Architecture overview](docs/architecture/overview.md)
- [Current delivery status](docs/delivery/status/current.md)
- [Build context](BUILD_CONTEXT.md)

## Prerequisites

- Windows
- .NET 10 SDK
- PowerShell 7 or Windows PowerShell
- Local disk space for SQLite, aligned crops, embeddings and reports
- A trusted private network for Pixel browser testing
- Personal photos kept outside the repository

## Build and synthetic verification

```powershell
./build.ps1
./test.ps1
./verify-local.ps1 -InstallModels
./verify-review.ps1 -Mode Smoke -Configuration Release
```

The local verification paths use synthetic fixtures or explicitly supplied private files. Generated biometric data must not be committed.

## Process a local subset

Keep the output outside the source directory:

```powershell
$root = "C:\PhotoIdentityPilot500"
$source = Join-Path $root "source"
$output = Join-Path $root "outputs"
$db = Join-Path $root "catalogue.db"

New-Item -ItemType Directory -Force -Path $source,$output | Out-Null

dotnet run --project src/PhotoIdentity.Cli -- `
  batch start `
  --database $db `
  --source $source `
  --output $output
```

Use the printed run ID for status and resume:

```powershell
dotnet run --project src/PhotoIdentity.Cli -- `
  batch status --database $db --run RUN_ID

dotnet run --project src/PhotoIdentity.Cli -- `
  batch resume --database $db --run RUN_ID
```

Batch processing scans current immutable revisions, records unsupported or unavailable media, and persists faces and embeddings without modifying the source files.

## Run the browser review application

Publish the hosted Blazor client and API before running against the catalogue:

```powershell
$publish = Join-Path $root "review-app"
Remove-Item $publish -Recurse -Force -ErrorAction SilentlyContinue

dotnet publish `
  .\src\PhotoIdentity.Api\PhotoIdentity.Api.csproj `
  --configuration Release `
  --output $publish

$env:PhotoIdentity__DatabasePath = $db
Push-Location $publish
dotnet .\PhotoIdentity.Api.dll --urls "http://0.0.0.0:5080"
```

Open `http://localhost:5080` on Windows. A Pixel on the same trusted private network can use the computer's LAN address when the firewall permits the port for that private profile.

The current UI supports gallery filters, person creation, assignment, rejection, undo and privacy-limited details. Ranked suggestion review, person rename/merge, bulk actions and model-revision filters are planned in WI-0027. The application is unauthenticated; do not expose it to an untrusted network.

## Run the current evaluation harness

The checked-in fixture is synthetic:

```powershell
dotnet run --project src/PhotoIdentity.Cli -- `
  evaluate `
  --dataset tools/model-lab/example-dataset.json `
  --output .artifacts/model-lab/example-report.json
```

Validation selects thresholds and the held-out test split reports final metrics. The current command consumes a prepared manifest. WI-0028 will generate that manifest deterministically from a reviewed catalogue.

## Portable processing bundles

Portable bundles are complete and can run on another local machine now or on Azure later. Export, process and import remain model-derived operations; people and human review history never enter the worker bundle.

See [portable processing bundles](docs/architecture/portable-bundles.md). Azure execution is optional and deliberately deferred while access is unavailable.

## Privacy

Do not commit personal photos, names, face crops, embeddings, SQLite catalogues, model binaries, credentials, SAS tokens, real evaluation manifests or model-lab reports. See [security and privacy](docs/architecture/security-and-privacy.md).
