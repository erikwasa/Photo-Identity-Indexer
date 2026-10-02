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

Previously the primary navigation only wrapped at 860px and below. At wider phone landscape and intermediate desktop widths, its combined intrinsic width plus the brand exceeded the header. Make wrapping unconditional, give the navigation the available flex space with a zero minimum width, and preserve the brand width. On narrow screens, anchor the Advanced panel to the navigation area instead of its wrapped summary so the menu also stays within the viewport. All primary destinations and the Advanced menu remain accessible. No global overflow clipping is introduced.

## Verification

Use a Chromium layout fixture with the actual full-app Razor shell and styles at widths 360, 412, 680, 740, 844, 900, 960, 1024 and 1280, with Advanced closed and open. Assert document width equals viewport width and every primary destination/menu link stays inside the viewport. Repeat after rotating (changing viewport width) without reloading. This shell-focused check does not claim coverage of every page's content.

Maintainer retest after merging and rebuilding:

1. Open Full app in a phone browser and installed PWA.
2. Rotate to landscape and try panning right on Review and Settings. There should be no empty horizontal strip.
3. Confirm every primary destination remains reachable; open Advanced and use a link.
4. Rotate back to portrait without reloading and repeat. Check a desktop window around 900–1280px wide as well.

Remain in review until this maintained phone/PWA verification passes.

## Automated evidence

[Validation run 36938800682](https://github.com/erikwasa/Photo-Identity-Indexer/actions/runs/36938800682) reproduced a document width of 1133px at a 900px viewport before the fix. After the fix, all 24 closed/open menu checks across nine distinct widths and repeated rotation widths passed. Lifecycle completion/review commands, documentation validation and generated-view freshness passed. The temporary workflow was removed after recording the generated lifecycle changes; the permanent CI gate is unchanged.

[Windows documentation run 36939419884](https://github.com/erikwasa/Photo-Identity-Indexer/actions/runs/36939419884) regenerated and validated all delivery views, including the roadmap. Implementation and the six accepted lifecycle updates are submitted in [PR #479](https://github.com/erikwasa/Photo-Identity-Indexer/pull/479).

## Maintainer landscape retest — 2026-10-02

PR #479 is merged. The maintainer confirms phone landscape no longer pans, but the supplied Archive screenshot shows the Local catalogue badge on a pale right-hand strip outside the painted header/workflow area. This is partial acceptance only; issue #461 remains open. The screenshot is not committed.

The earlier fixture resized a desktop Chromium page; it did not emulate a mobile viewport, touch capabilities or device scale. Width/link bounds alone did not establish correct phone rendering.

The follow-up applies the existing mobile header layout whenever the primary input is coarse with no hover, as well as below 680px. Phone landscape therefore keeps the same static header, wrapped navigation, hidden redundant catalogue badge and navigation-anchored Advanced menu as portrait. Desktop pointer layouts retain their current behavior. No global overflow clipping is added. The screenshot establishes the visible defect; it does not establish a browser compositor root cause.

Retest Archive in phone landscape and portrait, including rotation without reload: the dark header should span the page, there should be no pale badge strip, and all primary/Advanced destinations must remain reachable. Also confirm desktop navigation at intermediate widths. WI-0176 remains in review after implementation until maintained phone acceptance.
