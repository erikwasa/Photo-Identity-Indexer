---
id: WI-0015
title: Build minimal review application
milestone: M04
status_source: ../status/work-items.yaml
depends_on: [WI-0011, WI-0013]
affected_modules: [PhotoIdentity.Api, PhotoIdentity.Web, PhotoIdentity.Persistence.Sqlite, PhotoIdentity.Integration.Tests, PhotoIdentity.ReviewVerification]
---

# WI-0015: Build minimal review application

## Objective

Create a local ASP.NET Core API and responsive Blazor PWA for face galleries, person creation, manual labels, rejections, undo and photo details.

## Acceptance criteria

- [x] The UI works on Windows and a Pixel on a trusted network.
- [x] Labels persist after restart.
- [x] Review actions are auditable and reversible.
- [x] Sensitive source paths are not unnecessarily returned to the browser.

The maintainer completed the final Windows and Pixel interaction check on 2026-07-27, including trusted-network access, touch interaction, assignment, rejection, undo, restart persistence and privacy-limited details.

## Architecture

`PhotoIdentity.Api` hosts the local same-origin API and the `PhotoIdentity.Web` Blazor WebAssembly client. SQLite remains the only durable authority. The client receives opaque identifiers, face-image URLs, photo basenames and limited review metadata; source roots and crop storage paths never enter browser DTOs.

The API initializes the configured catalogue before serving requests. The database path comes from `PhotoIdentity:DatabasePath`, with the environment-variable form `PhotoIdentity__DatabasePath`. No CORS policy is enabled because the client and API are intended to share an origin.

## Schema version 4 and audit policy

Schema version 4 adds `review_actions`, an append-only history of:

- assignment to an existing person;
- rejection of a false or unusable face;
- undo of the latest active assignment or rejection.

An undo marks the target action reversed and appends a separate undo event. Current review state is derived from the newest unreversed assignment or rejection. Human label rows remain durable evidence rather than being destructively deleted, while the review-action history defines the current UI state.

## Review API

The local API provides:

- paged face galleries filtered by unreviewed, assigned, rejected or all;
- face details and audit history;
- person listing and creation;
- assignment, rejection and one-step undo;
- face-image streaming through an opaque occurrence URL.

Image files are resolved only inside the API process. Missing crop files return not found without exposing their server path. Batch-relative crop paths are resolved against the matching processing run's persisted output root and converted to validated physical paths before streaming. Every `/api/review` response sends `Cache-Control: no-store`, including JSON and face images, so ordinary browser HTTP caching is explicitly disabled.

## Responsive web application

The hosted Blazor WebAssembly client includes:

- a responsive face gallery with touch-sized actions;
- review-state filters and pagination;
- person creation and assignment;
- rejection and undo;
- a details page with privacy-limited photo metadata and the complete review timeline;
- an installable web manifest.

The service worker excludes every `/api/` request, including face images and review JSON, so the application does not deliberately retain biometric API responses in its cache. Together with the API's `no-store` headers, this prevents both application-managed and ordinary HTTP caching of review responses. The responsive UI can be tested over trusted-network HTTP. PWA installation on a phone generally requires a secure context and is not treated as proof of the core review workflow.

## Device verification harness

`verify-review.ps1` creates an isolated catalogue below `.artifacts/review-verification` using synthetic coloured PNG crops. It never opens or changes the operator's real catalogue. The script supports:

- `Interactive`, which starts the real hosted application and waits while Windows and Pixel checks are performed;
- `Smoke`, which starts the app, validates the API and exits automatically;
- `Prepare`, which creates only the disposable catalogue and crops.

The smoke path verifies health, all three queue states, opaque image streaming, `no-store`, person creation, assignment, undo and retained audit history. GitHub Actions runs this mode through Windows PowerShell. The script detects LAN IPv4 addresses but deliberately does not create Windows Firewall rules.

Run the final acceptance path with:

```powershell
./verify-review.ps1
```

Use the printed localhost URL on Windows and a printed LAN URL on the Pixel. Keep the listener on a trusted private network because this milestone intentionally has no authentication and uses HTTP.

## Operator command for a real catalogue

Publish the hosted application so the Blazor client is present under `wwwroot`, then run the published API from its publish directory:

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

Open `http://localhost:5080` on Windows. For Pixel verification, use the computer's LAN address on a trusted network and permit the selected port through Windows Firewall only for the intended network profile. Stop the application before running `Pop-Location`.

## Validation and completion

```powershell
./verify-review.ps1 -Mode Smoke -Configuration Release
dotnet test tests/PhotoIdentity.Integration.Tests/PhotoIdentity.Integration.Tests.csproj
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
```

Pull request [#27](https://github.com/erikwasa/Photo-Identity-Indexer/pull/27) merged the schema migration, review repository, same-origin API, responsive client and integration coverage at `88f5c2c1b2dbccea9e99870405bbb9e280aa1d00`. Pull request [#28](https://github.com/erikwasa/Photo-Identity-Indexer/pull/28) merged the isolated device-verification harness at `2dbb4de34df81ebfe2b326f0bc4fb48369d46b81`.

GitHub Actions run `30191749014` passed the published hosted-client smoke path, synthetic gallery and image streaming, privacy/cache checks, assignment and undo, documentation validation and the existing Windows mixed-media verification.

During the real-catalogue acceptance run, batch-generated relative crop paths exposed a physical-versus-virtual path-resolution defect. Pull request [#31](https://github.com/erikwasa/Photo-Identity-Indexer/pull/31) resolved those paths against the processing run output root, rejected root escapes and added production-shaped integration coverage. GitHub Actions run `30221154431` passed on that fix.

The maintainer then reported successful Windows and Pixel interaction verification on 2026-07-27. WI-0015 and M04 are complete.

## Deliberate limitations

- Authentication and per-user identities are not included; this slice is limited to a trusted local network and the client currently reports a local actor string.
- Person rename, merge and bulk review are outside WI-0015.
- Historical `person_labels` rows are retained; current review state must be read through the review-action projection.
- PWA installation over trusted-network HTTP is not part of the acceptance boundary.
