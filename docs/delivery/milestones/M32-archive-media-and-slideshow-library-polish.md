---
id: M32
title: Archive media compatibility and slideshow library polish
status_source: ../status/milestones.yaml
depends_on: [M12, M26, M27, M28]
---

# M32: Archive media compatibility and slideshow library polish

## Outcome

Photo Identity handles the real DNG files now present in the maintained archive and makes the slideshow gallery easier to understand at a glance without regressing established slideshow performance.

This milestone is intentionally narrow follow-up work. It does not reopen generic RAW support for camera formats that are not present, and it does not redesign slideshow playback.

## Work items

- [WI-0164](../work-items/WI-0164-dng-archive-support.md) - add verified end-to-end DNG support using representative private archive samples.
- [WI-0165](../work-items/WI-0165-slideshow-library-status-counts.md) - show compact preparation state and truthful photo-count information in the slideshow library.

## Delivery principles

- Implement RAW support from real archive evidence rather than guessing generic camera-format behavior.
- Keep originals immutable; browser viewing uses application-owned rendered derivatives where needed.
- Preserve the cheap initial slideshow-library path established by WI-0108.
- Do not describe an unprepared slideshow as unplayable.
- Present exact counts only when the underlying collection semantics make them exact.
- Keep gallery additions visually small and usable on desktop and phone.

## Exit criteria

- [ ] Existing and newly discovered DNG files can enter the normal archive pipeline and be viewed/processed through supported derivatives.
- [ ] Representative private DNG samples have accepted orientation, colour, metadata, runtime and memory evidence.
- [ ] Slideshow cards show preparation state only when it is valid for the current revision set.
- [ ] Manual and Smart slideshow counts are truthful, and Creative Collection quantity wording distinguishes target/maximum from exact membership when necessary.
- [ ] Slideshow-library card rendering remains responsive and does not require full snapshots solely for decorative status/count information.
- [ ] Maintainer verifies the DNG path with private archive files and the slideshow indicators on desktop and phone.
