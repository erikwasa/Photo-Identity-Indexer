# Build context

This file is intentionally a short handoff for the next development or verification session. Formal work-item lifecycle status is resolved through `PhotoIdentity.Docs`.

## Current focus

**M28 Library curation and metadata editing is continuing with WI-0144: multiple named locations in Smart Collections.**

WI-0140's searchable shared Place picker merged in PR #391, WI-0141's provenance/precision capture-date model merged in PR #392, WI-0142's Photo Details capture-date editor merged in PR #394, and WI-0143's structured Smart Collection date controls merged in PR #396. Their requested maintainer interaction checks remain intentionally bundled with the later M28 verification pass.

PR #398 evolves the Smart Collection named-place filter from one hierarchy to a bounded list of up to 16 normalized hierarchies. Selected places use ANY semantics and continue to include descendants; duplicate paths are removed and descendants collapse beneath selected ancestors. The named-place group remains ANDed with the optional GPS rectangle.

New and updated saved definitions use filter schema v3. PostgreSQL catalogue schema 29 and the temporary SQLite compatibility schema 21 allow that version, while v1/v2 definitions remain readable. A direct PostgreSQL v2 compatibility test verifies that an existing single-place definition reopens as a one-element location list without changing results.

The Smart Collection UI reuses the WI-0140 searchable Place picker as an add control and renders selected places as removable chips under an explicit Match any selected place heading. Transient browser state preserves the list while retaining legacy single-place compatibility.

WI-0157 reduced-precision GPS place fallback merged in PR #399 and remains in progress pending maintainer verification against the recorded GPS/no-Place baseline. M26 remains active separately. M29 is ready. M30 video support remains intentionally blocked until explicit maintainer reactivation.

## Next concrete step

Validate PR #398 through the normal CI gates after reconciling current main, then merge when green. After merge, WI-0145 is the next planned M28 dependency: explicit photo-list collections for manually assembled slideshows. Desktop/phone verification remains deferred until the requested M28 batch review.

## Relevant files

- docs/delivery/work-items/WI-0144-smart-collection-multiple-locations.md
- docs/delivery/status/work-items/active/WI-0144.yaml
- src/PhotoIdentity.Core/Collections/SmartCollectionFilter.cs
- src/PhotoIdentity.Api/SmartCollectionEndpoints.cs
- src/PhotoIdentity.Persistence.Postgres/PostgresCatalogueDatabase.cs
- src/PhotoIdentity.Persistence.Postgres/PostgresSmartCollectionRepository.cs
- src/PhotoIdentity.Persistence.Postgres/PostgresSmartCollectionQueryRepository.cs
- src/PhotoIdentity.Web/Components/SmartCollectionsWorkspace.razor
- src/PhotoIdentity.Web/Components/SmartCollectionsWorkspace.razor.cs
- tests/PhotoIdentity.Persistence.Tests/PostgresSmartCollectionRepositoryTests.cs
- tests/PhotoIdentity.Integration.Tests/SmartCollectionFilterTests.cs
- tests/PhotoIdentity.Integration.Tests/SmartCollectionPlaceLocationTests.cs

## Repository validation

    ./build.ps1
    ./test.ps1
    dotnet run --project tools/PhotoIdentity.Docs -- validate
    dotnet run --project tools/PhotoIdentity.Docs -- generate --check
    ./verify-postgres.ps1
