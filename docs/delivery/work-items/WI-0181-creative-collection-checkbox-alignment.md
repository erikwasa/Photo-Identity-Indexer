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

- [ ] `Favor photos not shown recently` is vertically aligned with its checkbox on desktop and phone/PWA.
- [ ] The checkbox and label behave as one accessible/tappable control.
- [x] Shared collection-form styling no longer relies on CSS isolation accidentally crossing component/page boundaries.
- [ ] Existing Smart Collection form styling does not regress after extracting/reusing shared rules.
- [ ] No new horizontal overflow or awkward wrapping appears on supported phone widths.
- [x] Focused Web/markup regression coverage is present; maintainer desktop/phone verification remains required.

## Implementation notes

PR #494 extracts the Smart/Creative authoring shell, shared fields, actions and Creative recipe styles into `wwwroot/css/collection-workspace.css`, loaded explicitly by the Web shell before component-isolated styles. The shared rules are scoped to the `smart-collections-heading` and `creative-collections-heading` workspaces, so the separate slideshow collection-management page is not restyled merely because it reuses some `smart-*` class names.

`SmartCollectionsWorkspace.razor.css` now retains only Smart Collection-specific filter/result/place rules. The Creative novelty toggle remains a semantic `<label>` containing the checkbox and text, but receives an explicit two-column alignment, a 44 px minimum touch target, a 1.25 rem checkbox and a full-width row inside Creative controls. At phone widths, Creative controls and preview summary collapse to one column to avoid cramped wrapping or horizontal overflow.

`CollectionWorkspaceStyleContractTests` verifies that the shared stylesheet is loaded before isolated component styles, that the Creative novelty markup retains the label/checkbox/text contract, that the touch/alignment rules are present, and that the shared selectors do not drift back into the Smart component-isolated stylesheet.

## Verification status

Implementation is in review in PR #494. Required CI plus maintainer visual verification on desktop and phone/PWA remain outstanding. Maintainer verification should check the novelty checkbox alignment and whole-label tap behavior, target/context field wrapping, horizontal overflow, and unchanged `/smart-collections` authoring layout.
