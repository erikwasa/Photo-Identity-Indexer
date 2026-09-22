# Build context

This file is intentionally a short handoff for the next development or verification session. Formal work-item lifecycle status is resolved through `PhotoIdentity.Docs`.

## Current focus

**M28 Library curation and metadata editing is implementing WI-0145: explicit photo-list collections for manually assembled slideshows.**

WI-0140 through WI-0144 are now maintainer-accepted and completed. The accepted slice covers the searchable shared Place picker, manual capture-date provenance/precision and Photo Details editing, structured Smart Collection date controls, and multiple named Places with ANY semantics. Their implementation PRs (#391, #392, #394, #396 and #398) merged with successful CI, and the combined M28 manual verification was accepted on 2026-09-22.

WI-0157 reduced-precision GPS place fallback remains in progress and is being verified in a separate maintainer thread. Do not fold its acceptance or completion into the WI-0140–WI-0144 closure.

WI-0145 now has a separate Core model, PostgreSQL schema/repository and API for named ordered immutable revision lists. The slideshow-snapshot endpoint returns the existing lightweight playback manifest shape, preserves manual order and omits revisions that have become unavailable. WI-0146 follows after WI-0145 passes CI and merges. M26 remains active separately. M29 is ready. M30 video support remains intentionally blocked until explicit maintainer reactivation.

## Next concrete step

Validate the WI-0145 implementation through normal CI and live PostgreSQL verification, then merge when green. After merge, proceed to WI-0146 for curation and launch UI.

## Relevant files

- docs/delivery/milestones/M28-library-curation-metadata-editing.md
- docs/delivery/work-items/WI-0145-manual-slideshow-collection-model.md
- docs/delivery/status/work-items/active/WI-0145.yaml
- docs/delivery/work-items/WI-0146-manual-slideshow-curation-ui.md
- docs/delivery/status/work-items/active/WI-0157.yaml

## Repository validation

    ./build.ps1
    ./test.ps1
    dotnet run --project tools/PhotoIdentity.Docs -- validate
    dotnet run --project tools/PhotoIdentity.Docs -- generate --check
    ./verify-postgres.ps1
