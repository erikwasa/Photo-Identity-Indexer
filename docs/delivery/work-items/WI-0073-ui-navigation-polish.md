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
- Hidden state is visually obvious and says what it means, e.g. `Hidden from Smart Collections` rather than a generic muted badge.
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

## Acceptance criteria

- [ ] Smart Collection and person cards contain images/text/actions at normal and narrow widths.
- [ ] Hidden people use clear high-contrast presentation and are ordered after visible people.
- [ ] Advanced and Collections people menus light-dismiss on selection, outside click, Escape and navigation.
- [ ] Dismissible menus remain keyboard/focus accessible.
- [ ] Favorite native person selects retain favorites-first ordering while type-ahead starts from display names.
- [ ] Archive status differentiates processing-with-OneDrive-transitions from truly waiting on OneDrive.
- [ ] Archive View → Photo Details → Back restores filters and paging through URL-backed context.
- [ ] Previously verified M19 Smart Collection/person/navigation semantics remain unchanged.
- [ ] Focused component/integration tests and browser verification pass.

## Source finding

This item consolidates the non-semantic UI/navigation findings from `M19-maintainer-review-2026-08-19.md`. Keep scope visual/navigation only; new Photo Viewer metadata/location simplification belongs to WI-0077 and Face Review filtering belongs to WI-0074.
