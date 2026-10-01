---
id: WI-0176
title: Keep full-app navigation within landscape viewports
milestone: M32
status_source: PhotoIdentity.Docs
depends_on: []
related_adrs: []
affected_modules: [PhotoIdentity.Web, docs]
---

# WI-0176: Keep full-app navigation within landscape viewports

## Report and scope

Issue #461: the maintainer can pan slightly right in the full app in phone landscape mode (2026-10-02, Europe/Stockholm). Earlier navigation/settings work is retained; this follow-up addresses the remaining shell overflow. Issue #461 stays open until maintained browser/PWA acceptance.

## Implementation

Previously the primary navigation only wrapped at 860px and below. At wider phone landscape and intermediate desktop widths, its combined intrinsic width plus the brand exceeded the header. Make wrapping unconditional, give the navigation the available flex space with a zero minimum width, and preserve the brand width. All primary destinations and the Advanced menu remain accessible. No global overflow clipping is introduced.

## Verification

Use a Chromium layout fixture with the actual full-app Razor shell and styles at widths 360, 412, 680, 740, 844, 900, 960, 1024 and 1280, with Advanced closed and open. Assert document width equals viewport width and every primary destination/menu link stays inside the viewport. Repeat after rotating (changing viewport width) without reloading. This shell-focused check does not claim coverage of every page's content.

Maintainer retest after merging and rebuilding:

1. Open Full app in a phone browser and installed PWA.
2. Rotate to landscape and try panning right on Review and Settings. There should be no empty horizontal strip.
3. Confirm every primary destination remains reachable; open Advanced and use a link.
4. Rotate back to portrait without reloading and repeat. Check a desktop window around 900–1280px wide as well.

Remain in review until this maintained phone/PWA verification passes.
