# Build context

This file is intentionally a short handoff for the next development or verification session. Formal work-item lifecycle status is resolved through `PhotoIdentity.Docs`.

## Current focus

**M28 Library curation and metadata editing is complete.**

WI-0140 through WI-0146 and WI-0157 are completed. WI-0145's explicit photo-list collection backend merged in PR #407 with successful CI run #2095. WI-0146 merged in PR #410 and added Photo Details create/add/remove controls, lightweight review/reorder, slideshow-library discovery and normal manual snapshot playback.

On 2026-09-23 the maintainer verified the combined WI-0145/WI-0146 flow on desktop and phone. Explicit manual slideshow membership and ordering persisted as expected, create/add/remove/reorder worked, the collection appeared in the slideshow library, and launch/return routing plus normal playback worked as expected. This closes the final M28 exit criterion.

M26 is ready with WI-0158 as the next named-Creative-Collection product item; WI-0125 remains deferred. M29 is ready with WI-0147 as its first PostgreSQL-only cleanup item. M30 video support remains intentionally blocked until explicit maintainer reactivation.

## Next concrete step

Proceed with WI-0158 if continuing product-facing Creative Collection work, or WI-0147 if prioritising PostgreSQL-only catalogue cleanup.

## Relevant files

- docs/delivery/milestones/M28-library-curation-metadata-editing.md
- docs/delivery/work-items/WI-0145-manual-slideshow-collection-model.md
- docs/delivery/status/work-items/archive/WI-0145.yaml
- docs/delivery/work-items/WI-0146-manual-slideshow-curation-ui.md
- docs/delivery/status/work-items/archive/WI-0146.yaml
- docs/delivery/work-items/WI-0158-named-creative-collections-slideshow-library.md
- docs/delivery/work-items/WI-0147-postgres-only-runtime-composition.md

## Repository validation

    ./build.ps1
    ./test.ps1
    dotnet run --project tools/PhotoIdentity.Docs -- validate
    dotnet run --project tools/PhotoIdentity.Docs -- generate --check
    ./verify-postgres.ps1
