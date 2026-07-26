---
id: WI-0015
title: Build minimal review application
milestone: M04
status_source: ../status/work-items.yaml
depends_on: [WI-0011, WI-0013]
affected_modules: [PhotoIdentity.Api, PhotoIdentity.Web, PhotoIdentity.Persistence.Sqlite, PhotoIdentity.Integration.Tests]
---

# WI-0015: Build minimal review application

## Objective

Create a local ASP.NET Core API and responsive Blazor PWA for face galleries, person creation, manual labels, rejections, undo and photo details.

## Acceptance criteria

- [ ] The UI works on Windows and a Pixel on a trusted network.
- [x] Labels persist after restart.
- [x] Review actions are auditable and reversible.
- [x] Sensitive source paths are not unnecessarily returned to the browser.

The remaining criterion requires human interaction and layout verification on the target Windows computer and Pixel device. Automated coverage validates the persistence, API and privacy boundaries but cannot prove touch comfort or trusted-network setup.

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

Image files are resolved only inside the API process. Missing crop files return not found without exposing their server path. Every `/api/review` response sends `Cache-Control: no-store`, including JSON and face images, so ordinary browser HTTP caching is explicitly disabled.

## Responsive web application

The hosted Blazor WebAssembly client includes:

- a responsive face gallery with touch-sized actions;
- review-state filters and pagination;
- person creation and assignment;
- rejection and undo;
- a details page with privacy-limited photo metadata and the complete review timeline;
- an installable web manifest.

The service worker excludes every `/api/` request, including face images and review JSON, so the application does not deliberately retain biometric API responses in its cache. Together with the API's `no-store` headers, this prevents both application-managed and ordinary HTTP caching of review responses. The responsive UI can be tested over trusted-network HTTP. PWA installation on a phone generally requires a secure context and is not treated as proof of the core review workflow.

## Operator command

```powershell
$env:PhotoIdentity__DatabasePath = "C:\PhotoIdentity\catalogue.db"
dotnet run --project src/PhotoIdentity.Api --urls "http://0.0.0.0:5080"
```

Open `http://localhost:5080` on Windows. For Pixel verification, use the computer's LAN address on a trusted network and permit the selected port through Windows Firewall only for the intended network profile.

## Validation

```powershell
dotnet test tests/PhotoIdentity.Integration.Tests/PhotoIdentity.Integration.Tests.csproj
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
```

Draft pull request [#27](https://github.com/erikwasa/Photo-Identity-Indexer/pull/27) adds the schema migration, review repository, same-origin API, responsive client and integration coverage.

## Deliberate limitations

- Authentication and per-user identities are not included; this slice is limited to a trusted local network and the client currently reports a local actor string.
- Person rename, merge and bulk review are outside WI-0015.
- Historical `person_labels` rows are retained; current review state must be read through the review-action projection.
- The final Windows and Pixel verification remains a human acceptance step.
