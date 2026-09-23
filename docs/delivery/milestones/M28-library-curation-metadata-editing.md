---
id: M28
title: Library curation and metadata editing
status_source: ../status/milestones.yaml
depends_on: [M19, M22, M24]
---

# M28: Library curation and metadata editing

## Outcome

Photo Identity makes common family-library corrections and curation tasks direct: Places are searchable, GPS-backed place enrichment can retain useful administrative geography when exact locality is unavailable, uncertain/manual capture dates are first-class and searchable, Smart Collection dates use structured controls, location filters can match multiple Places, a viewer can assemble an explicit slideshow from chosen photos without changing Smart Collection semantics, and Smart Collection browsing can remain fluid across long result sets and Photo Details.

## Delivery principles

- Preserve extracted metadata as source evidence; manual overrides carry provenance and precision.
- Keep Smart Collections as reusable query definitions. Explicit photo-list collections are separate.
- Reuse shared controls and slideshow snapshot/playback boundaries.
- Prefer compact searchable phone-friendly interactions over free-text mini-languages.
- Never write metadata changes back into originals.
- Prefer human-readable effective Place labels over raw coordinate display in browsing surfaces while retaining GPS as evidence.
- Keep browser navigation and scrolling incremental; do not turn convenient UI browsing into unbounded server queries.

## Work items

- [WI-0140](../work-items/WI-0140-searchable-place-picker.md) - searchable shared Place picker.
- [WI-0141](../work-items/WI-0141-manual-capture-date-model.md) - manual capture-date override model with precision/provenance.
- [WI-0142](../work-items/WI-0142-manual-capture-date-editor.md) - Photo Details date editing.
- [WI-0143](../work-items/WI-0143-structured-smart-collection-date-controls.md) - structured Smart Collection date controls.
- [WI-0144](../work-items/WI-0144-smart-collection-multiple-locations.md) - multiple named Places with ANY semantics.
- [WI-0145](../work-items/WI-0145-manual-slideshow-collection-model.md) - explicit ordered photo-list collection model.
- [WI-0146](../work-items/WI-0146-manual-slideshow-curation-ui.md) - manual slideshow curation and launch UI.
- [WI-0157](../work-items/WI-0157-reduced-precision-place-fallback.md) - retain less precise administrative Place hierarchy when GPS cannot resolve a populated place.
- [WI-0159](../work-items/WI-0159-smart-collection-photo-navigation.md) - retain Smart Collection context in Photo Details and navigate directly to previous/next results.
- [WI-0160](../work-items/WI-0160-smart-collection-infinite-scroll.md) - replace visible paging with bounded incremental infinite scrolling.
- [WI-0161](../work-items/WI-0161-smart-collection-place-labels.md) - replace raw GPS card labels with compact effective Place names from manual or enriched location evidence.

## Delivery sequence

1. WI-0140 and WI-0141 can begin independently.
2. WI-0142/WI-0143 consume the effective-date model.
3. WI-0144 reuses searchable Place interaction.
4. WI-0145 establishes explicit collection semantics independently from Smart Collections.
5. WI-0146 adds curation after the explicit collection contract is stable.
6. WI-0157 can proceed independently on the existing GeoNames enrichment boundary and must preserve manual-place precedence.
7. WI-0159 follows WI-0146 so Photo Details navigation can preserve the final Smart Collection return-state contract.
8. WI-0160 follows WI-0159 so infinite-scroll restoration and Photo Details return behavior share one navigation model rather than two competing paging models.
9. WI-0161 can proceed independently after WI-0144/WI-0157 because it only consumes the established effective Place hierarchy for display.

## Exit criteria

- [x] Place assignment/filtering no longer requires scrolling a long dropdown.
- [x] Manual dates preserve year-only/year-month uncertainty with provenance and survive metadata reinspection.
- [x] Smart Collection date filtering is structured and searches effective dates consistently.
- [x] Smart Collections can match any of multiple selected Place hierarchies.
- [x] Manual slideshow collections remain explicit revision lists rather than mutable Smart Collection results.
- [ ] Explicit collections can be curated and launched through normal slideshow playback.
- [x] GPS-backed photos can retain useful country/administrative Place hierarchy when no populated-place match exists, without inventing locality precision.
- [ ] Smart Collection Photo Details can traverse neighboring results without returning to the collection between photos.
- [ ] Long Smart Collection result sets can be browsed with bounded infinite scrolling instead of explicit paging.
- [ ] Smart Collection cards show compact effective Place names when available rather than raw coordinates.
