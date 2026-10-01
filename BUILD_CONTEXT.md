# Build context

Formal lifecycle status is resolved through `PhotoIdentity.Docs`; read the target shard and linked document before continuing.

## Current focus

WI-0175 / issue #476 repairs the 2026-10-01 failed slideshow acceptance: saved Creative catalogue-query timeouts, unpooled PostgreSQL socket 10048 failures in manual listing/revalidation, and receipt loss when collection loading fails. Branch: `agent/wi-0175-slideshow-reliability`.

WI-0165 and WI-0169 acceptance failed; WI-0173 navigation caching is partially verified. Keep those items in review until maintained Windows/phone retest. The generic frontend error has no captured browser exception; do not claim its exact cause from server logs.

## Next concrete step

Windows preparation CI #36928766356 passed 23 focused tests and documentation lifecycle/generation checks. WI-0175 is in_review; the temporary workflow was removed. Check standard PR CI, then retest saved Creative targets 30/50 and Broad/150, manual refresh, Prepared after F5, and retry after temporary verification failure. Capture browser console details if the generic error recurs.

WI-0167 remains untested. WI-0170 needs maintained bulk-review acceptance. WI-0163 remains independently in progress; do not change its lifecycle. WI-0171 remains ready for phone rendering reproduction. M30 video support is intentionally deferred.

## Relevant pointers

- docs/delivery/work-items/WI-0175-slideshow-catalogue-recovery.md
- docs/delivery/work-items/WI-0165-slideshow-library-status-counts.md
- docs/delivery/work-items/WI-0169-creative-collection-scale.md
- docs/delivery/work-items/WI-0173-slideshow-library-navigation-cache.md
