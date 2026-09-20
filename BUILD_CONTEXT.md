# Build context

This file is intentionally a short handoff for the next development or verification session. Formal work-item lifecycle status is resolved through `PhotoIdentity.Docs`.

## Current focus

**M28 Library curation and metadata editing is continuing with WI-0141: manual capture-date overrides with provenance and precision.**

WI-0140's searchable shared Place picker merged in PR #391. Its requested desktop/phone interaction acceptance is intentionally deferred until the current batch of M28 work is ready for maintainer verification.

PR #392 implements the WI-0141 foundation. PostgreSQL schema v27 stores manual capture dates as append-only set/clear actions separate from extracted `photo_capture_metadata`. The model preserves exact year/year-month/full-date precision and derives an inclusive effective range instead of inventing missing day/time values. Reinspection may replace extracted metadata while the manual action remains authoritative; clearing reveals the latest extracted value again.

PostgreSQL Smart Collection queries now filter against the effective date range using overlap semantics and expose the effective range/source alongside raw extracted `TakenAtLocal`. Focused tests cover precision, metadata refresh, reversibility and imprecise-date filtering. Photo Details editing remains WI-0142 scope.

M26 remains active with WI-0128 tracked separately. M29 is ready. M30 video support remains intentionally blocked until explicit maintainer reactivation.

## Next concrete step

Validate PR #392 through the normal CI gates and merge when green. Afterward WI-0142 and WI-0143 become ready consumers of the capture-date model; the maintainer can verify WI-0140 together with the later M28 UI changes as requested.

## Relevant files

- docs/delivery/milestones/M28-library-curation-metadata-editing.md
- docs/delivery/work-items/WI-0141-manual-capture-date-model.md
- docs/delivery/status/work-items/active/WI-0141.yaml
- src/PhotoIdentity.Core/Sources/PhotoCaptureDate.cs
- src/PhotoIdentity.Core/Sources/IPhotoCaptureDateRepository.cs
- src/PhotoIdentity.Persistence.Postgres/PostgresPhotoCaptureDateRepository.cs
- src/PhotoIdentity.Persistence.Postgres/PostgresSmartCollectionQueryRepository.cs
- tests/PhotoIdentity.Core.Tests/PhotoCaptureDateValueTests.cs
- tests/PhotoIdentity.Persistence.Tests/PostgresPhotoCaptureDateRepositoryTests.cs

## Repository validation

    ./build.ps1
    ./test.ps1
    dotnet run --project tools/PhotoIdentity.Docs -- validate
    dotnet run --project tools/PhotoIdentity.Docs -- generate --check
    ./verify-postgres.ps1
