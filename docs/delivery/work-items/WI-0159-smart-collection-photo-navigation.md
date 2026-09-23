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

- [ ] Opening a photo from Smart Collection results shows previous/next controls when neighboring results exist.
- [ ] Next/previous follows the exact Smart Collection ordering and never jumps to a photo outside the active result set.
- [ ] The first photo has no previous target and the last photo has no next target.
- [ ] Navigating several photos and then returning restores the Smart Collection context without requiring filter re-entry.
- [ ] Saved and transient Smart Collection result contexts are supported.
- [ ] Direct Photo Details navigation that did not originate in Smart Collections still works without collection navigation controls.
- [ ] Automated coverage protects ordering, boundaries, stale/deleted result handling, and return routing.

## Verification requirements

Maintainer verification on desktop and phone: open a Smart Collection with more than one result, navigate forward/backward across several photos, return to Smart Collections, and confirm the same collection/context remains active.
