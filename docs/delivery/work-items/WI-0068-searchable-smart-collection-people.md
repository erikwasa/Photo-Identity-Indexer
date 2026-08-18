---
id: WI-0068
title: Make Smart Collection people selection searchable and portrait-led
milestone: M19
status_source: ../status/work-items.yaml
depends_on: [WI-0050, WI-0066, WI-0067]
related_adrs: []
affected_modules: [PhotoIdentity.Web, PhotoIdentity.Api]
---

# WI-0068: Make Smart Collection people selection searchable and portrait-led

## Objective

Replace the current long Smart Collection people checkbox list with a searchable multi-select experience that remains efficient as the catalogue contains many named people.

The selector must preserve the existing `all` / `any` Smart Collection query contract and stable PersonId-based saved definitions. It should consume WI-0066 visibility metadata and WI-0067 representative portraits rather than inventing separate Smart Collection person state.

## User contract

- The People filter has an incremental case-insensitive search field.
- Search filters by display name without changing the underlying selection.
- Search results show the person's representative portrait and display name.
- Hidden people from WI-0066 are excluded from normal search/discovery.
- Selected people remain clearly visible even when they no longer match the current search text.
- Selected people can be removed directly without first searching for them again.
- Favorites may continue to influence ordering, but search relevance and deterministic name ordering must remain understandable.
- `All selected` and `Any selected` keep their current semantics.

## Selection behavior

The editor must continue to store and submit stable canonical PersonId values rather than display names. Renaming a person therefore must not break an existing saved Smart Collection.

Changing search text must not implicitly add or remove people. Clearing search restores the normal visible-person result set. Empty/no-match states should be explicit rather than appearing as a broken picker.

For a saved definition that already references a now-hidden person, WI-0066 compatibility rules take precedence: the hidden person remains visible in the selected-person area with a hidden indicator and can be removed, but it is not discoverable as a new search result.

## Portrait behavior

Use the resolved representative face supplied by WI-0067. The selector must not calculate its own random portrait. If a representative image cannot be resolved, show a stable neutral fallback while keeping the person selectable.

Portrait loading should use browser-safe local URLs and existing derivative infrastructure. The search interaction must not hydrate original photos or expose source paths/filenames.

## Performance and accessibility

The first implementation may filter the already-loaded canonical people list client-side if the catalogue size remains appropriate. If measured list size or payload cost makes this unsuitable, introduce a bounded server search endpoint while preserving the same UI contract.

The picker must remain keyboard usable and expose meaningful labels for search results, selected people and remove actions. Portraits are supplementary identity cues; the display name remains the authoritative accessible label.

## In scope

- Incremental display-name search in the modern `/smart-collections` People filter.
- Portrait + name search results.
- Persistent selected-person area/chips that survive search filtering.
- WI-0066 hidden-person behavior.
- WI-0067 explicit/automatic portrait reuse.
- Preservation of PersonId and `all`/`any` saved-query semantics.
- Accessibility and no-result states.

## Out of scope

Fuzzy biometric search, searching by filenames/tags, changing Smart Collection query semantics, person aliases, global application search, and modifying the legacy `/collections` workspace unless a shared component is extracted deliberately.

## Acceptance criteria

- [ ] Typing in the Smart Collection People search filters visible candidates by display name case-insensitively.
- [ ] Search text never changes existing selections implicitly.
- [ ] Each normal candidate shows the resolved representative portrait and display name, with a safe fallback if no portrait resolves.
- [ ] Hidden people are absent from normal search/discovery.
- [ ] Already-selected hidden people from saved definitions remain visible as selected with an explicit hidden indicator and can be removed.
- [ ] Selected people remain visible and removable when the current search text does not match them.
- [ ] Clearing search restores the normal candidate list.
- [ ] Existing `all` and `any` people matching semantics are unchanged.
- [ ] Saved definitions continue to persist PersonId values and survive person renames.
- [ ] Search/portrait rendering does not hydrate originals or expose source paths/filenames.
- [ ] The picker is keyboard usable and display names remain the accessible identity label.
- [ ] Automated UI/component coverage verifies search filtering, retained selection, hidden-person compatibility and portrait fallback.

## Suggested implementation slices

1. Searchable multi-select interaction with retained selections and existing all/any behavior.
2. Integrate WI-0066 visibility rules and saved hidden-person compatibility.
3. Integrate WI-0067 representative portraits, fallback and accessibility coverage.
