---
id: WI-0168
title: Add bidirectional navigation between Slideshows and the full PWA
milestone: M32
status_source: ../status/work-items.yaml
depends_on: [WI-0108]
related_adrs: []
affected_modules: [PhotoIdentity.Web, PhotoIdentity.Integration.Tests, docs]
---

# WI-0168: Add bidirectional navigation between Slideshows and the full PWA

## Objective

Make the installed Photo Identity PWA navigable as one application on a phone by providing an obvious path from the normal app to Slideshows and a path from the slideshow library back to the full application.

## Maintainer evidence

When Photo Identity is installed from the browser as a standalone PWA, there is currently no application navigation path to the slideshow library. If the slideshow surface is reached directly, its `ConsumerLayout` also provides no route back to the rest of Photo Identity. In standalone display mode the browser address bar is not available as an escape hatch.

The PWA manifest already uses `/` as both `start_url` and `scope`, so this is an application-navigation gap rather than a manifest-scope limitation.

## Scope

- Add a compact **Slideshows** destination to the normal Photo Identity navigation.
- Add an unobtrusive **Full app** / **Photo Identity** navigation affordance from the slideshow consumer surface back to the normal application.
- Preserve the intentionally simple, read-only slideshow presentation surface; do not turn the consumer layout into the full operator navigation chrome.
- Ensure navigation remains usable in standalone PWA mode on the maintained phone layout and in the ordinary desktop browser.
- Preserve the existing trusted-private-network and unauthenticated access boundary; this work adds navigation only.

## Implementation progress

PR #465 implements the navigation as a narrow Web UI change. The normal `MainLayout` now includes **Slideshows** in primary navigation, while the slideshow `ConsumerLayout` includes a compact **Full app** link back to `/`. Both cross-surface targets are centralized in `AppSurfaceNavigation`, and the existing slideshow route test suite protects those route values without adding a new browser-test dependency.

The consumer header now uses a space-between layout and a 44 px return control, with a compact small-phone treatment below 420 px. This keeps the read-only slideshow surface intentionally simpler than the operator shell and is designed to avoid introducing horizontal overflow in the installed PWA. Automated build/test/docs validation and maintained phone/desktop acceptance remain pending.

## Acceptance criteria

- [ ] The normal application navigation includes a discoverable route to `/slideshows`.
- [ ] The slideshow library/consumer surface includes a discoverable route back to the normal application.
- [ ] Both directions work in an installed standalone PWA without relying on a browser address bar or browser Back history.
- [ ] The slideshow consumer layout remains visually simple and focused on read-only playback.
- [ ] The new navigation is responsive and accessible on the maintained desktop Edge and phone layouts.
- [ ] Automated route/presentation coverage protects the presence and targets of the two navigation affordances where practical.

## Verification plan

1. Install/open Photo Identity as a standalone PWA on the maintained phone.
2. From the normal app, navigate to Slideshows using only in-app controls.
3. From the slideshow library, return to the full app using only in-app controls.
4. Repeat the two directions in desktop Edge and confirm the consumer layout remains compact.
