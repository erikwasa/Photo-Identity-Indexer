# Build context

This file is intentionally a short handoff for the next development or verification session. Formal work-item lifecycle status is resolved through `PhotoIdentity.Docs`.

## Current focus

**M28 Library curation and metadata editing is continuing with WI-0143: structured Smart Collection date controls.**

WI-0140's searchable shared Place picker merged in PR #391, WI-0141's provenance/precision capture-date model merged in PR #392, and WI-0142's Photo Details capture-date editor merged in PR #394. Their requested maintainer interaction checks remain intentionally bundled with the later M28 verification pass.

PR #396 removes the Smart Collection date mini-language from the UI. Taken time now offers Any date, Year, Month, Exact date and Date range controls. New UI requests send explicit inclusive `takenRange.from`/`takenRange.to` bounds; the API retains the legacy `Taken` string only for compatibility with older callers.

Saved Smart Collections already persist explicit date bounds under versioned filter schema v2, so WI-0143 does not need a storage migration. Existing saved ranges reopen into the closest equivalent structured mode. Any date explicitly leaves undated photos eligible; populated modes require an effective capture date. WI-0141's overlap rule remains authoritative for imprecise manual dates, and live PostgreSQL coverage verifies a structured one-day request can match a manual year-only date.

M26 remains active separately. M29 is ready. M30 video support remains intentionally blocked until explicit maintainer reactivation.

## Next concrete step

Validate PR #396 through the normal CI gates and merge when green. After merge, WI-0144 can build on the merged searchable Place picker to support multiple named locations while desktop/phone verification remains deferred until the requested M28 batch review.

## Relevant files

- docs/delivery/milestones/M28-library-curation-metadata-editing.md
- docs/delivery/work-items/WI-0143-structured-smart-collection-date-controls.md
- docs/delivery/status/work-items/active/WI-0143.yaml
- src/PhotoIdentity.Api/SmartCollectionEndpoints.cs
- src/PhotoIdentity.Web/Components/SmartCollectionsWorkspace.razor
- src/PhotoIdentity.Web/Components/SmartCollectionsWorkspace.razor.cs
- src/PhotoIdentity.Web/SmartCollectionDateEditorModel.cs
- src/PhotoIdentity.Web/SmartCollectionContracts.cs
- tests/PhotoIdentity.Integration.Tests/SmartCollectionDateEditorModelTests.cs
- tests/PhotoIdentity.Integration.Tests/StructuredSmartCollectionDateApplicationTests.cs
- tests/PhotoIdentity.Integration.Tests/ManualCaptureDateApplicationTests.cs

## Repository validation

    ./build.ps1
    ./test.ps1
    dotnet run --project tools/PhotoIdentity.Docs -- validate
    dotnet run --project tools/PhotoIdentity.Docs -- generate --check
    ./verify-postgres.ps1
