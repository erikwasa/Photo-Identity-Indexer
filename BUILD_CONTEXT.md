# Build context

This file is intentionally a short handoff for the next development or verification session. Formal work-item lifecycle status is resolved through `PhotoIdentity.Docs`.

## Current focus

**M28 Library curation and metadata editing is implementing WI-0146: manual slideshow curation and launch UI.**

WI-0140 through WI-0144 are now maintainer-accepted and completed. The accepted slice covers the searchable shared Place picker, manual capture-date provenance/precision and Photo Details editing, structured Smart Collection date controls, and multiple named Places with ANY semantics. Their implementation PRs (#391, #392, #394, #396 and #398) merged with successful CI, and the combined M28 manual verification was accepted on 2026-09-22.

WI-0157 reduced-precision GPS place fallback remains in progress and is being verified in a separate maintainer thread. Do not fold its acceptance or completion into the WI-0140–WI-0144 closure.

WI-0145 is merged but maintainer verification is intentionally deferred until WI-0146 is available, so both can be reviewed end-to-end together. WI-0146 adds Photo Details create/add/remove controls, a lightweight review/reorder page, slideshow-library entries and normal manual snapshot playback. M26 remains active separately. M29 is ready. M30 video support remains intentionally blocked until explicit maintainer reactivation.

## Next concrete step

Validate and merge WI-0146, then run one combined maintainer desktop/phone review covering WI-0145 persistence plus WI-0146 create/add/remove/reorder/library/launch/playback behavior.

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
