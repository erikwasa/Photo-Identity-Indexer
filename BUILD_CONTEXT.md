# Build context

Formal lifecycle status is resolved through `PhotoIdentity.Docs`; read only the target shard and linked work-item document before continuing.

## Current focus

Maintainer M32 acceptance (2026-09-30 Europe/Stockholm) accepted WI-0168 navigation, left WI-0167 untested, and failed WI-0165 player preparation continuity (53 photos, 3m20s) and WI-0169 Broad/150 preview (~60s HTTP 503). The private stdout log has no completed Creative phase summary; a particular bottleneck is not established.

`agent/slideshow-acceptance-followups` implements the WI-0165 receipt write and bounded local verification; WI-0169 worker dispatch, shared hash gate and cancellation-surviving phase diagnostics; WI-0170 browser-session card/cover reuse; and WI-0172 separate full-app collection management. These items remain in review pending CI and maintained-machine acceptance. WI-0171 tracks the unconfirmed phone grid-like rendering artifact and remains ready for reproduction. WI-0168 is completed from the maintainer's explicit acceptance.

WI-0163 bulk enrichment remains independently in progress on `agent/wi-0163-bulk-metadata-enrichment`. Do not change its state as part of slideshow work. M30 video support remains blocked.

## Next concrete step

Check the follow-up PR's CI, then retest the 53-photo manual preparation twice (cloud-only vs already local), Prepared after exit/reload/invalidation, and the ~1,000-anchor Broad/150 Creative preview twice in the same process. For another timeout capture the new phase/final diagnostics. Test cached library returns and the management hub on desktop/phone. Reproduce WI-0171 with phone/browser details, screenshot/video, same-photo proxy/original comparison, and paused/moving/fading state. Continue WI-0167 OneDrive-unavailable acceptance independently.

## Relevant pointers

- docs/delivery/work-items/WI-0165-slideshow-library-status-counts.md
- docs/delivery/work-items/WI-0169-creative-collection-scale.md
- docs/delivery/work-items/WI-0170-slideshow-library-navigation-cache.md
- docs/delivery/work-items/WI-0171-phone-slideshow-grid-artifacts.md
- docs/delivery/work-items/WI-0172-slideshow-collection-management.md

## Repository validation

    ./build.ps1
    ./test.ps1
    dotnet run --project tools/PhotoIdentity.Docs -- validate
    dotnet run --project tools/PhotoIdentity.Docs -- generate --check
    ./verify-postgres.ps1
