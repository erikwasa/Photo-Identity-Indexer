# Build context

Formal lifecycle status is resolved through `PhotoIdentity.Docs`; read the target shard and linked document before continuing.

## Current focus

WI-0171 / PR #478 addresses the reproduced phone slideshow grid artifact. On a Google Pixel 9 Pro XL installed PWA, some prepared originals showed fixed-position thin raster seams that flickered while the subtle scale motion ran; the maintainer did not see the lines on PC. The targeted fallback keeps prepared originals and cross-fades but suppresses the foreground scale transform for prepared-original playback on coarse-pointer/no-hover devices.

WI-0175 / PR #477 is merged but remains in review pending maintained slideshow reliability acceptance. WI-0165 and WI-0169 acceptance previously failed; WI-0173 navigation caching is partially verified. Keep those items in review until maintained Windows/phone retest.

## Next concrete step

Check standard PR #478 CI, then retest an affected photo on the Pixel 9 Pro XL with Prepare originals enabled while paused, playing and during a cross-fade. Compare the same photo with Prepare originals disabled, and confirm desktop playback still retains the normal subtle scale motion. Complete WI-0171 only after the maintained phone/desktop verification passes.

WI-0167 remains untested. WI-0170 needs maintained bulk-review acceptance. WI-0163 remains independently in progress; do not change its lifecycle. M30 video support is intentionally deferred.

## Relevant pointers

- docs/delivery/work-items/WI-0171-phone-slideshow-grid-artifacts.md
- docs/delivery/work-items/WI-0175-slideshow-catalogue-recovery.md
- docs/delivery/work-items/WI-0165-slideshow-library-status-counts.md
- docs/delivery/work-items/WI-0169-creative-collection-scale.md
- docs/delivery/work-items/WI-0173-slideshow-library-navigation-cache.md
