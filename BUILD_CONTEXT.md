# Build context

This file is intentionally a short handoff for the next development or verification session. Formal work-item lifecycle status is resolved through `PhotoIdentity.Docs`.

## Current focus

**M28 Library curation and metadata editing is verifying WI-0146: manual slideshow curation and launch UI.**

WI-0140 through WI-0145 and WI-0157 are completed. WI-0145's explicit photo-list collection backend merged in PR #407 with successful CI run #2095: separate named ordered revision lists, PostgreSQL persistence, lifecycle API and playback-compatible snapshots are now part of `main`.

WI-0146 merged in PR #410 and adds Photo Details create/add/remove controls, a lightweight review/reorder page, slideshow-library entries and normal manual snapshot playback. Its implementation/integration/package jobs passed; the merged PR's documentation validation failure was caused by stale M28 lifecycle output and is corrected by the WI-0145 completion follow-up. WI-0146 remains open only for maintainer desktop/phone verification.

M26 is ready with WI-0158 as the next named-Creative-Collection product item; WI-0125 remains deferred. M29 is ready. M30 video support remains intentionally blocked until explicit maintainer reactivation.

## Next concrete step

Run the WI-0146 maintainer desktop/phone review covering create/add/remove/reorder, slideshow-library discovery, launch/return routing and playback. If accepted, complete WI-0146 and M28.

## Relevant files

- docs/delivery/milestones/M28-library-curation-metadata-editing.md
- docs/delivery/work-items/WI-0145-manual-slideshow-collection-model.md
- docs/delivery/status/work-items/archive/WI-0145.yaml
- docs/delivery/work-items/WI-0146-manual-slideshow-curation-ui.md
- docs/delivery/status/work-items/active/WI-0146.yaml

## Repository validation

    ./build.ps1
    ./test.ps1
    dotnet run --project tools/PhotoIdentity.Docs -- validate
    dotnet run --project tools/PhotoIdentity.Docs -- generate --check
    ./verify-postgres.ps1
