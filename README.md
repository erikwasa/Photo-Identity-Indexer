# Photo Identity Indexer

A private, model-independent system for detecting and identifying people in a personal photo archive.

The project is a local-first modular .NET application. Personal OneDrive is accessed through the Windows sync client. Optional Azure compute receives explicit portable job bundles and does not authenticate to OneDrive or use Azure application identities.

## Project status

**M06 — Evaluation harness** is active through WI-0017, adding reproducible gallery, validation and held-out test reporting for the exact matcher. **M05 — Identity matching** and **M07 — Portable job bundles** are complete.

M01 single-image inference, M02 local catalogue and durable processing, M03 OneDrive availability and verified staging, M04 local review, M05 identity matching, and M07 portable bundles are complete and verified. M09, the first Azure VM pilot without identities, is also ready but is not the active implementation track.

- [Documentation index](docs/index.md)
- [Current build context](BUILD_CONTEXT.md)
- [Roadmap](docs/delivery/roadmap.md)
- [Canonical work-item status](docs/delivery/status/work-items.yaml)
- [SQLite persistence operations](docs/operations/sqlite-persistence.md)

## Prerequisites

- .NET 10 SDK
- PowerShell 7 or Windows PowerShell
- Windows for the current OpenCV native-runtime, OneDrive Files On-Demand and review-host verification paths

## Build and test

```powershell
./build.ps1
./test.ps1
```

## Local verification checkpoint

Install and verify the pinned models, build the solution, run all tests and validate the living documentation:

```powershell
./verify-local.ps1 -InstallModels
```

Verify real private images without committing them to the repository. Array-valued parameters such as `-Image` must be supplied once with all values:

```powershell
./verify-local.ps1 `
  -Image "C:\PrivateVerification\normal.jpg","C:\PrivateVerification\pixel-rotated.jpg","C:\PrivateVerification\sample.png" `
  -UnsupportedImage "C:\PrivateVerification\sample.heic"
```

Outputs and privacy-safe reports are written below ignored `.artifacts/local-verification`. Inspect the generated PNGs manually and confirm that the originals remain unchanged.

Decode one JPEG or PNG directly:

```powershell
 dotnet run --project src/PhotoIdentity.Cli -- `
  decode `
  --input "C:\PrivateVerification\pixel-rotated.jpg" `
  --output ".artifacts\local-verification\pixel-normalised.png"
```

Run the complete single-image inspection path:

```powershell
 dotnet run --project src/PhotoIdentity.Cli -- `
  inspect "C:\PrivateVerification\family-photo.jpg" `
  --output ".artifacts\inspect\family-photo" `
  --overwrite `
  --verbose
```

The inspect output contains an embedded-image annotated SVG, padded and aligned face PNGs, one JSON embedding per face, a reproducibility manifest and stage timings. The command verifies that the original source hash remains unchanged.

Start a durable local-folder batch. The output directory must be outside the source root:

```powershell
 dotnet run --project src/PhotoIdentity.Cli -- `
  batch start `
  --database "C:\PhotoIdentity\catalogue.db" `
  --source "C:\Photos" `
  --output "C:\PhotoIdentity\outputs"
```

Resume, inspect or cancel the run using the printed run ID:

```powershell
 dotnet run --project src/PhotoIdentity.Cli -- `
  batch resume --database "C:\PhotoIdentity\catalogue.db" --run RUN_ID

 dotnet run --project src/PhotoIdentity.Cli -- `
  batch status --database "C:\PhotoIdentity\catalogue.db" --run RUN_ID

 dotnet run --project src/PhotoIdentity.Cli -- `
  batch cancel --database "C:\PhotoIdentity\catalogue.db" --run RUN_ID
```

Batch start scans JPEG and PNG files, records unsupported files separately, creates one durable job for each current revision and processes aligned crops and embeddings into SQLite. Resume reconstructs the saved source, output and model configuration.

## OneDrive sync-root policy

OneDrive integration uses the local Windows sync folder only. Online-only placeholders are reported without intentionally opening them; hydrate them through the OneDrive client before staging. Verified staging directories must be outside the source root. The adapter does not request Microsoft Graph permissions, OneDrive credentials or access tokens.

## Local review application

Publish the same-origin API and responsive Blazor client before running it against an existing catalogue:

```powershell
$publish = Join-Path $PWD ".artifacts\review-app"

dotnet publish `
  .\src\PhotoIdentity.Api\PhotoIdentity.Api.csproj `
  --configuration Release `
  --output $publish

$env:PhotoIdentity__DatabasePath = "C:\PhotoIdentity\catalogue.db"

Push-Location $publish
dotnet .\PhotoIdentity.Api.dll --urls "http://0.0.0.0:5080"
```

Open `http://localhost:5080` on Windows. A Pixel on the same trusted network can use the computer's LAN address when the Windows Firewall rule permits the port for that network profile. The client receives opaque image URLs and limited metadata; source roots and crop storage paths remain inside the API process. The current trusted-network slice does not include authentication, so do not expose the listener to an untrusted network. Stop the application before running `Pop-Location`.

Verify the device workflow without using personal photos or a real catalogue:

```powershell
./verify-review.ps1
```

The script creates synthetic review data below ignored `.artifacts/review-verification`, runs automated API/privacy checks, prints localhost and LAN URLs, and waits while the Windows and Pixel checklist is completed. It never creates firewall rules. For CI or command-line smoke verification, use:

```powershell
./verify-review.ps1 -Mode Smoke -Configuration Release
```

## Portable bundle workflow

WI-0018 defines versioned job and result ZIP archives for full-image, reduced-image and aligned face-crop processing. Each payload has a canonical archive path, role, byte count and SHA-256 digest. Extraction rejects unsafe paths, cross-platform name collisions, undeclared files and corrupted bytes.

Export one immutable revision:

```powershell
dotnet run --project src/PhotoIdentity.Cli -- `
  bundle export `
  --database "C:\PhotoIdentity\catalogue.db" `
  --revision REVISION_ID `
  --job "C:\PhotoIdentity\transfer\job.photoid-job"
```

Use `--profile reduced-image --max-width 1600 --max-height 1600` for bounded transfer. Crop-only export requires every canonical one-based face number, such as `--crop "3=C:\PhotoIdentity\crops\face-003.png"`; argument order is never treated as face identity.

Process the verified job on a machine with the pinned models installed but no catalogue access:

```powershell
dotnet run --project src/PhotoIdentity.Cli -- `
  bundle process `
  --job "C:\PhotoIdentity\transfer\job.photoid-job" `
  --result "C:\PhotoIdentity\transfer\result.photoid-result"
```

Import the exact job/result pair:

```powershell
dotnet run --project src/PhotoIdentity.Cli -- `
  bundle import `
  --database "C:\PhotoIdentity\catalogue.db" `
  --job "C:\PhotoIdentity\transfer\job.photoid-job" `
  --result "C:\PhotoIdentity\transfer\result.photoid-result" `
  --output "C:\PhotoIdentity\bundle-imports"
```

The returned result is linked to the exact original job-manifest digest and immutable revision. SQLite import writes only model-derived face data. People, human labels and review actions are outside the import path, and replaying the same result is harmless.

## Model-lab evaluation

Run the synthetic split-disciplined example:

```powershell
dotnet run --project src/PhotoIdentity.Cli -- `
  evaluate `
  --dataset tools/model-lab/example-dataset.json `
  --output .artifacts/model-lab/example-report.json `
  --archive-images 100000 `
  --hourly-cost 1.50 `
  --currency GBP
```

The manifest separates gallery, validation and held-out test data. Validation alone selects the identity threshold; the test split reports final detector recall, identification precision, unknown rejection, confusion and throughput. Fixed input produces byte-for-byte identical JSON with exact model hashes and pipeline version.

Real model-lab manifests, embeddings, identity identifiers and reports are sensitive local data. Do not commit them. The checked-in example is synthetic.

## First target demonstration

1. Run `photoid inspect family-photo.jpg`.
2. Verify detected face boxes, landmarks, padded crops and aligned model inputs.
3. Confirm 128-dimensional unit-normalised SFace embeddings.
4. Compare same-person and different-person cosine similarities and repeated CPU inference.

## Privacy

Do not commit personal photos, face crops, embeddings, model binaries, credentials, SAS tokens, generated biometric data, real evaluation manifests or model-lab reports. See [security and privacy](docs/architecture/security-and-privacy.md).
