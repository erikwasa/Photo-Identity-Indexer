---
id: WI-0140
title: Replace long place dropdowns with a searchable place picker
milestone: M28
status_source: ../status/work-items.yaml
depends_on: [WI-0063]
related_adrs: []
affected_modules: [PhotoIdentity.Web, PhotoIdentity.Integration.Tests, docs]
---

# WI-0140: Replace long place dropdowns with a searchable place picker

## Objective

Make existing Place vocabulary quick to find when assigning or filtering locations.

## Why

The shared PlacePicker currently renders the complete hierarchy as a select dropdown, which becomes cumbersome as the reusable place vocabulary grows.

## In scope

- Replace the shared select with an accessible searchable combobox/autocomplete.
- Preserve canonical hierarchical values and parent labels.
- Reuse it in Photo Details and Smart Collections.
- Filter locally first unless measured vocabulary size justifies server search.
- Preserve keyboard/mobile usability and clear selection.

## Out of scope

- Changing Place hierarchy semantics.
- Adding external geocoding to the picker.

## Acceptance criteria

- [x] Typing a place fragment narrows the reusable vocabulary.
- [x] Selection stores the same canonical Place path.
- [x] Photo Details and Smart Collections use the same component.
- [ ] Keyboard/mobile interaction has focused coverage.

## Verification requirements

Automated component/integration coverage plus a desktop and phone interaction check.

## Completion notes

- Files changed: shared `PlacePicker` Razor/CSS, new `PlacePickerModel`, focused integration tests and delivery status/handoff files.
- Trade-offs: filtering stays client-side against the already-loaded Place vocabulary and renders at most 24 matches at once. Search matches leaf name, canonical value and parent value; the authoritative selected value remains the unchanged canonical Place path.
- Deferred work: maintainer desktop and phone interaction verification remains required before completion. Server-side Place search remains intentionally deferred until vocabulary size is measured to justify it.
- Commands run: implementation prepared through the GitHub connector; normal build/integration/docs CI runs on PR #391 because this agent session has no local repository checkout.
