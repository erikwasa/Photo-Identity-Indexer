---
id: WI-0060
title: Streamline bulk face review
milestone: M18
status_source: ../status/work-items.yaml
depends_on: [WI-0033]
related_adrs: []
affected_modules: [PhotoIdentity.Api, PhotoIdentity.Web, PhotoIdentity.Persistence.Sqlite, PhotoIdentity.Integration.Tests, PhotoIdentity.ReviewVerification]
---

# WI-0060: Streamline bulk face review

## Objective

Reduce the interaction and scrolling cost of assigning, marking Unknown and rejecting groups of faces while retaining explicit human intent, append-only audit semantics and protection against stale bulk mutations.

## Why

The current continuous review gallery supports individual checkboxes plus `Select loaded`, but it has no range-selection model. Selecting most of the first several dozen faces therefore requires many individual clicks. Bulk controls live above the infinite-scroll gallery, so an operator who selects while scrolling must return to the top before acting. The visible preview-then-commit sequence also adds a second user confirmation even though selection plus the chosen bulk action already express explicit intent.

A common workflow is to filter/order the queue, recognize that most of a contiguous group belongs to one person, select that range, remove a few exceptions, and commit the decision from the current scroll position.

## In scope

- Add desktop Shift-click range selection using the current loaded face order after filtering and sorting.
- Keep individual checkbox toggling so exceptions can be removed from a selected range.
- Retain `Select loaded` and `Clear` controls.
- Show a persistent sticky/fixed bulk-action bar whenever one or more eligible faces are selected.
- Keep the selected count and bulk actions accessible from the operator's current scroll position.
- Support bulk assignment to one named person, bulk Unknown and bulk false-detection rejection from the persistent action bar; preserve grouped top-suggestion acceptance where applicable.
- Remove the user-visible preview/confirmation step. One explicit action from the selected state should perform the mutation.
- Preserve stale-data protection internally. The client may perform preview/revalidation immediately before commit, or the API may offer an equivalent atomic validated commit, but a changed eligible set must not silently mutate a different set than the user selected.
- Preserve filters, ordering and practical scroll position after successful bulk actions.
- Prefer updating/removing affected visible cards in place and refilling the continuous queue rather than resetting the entire workspace to the top.
- Maintain keyboard accessibility and existing touch behavior; Shift range selection is an enhancement for pointer/keyboard desktop use, not a replacement for normal checkbox selection.

## Out of scope

- Automatic assignment based only on model score or confidence.
- Lasso/drag selection or arbitrary spatial selection.
- Selecting unloaded faces that are not represented in the current client queue.
- Removing audit records, undo semantics or server-side eligibility validation.

## Acceptance criteria

- [ ] Selecting one eligible face and Shift-clicking a later eligible face selects the contiguous eligible range between them in the current filtered/sorted loaded order.
- [ ] Individual selected faces can be toggled off as exceptions without clearing the rest of the range.
- [ ] `Select loaded` and `Clear` remain available and behave consistently with range selection.
- [ ] While at least one face is selected, a persistent action surface shows the selected count and remains usable while the operator scrolls down the gallery.
- [ ] The persistent action surface supports assignment to one person, Unknown and false-detection rejection; suggestion acceptance remains available when the selected set satisfies its existing eligibility rules.
- [ ] The operator does not have to complete a separate preview/confirm UI step before a selected bulk action is committed.
- [ ] The server/client still detects stale or changed eligibility before mutation and does not silently commit a materially different set.
- [ ] A successful bulk action does not jump the review workspace back to the top; current filters and ordering remain active.
- [ ] Processed cards are updated or removed consistently with the current state filter, and continuous loading can refill the visible queue as needed.
- [ ] Keyboard focus and checkbox semantics remain accessible, and touch users can continue selecting individual faces without Shift.
- [ ] Automated coverage protects range-selection state, action eligibility, internal revalidation/commit behavior and post-commit queue refresh.
- [ ] Human Windows verification demonstrates the representative workflow: select a contiguous block of roughly 30 faces, deselect several exceptions, assign the remaining selection from the scrolled position, then repeat with Unknown and false-detection actions.

## Verification requirements

Automated Web/API/integration coverage plus human Windows verification of range selection, sticky actions and scroll preservation are required.

## Completion notes

- Files changed:
- Trade-offs:
- Deferred work:
- Commands run:
