---
id: WI-0073
title: Polish cards, menus and archive navigation
milestone: M20
status_source: ../status/work-items.yaml
depends_on: [WI-0061, WI-0066, WI-0068]
related_adrs: []
affected_modules: [PhotoIdentity.Web, PhotoIdentity.Api]
---

# WI-0073: Polish cards, menus and archive navigation

## Objective

Resolve the concrete UI/navigation defects recorded during the 2026-08-19 maintainer pass without changing the underlying M19 identity, Smart Collection or archive semantics.

## Contract

### Smart Collection card containment

- Featured/preview images remain visually contained by their cards at normal and narrow widths.
- Affected card/container content uses safe shrinking/overflow rules; images use explicit block/object-fit/max-width behavior.
- Verify both Smart Collection result thumbnails and portrait-led people cards.

### Maintain People layout and hidden state

- Person-card text and buttons never spill outside the card.
- Hidden state is visually obvious; compact badges use `Hidden`, while visible/available people need no redundant availability badge.
- Visible people sort before hidden people; existing favorites-first/name ordering remains deterministic inside each group.
- Hidden status remains presentation/discovery metadata only and does not alter face evidence or review availability.

### Dismissible menus

Use one reusable controlled dismissible menu/popover behavior for the primary Advanced menu and the Library/Collections people menu:

- close after a menu action/selection;
- close on outside pointer/click;
- close on Escape;
- close on route/navigation change;
- preserve keyboard focus and correct expanded-state accessibility.

### Favorite-person native select type-ahead

- Favorite people remain promoted/sorted.
- Native `<select>` option text begins with the person's display name so browser type-ahead matches the name instead of a leading star/icon.
- Use one consistent label convention such as `Ada Lovelace — Favorite` across native person selectors.

### Archive status wording

- `Waiting for OneDrive` is shown only when a OneDrive transition is the only useful-work blocker.
- If work is still progressing while downloads/releases exist, show a running message with useful transition counts.
- Include enough progress information to distinguish an active pipeline from a stalled transition, preferably a last-progress timestamp or completed-work delta.

### Archive return context

- Archive filters and paging are represented in the `/archive` URL.
- `View` links carry an exact `returnUrl` for the current archive state.
- Photo Details labels the context-aware action `Back to archive` when applicable.
- Browser/mouse Back and the explicit Back action restore the same filters/page.

## Maintainer review — 2026-08-21

The first implementation was reviewed on the real application. Favorite-person type-ahead, dismissible menus and archive return navigation passed. The following corrective scope remains before WI-0073 is accepted:

### Face Review

- Suggested-person results must be collapsed on initial load and behave as a compact searchable picker rather than an always-expanded list.
- Hidden-person compact badges must say `Hidden` and must not force long names/badges/actions into overflowing multi-row layouts.
- Reduce Queue controls vertical density. Processing run and suggestion model may use narrower columns; retain the existing filter semantics.

### Smart Collection people picker

- Center a slightly smaller representative portrait inside its row so the circular image has balanced top/bottom spacing.
- Reserve a stable Add/Remove action column. The action must remain visible when selected/available lists become scrollable or contain more than 5–6 people.
- Only the middle name/status area should shrink/ellipsis; scrollbar width must not consume the action area.

### Maintain People

- Remove the normal `Available in Smart Collections` badge; only hidden people need a compact `Hidden` badge.
- Change buttons to `Hide` / `Show` while retaining longer accessibility/context wording where useful.
- Remove visible `Featured photo`/`Automatic photo` and `Selected explicitly`/`Selected automatically` copy from the overview cards. The dedicated featured-photo controls retain this distinction.
- Replace the technical label count with a real **distinct photo count**. Do not simply rename `person_labels` rows. Count revisions where the person appears through confirmed face evidence and/or manual photo-level presence without double-counting one revision.

### Archive advancement classification

Observed: the page reported `Waiting for OneDrive to finish a managed download or release` while a newly added archive folder was actively synchronized/verified to completion.

Correct the underlying state classification, not just the message:

- track runnable/useful archive work separately from managed OneDrive transitions;
- report Running while useful work can progress, even if downloads/releases are also present;
- report Waiting for OneDrive only when the transition is the sole remaining blocker;
- add regression coverage for an existing managed transition plus newly added coverage where synchronization/verification still progresses.

Full review notes and cross-item decisions are recorded in `../milestones/M20-maintainer-review-2026-08-21.md`.

## Acceptance criteria

- [ ] Smart Collection and person cards contain images/text/actions at normal and narrow widths, including long selected-person lists where Add/Remove remains visible.
- [ ] Hidden people use compact high-contrast `Hidden` presentation, visible people have no redundant availability badge, and visible people remain ordered before hidden people.
- [x] Advanced and Collections people menus light-dismiss on selection, outside click, Escape and navigation.
- [x] Dismissible menus remain keyboard/focus accessible.
- [x] Favorite native person selects retain favorites-first ordering while type-ahead starts from display names.
- [ ] Face Review suggested-person results are collapsed by default and Queue controls use a compact contained layout.
- [ ] Maintain People overview removes representative-selection status copy, uses `Hide`/`Show`, and reports distinct photos rather than internal label rows.
- [ ] Archive status differentiates processing-with-OneDrive-transitions from truly waiting on OneDrive at the state-model level.
- [x] Archive View → Photo Details → Back restores filters and paging through URL-backed context.
- [ ] Previously verified M19 Smart Collection/person/navigation semantics remain unchanged after the corrective slice.
- [ ] Focused component/integration tests and final browser verification pass.

## Source finding

This item consolidates the non-semantic UI/navigation findings from `M19-maintainer-review-2026-08-19.md`. The 2026-08-21 review adds the corrective scope above. New Photo Viewer metadata/location simplification belongs to WI-0077 and Face Review filtering semantics belong to WI-0074.
