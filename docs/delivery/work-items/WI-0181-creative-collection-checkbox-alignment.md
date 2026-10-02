---
id: WI-0181
title: Fix Creative Collections checkbox alignment and shared form styling
milestone: M32
status_source: PhotoIdentity.Docs
depends_on: []
related_adrs: []
affected_modules: [PhotoIdentity.Web, PhotoIdentity.Integration.Tests, docs]
---

# WI-0181: Fix Creative Collections checkbox alignment and shared form styling

## Context

Issue #489 records that the `Favor photos not shown recently` checkbox on `/creative-collections` is not vertically aligned with its text. The page currently uses collection-form class names that are styled inside `SmartCollectionsWorkspace.razor.css`; Blazor CSS isolation makes that an unsafe sharing mechanism for a separate page.

## Scope

- Correct the novelty checkbox alignment on `/creative-collections` for desktop and phone/PWA.
- Keep checkbox and text as one clear control with a useful touch target.
- Identify the collection-form rules that are genuinely shared between Smart and Creative Collection authoring surfaces and place them in an explicit shared styling/component boundary.
- Avoid depending on component-scoped CSS from `SmartCollectionsWorkspace` to style unrelated pages.
- Preserve existing Creative Collection recipe behavior and API contracts.
- Regression-check nearby labels, inputs and checkboxes at narrow widths for wrapping and horizontal overflow.

## Product constraint

Keep the fix visually quiet and consistent with the existing full-app forms. Do not redesign the Creative Collection editor as part of this item.

## Out of scope

- Changing Creative Collection recipe semantics.
- Reorganizing the entire `/creative-collections` page.
- Global restyling of unrelated application forms.

## Acceptance criteria

- `Favor photos not shown recently` is vertically aligned with its checkbox on desktop and phone/PWA.
- The checkbox and label behave as one accessible/tappable control.
- Shared collection-form styling no longer relies on CSS isolation accidentally crossing component/page boundaries.
- Existing Smart Collection form styling does not regress after extracting/reusing shared rules.
- No new horizontal overflow or awkward wrapping appears on supported phone widths.
- Focused Web/markup regression coverage plus maintainer desktop/phone verification confirm the result.
