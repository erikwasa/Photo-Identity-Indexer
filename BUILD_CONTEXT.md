# Build context

## Current milestone

**M04 — Minimal review application**

## Current work item

**WI-0015 — Build minimal review application**

Status: `in_progress`

## Branch and pull request

- Branch: `agent/WI-0015-review-app`
- Draft pull request: [#27 — Add minimal local face review application](https://github.com/erikwasa/Photo-Identity-Indexer/pull/27)

## Objective

Provide a local ASP.NET Core API and responsive Blazor WebAssembly review application for face galleries, person creation, assignment, rejection, undo, photo details and an auditable history without exposing sensitive filesystem paths to the browser.

## Current slice

Implement the complete minimal review workflow. SQLite schema version 4 stores reversible review actions; the API owns database and image access; the same-origin client provides desktop and phone layouts. Automated validation covers restart persistence, audit history, undo ordering and path-redacted responses. Human Windows and Pixel verification remains required before completion.

## Relevant files

- `src/PhotoIdentity.Persistence.Sqlite/SqliteCatalogueDatabase.cs`
- `src/PhotoIdentity.Persistence.Sqlite/ReviewCatalogueRecords.cs`
- `src/PhotoIdentity.Persistence.Sqlite/SqliteReviewRepository.cs`
- `src/PhotoIdentity.Api/Program.cs`
- `src/PhotoIdentity.Api/ReviewEndpoints.cs`
- `src/PhotoIdentity.Web/ReviewContracts.cs`
- `src/PhotoIdentity.Web/Pages/Home.razor`
- `src/PhotoIdentity.Web/Pages/FaceDetails.razor`
- `src/PhotoIdentity.Web/Components/FaceCard.razor`
- `src/PhotoIdentity.Web/wwwroot/css/app.css`
- `src/PhotoIdentity.Web/wwwroot/service-worker.js`
- `tests/PhotoIdentity.Integration.Tests/ReviewApplicationTests.cs`
- `docs/delivery/work-items/WI-0015-review-ui.md`
- `docs/delivery/status/work-items.yaml`

## Commands

```powershell
dotnet test tests/PhotoIdentity.Integration.Tests/PhotoIdentity.Integration.Tests.csproj
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check

$env:PhotoIdentity__DatabasePath = "C:\PhotoIdentity\catalogue.db"
dotnet run --project src/PhotoIdentity.Api --urls "http://0.0.0.0:5080"
```

## Acceptance test for this slice

- A paged face gallery loads from the existing catalogue.
- A human reviewer can create a person, assign a face, reject a face and undo the latest action.
- Assignment and rejection survive process restart.
- Every review mutation remains in the audit history and undo does not erase the target action.
- The browser receives opaque image endpoints instead of crop storage paths.
- Source roots and crop paths do not appear in gallery or details JSON.
- API and face-image requests are excluded from service-worker caching.
- Responsive controls remain usable at Pixel-class widths.
- The Windows host and Pixel trusted-network workflow are verified manually before completion.

## Verification

WI-0014 completed in pull request #26 at `b5d2a1ce24629df9fdb516eea12a69534fe257d5`. GitHub Actions run `30185278984` passed dependency audit, Release build, all tests, living-document validation, generated-document checks and Windows mixed-media verification. The human maintainer then validated the implementation against a real Personal OneDrive Files On-Demand folder.

Draft pull request #27 relies on GitHub Actions for executable validation because the agent environment does not contain the .NET SDK.

## Known issues

- The local trusted-network slice has no authentication; the review actor is currently self-reported by the client.
- Pixel PWA installation requires a secure context, although the responsive review workflow can be exercised over trusted-network HTTP.
- Person rename, merge and bulk review are outside WI-0015.
- Historical manual label rows are retained; the current assignment/rejection state is defined by the review-action projection.

## Next action

Resolve CI and review findings on pull request #27, then verify the gallery, assignment, rejection, undo and details workflow on Windows and a Pixel before marking WI-0015 and M04 completed.
