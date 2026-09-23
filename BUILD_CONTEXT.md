# Build context

This file is intentionally a short handoff for the next development or verification session. Formal work-item lifecycle status is resolved through `PhotoIdentity.Docs`.

## Current focus

**M26 Creative Collections is actively implementing WI-0125: birth dates and family relationships for age-aware family stories. M28 remains active with the new Smart Collection browsing follow-ups.**

WI-0125 was explicitly reactivated by the maintainer on 2026-09-23. The active branch adds partial birth-date precision, explicit family relationships with inverse semantics, deterministic age ranges, People maintenance UI, and age/relationship Smart Collection criteria that remain exact anchors for the existing Creative Collection path.

The same planning pass recorded four requested follow-ups: WI-0159 for previous/next navigation while viewing a Smart Collection photo, WI-0160 for bounded infinite scrolling, WI-0161 for compact effective Place labels instead of raw GPS under photo cards, and WI-0162 for scaling the successful WI-0127 semantic text-to-photo direction, combining it with WI-0128 generated-caption search, and saving result sets as explicit slideshow collections.

WI-0140 through WI-0146 and WI-0157 are completed. WI-0146 merged in PR #410 and the maintainer subsequently verified WI-0145/WI-0146 together on desktop and phone. That acceptance makes WI-0159 ready; WI-0160 follows WI-0159. WI-0161 and WI-0162 are ready independently. M29 remains ready. M30 video support remains intentionally blocked until explicit maintainer reactivation.

## Next concrete step

Complete CI/live PostgreSQL verification for WI-0125, then run maintainer verification with representative private people/photos. The next M28 browsing item, WI-0159, is now ready independently.

## Relevant files

- docs/delivery/milestones/M26-creative-collections.md
- docs/delivery/work-items/WI-0125-person-family-metadata.md
- docs/delivery/status/work-items/active/WI-0125.yaml
- src/PhotoIdentity.Core/People/PersonFamilyMetadata.cs
- src/PhotoIdentity.Persistence.Postgres/PostgresPersonFamilyMetadataRepository.cs
- src/PhotoIdentity.Web/Pages/People.razor
- docs/delivery/work-items/WI-0159-smart-collection-photo-navigation.md
- docs/delivery/work-items/WI-0160-smart-collection-infinite-scroll.md
- docs/delivery/work-items/WI-0161-smart-collection-place-labels.md
- docs/delivery/work-items/WI-0162-semantic-caption-search-slideshow-collections.md

## Repository validation

    ./build.ps1
    ./test.ps1
    dotnet run --project tools/PhotoIdentity.Docs -- validate
    dotnet run --project tools/PhotoIdentity.Docs -- generate --check
    ./verify-postgres.ps1
