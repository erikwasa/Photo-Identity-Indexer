---
id: WI-0159
title: Keep Smart Collection context while navigating photos
milestone: M28
status_source: ../status/work-items.yaml
depends_on: [WI-0146]
related_adrs: []
affected_modules: [PhotoIdentity.Web, PhotoIdentity.Api, PhotoIdentity.Integration.Tests, docs]
---

# WI-0159: Keep Smart Collection context while navigating photos

## Objective

When a photo is opened from Smart Collection results, let the operator move directly to the previous or next photo in that result sequence without returning to the Smart Collections page between photos.

## Why

The current details workflow makes inspection unnecessarily repetitive: open one result, return to the Smart Collection, then open the next. Smart Collections are frequently used as a review surface, so the photo viewer should retain enough collection/query context to traverse the current result sequence naturally.

## In scope

- Preserve the saved or transient Smart Collection context when opening Photo Details from results.
- Add previous/next navigation in Photo Details when that context is present.
- Use the same deterministic ordering as the Smart Collection result set.
- Keep the current photo addressable/deep-linkable by revision ID.
- Preserve a return route back to the Smart Collection and its current browsing position.
- Handle first/last item boundaries cleanly.
- Support both saved Smart Collections and transient previews without turning the photo page into a new collection query editor.
- Keep ordinary Photo Details links outside Smart Collections unchanged.

## Out of scope

- Slideshow playback controls.
- Reordering Smart Collection results.
- Persisting a new collection solely for navigation.
- Changing Smart Collection filter semantics.

## Acceptance criteria

- [x] Opening a photo from Smart Collection results shows previous/next controls when neighboring results exist.
- [x] Next/previous follows the exact Smart Collection ordering and never jumps to a photo outside the active result set.
- [x] The first photo has no previous target and the last photo has no next target.
- [x] Navigating several photos and then returning restores the Smart Collection context without requiring filter re-entry.
- [x] Saved and transient Smart Collection result contexts are supported.
- [x] Direct Photo Details navigation that did not originate in Smart Collections still works without collection navigation controls.
- [ ] Automated coverage protects ordering, boundaries, stale/deleted result handling, and return routing.

## Verification requirements

Maintainer verification on desktop and phone: open a Smart Collection with more than one result, navigate forward/backward across several photos, return to Smart Collections, and confirm the same collection/context remains active.

## Completion notes

- Implementation is in PR #417 and remains `in_progress` until CI and the deferred maintainer desktop/phone verification are complete.
- Photo Details reuses the existing Smart Collection `returnUrl` as the navigation context. Saved collections retain the collection ID; transient previews retain their per-tab preview key and reconstruct the exact stored filter state from `sessionStorage`.
- Navigation reuses the existing bounded saved/transient Smart Collection query endpoints, so the canonical query ordering remains authoritative and the browser never requests an unbounded result set.
- The navigator normally evaluates the current 40-photo result page. When the current photo is at a page edge it performs one additional three-row query around that boundary to resolve the adjacent photo without changing collection semantics.
- Previous/next links update the nested Smart Collection return offset to the 40-photo page containing the destination, so returning after traversing several photos restores the useful browsing position.
- If the collection changes enough that the current revision cannot be reconciled with the expected result page, navigation is disabled with a stale-context notice rather than jumping to a different photo.
- Direct Photo Details and Archive-originated Photo Details remain unchanged because the navigator activates only for a validated saved/transient Smart Collection return context.
