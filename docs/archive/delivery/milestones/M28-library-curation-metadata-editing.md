---
id: M28
title: Library curation and metadata editing
status_source: ../status/milestones.yaml
depends_on: [M19, M22, M24]
---

# M28: Library curation and metadata editing

## Outcome

Photo Identity makes common family-library corrections and curation tasks direct: Places are searchable, GPS-backed place enrichment can retain useful administrative geography when exact locality is unavailable, uncertain/manual capture dates are first-class and searchable, Smart Collection dates use structured controls, location filters can match multiple Places, and a viewer can assemble an explicit slideshow from chosen photos without changing Smart Collection semantics. Smart Collection browsing also keeps photo context during previous/next navigation, incrementally loads large result sets without visible paging, and shows compact effective Place labels instead of raw coordinate text.

## Delivery principles

- Preserve extracted metadata as source evidence; manual overrides carry provenance and precision.
- Keep Smart Collections as reusable query definitions. Explicit photo-list collections are separate.
- Reuse shared controls and slideshow snapshot/playback boundaries.
- Prefer compact searchable phone-friendly interactions over free-text mini-languages.
- Keep large Smart Collection browsing bounded and deterministic while preserving navigation/return context.
- Prefer effective named Place labels over raw coordinates on browsing surfaces.
- Never write metadata changes back into originals.

## Work items

- [WI-0140](../work-items/WI-0140-searchable-place-picker.md) - searchable shared Place picker.
- [WI-0141](../work-items/WI-0141-manual-capture-date-model.md) - manual capture-date override model with precision/provenance.
- [WI-0142](../work-items/WI-0142-manual-capture-date-editor.md) - Photo Details date editing.
- [WI-0143](../work-items/WI-0143-structured-smart-collection-date-controls.md) - structured Smart Collection date controls.
- [WI-0144](../work-items/WI-0144-smart-collection-multiple-locations.md) - multiple named Places with ANY semantics.
- [WI-0145](../work-items/WI-0145-manual-slideshow-collection-model.md) - explicit ordered photo-list collection model.
- [WI-0146](../work-items/WI-0146-manual-slideshow-curation-ui.md) - manual slideshow curation and launch UI.
- [WI-0157](../work-items/WI-0157-reduced-precision-place-fallback.md) - retain less precise administrative Place hierarchy when GPS cannot resolve a populated place.
- [WI-0159](../work-items/WI-0159-smart-collection-photo-navigation.md) - preserve Smart Collection context while navigating previous/next photos and returning to results.
- [WI-0160](../work-items/WI-0160-smart-collection-infinite-scroll.md) - replace visible paging with bounded incremental infinite scrolling.
- [WI-0161](../work-items/WI-0161-smart-collection-place-labels.md) - show compact effective named Place labels on Smart Collection result cards.

## Delivery sequence

1. WI-0140 and WI-0141 can begin independently.
2. WI-0142/WI-0143 consume the effective-date model.
3. WI-0144 reuses searchable Place interaction.
4. WI-0145 establishes explicit collection semantics independently from Smart Collections.
5. WI-0146 adds curation after the explicit collection contract is stable.
6. WI-0157 can proceed independently on the existing GeoNames enrichment boundary and must preserve manual-place precedence.
7. WI-0159 establishes deterministic in-context Smart Collection photo navigation and return semantics.
8. WI-0160 builds infinite scrolling on that navigation contract, while WI-0161 independently improves the compact Place presentation on result cards.

## Exit criteria

- [x] Place assignment/filtering no longer requires scrolling a long dropdown.
- [x] Manual dates preserve year-only/year-month uncertainty with provenance and survive metadata reinspection.
- [x] Smart Collection date filtering is structured and searches effective dates consistently.
- [x] Smart Collections can match any of multiple selected Place hierarchies.
- [x] Manual slideshow collections remain explicit revision lists rather than mutable Smart Collection results.
- [x] Explicit collections can be curated and launched through normal slideshow playback.
- [x] GPS-backed photos can retain useful country/administrative Place hierarchy when no populated-place match exists, without inventing locality precision.
- [x] Smart Collection Photo Details navigation preserves collection ordering/context and a useful return position.
- [x] Large Smart Collections browse through bounded infinite scrolling without duplicate/skip behavior or visible previous/next paging controls.
- [x] Smart Collection cards use compact effective named Places and do not fall back to raw coordinates when no named Place exists.

## Completion

M28 originally reached its core curation exit criteria on 2026-09-23 after maintainer desktop/phone acceptance of WI-0145/WI-0146. Follow-up browsing items WI-0159, WI-0160 and WI-0161 were then added to close the remaining Smart Collection navigation and presentation gaps.

On 2026-09-25 the maintainer accepted WI-0159 and then confirmed WI-0160 and WI-0161 work as expected after PR #422. All M28 work items and expanded exit criteria are now complete, so M28 is closed as completed on 2026-09-25.
