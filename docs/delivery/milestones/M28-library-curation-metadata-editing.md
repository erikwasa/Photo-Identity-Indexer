---
id: M28
title: Library curation and metadata editing
status_source: ../status/milestones.yaml
depends_on: [M19, M22, M24]
---

# M28: Library curation and metadata editing

## Outcome

Photo Identity makes common family-library corrections and curation tasks direct: Places are searchable, GPS-backed place enrichment can retain useful administrative geography when exact locality is unavailable, uncertain/manual capture dates are first-class and searchable, Smart Collection dates use structured controls, location filters can match multiple Places, and a viewer can assemble an explicit slideshow from chosen photos without changing Smart Collection semantics.

## Delivery principles

- Preserve extracted metadata as source evidence; manual overrides carry provenance and precision.
- Keep Smart Collections as reusable query definitions. Explicit photo-list collections are separate.
- Reuse shared controls and slideshow snapshot/playback boundaries.
- Prefer compact searchable phone-friendly interactions over free-text mini-languages.
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

## Delivery sequence

1. WI-0140 and WI-0141 can begin independently.
2. WI-0142/WI-0143 consume the effective-date model.
3. WI-0144 reuses searchable Place interaction.
4. WI-0145 establishes explicit collection semantics independently from Smart Collections.
5. WI-0146 adds curation after the explicit collection contract is stable.
6. WI-0157 can proceed independently on the existing GeoNames enrichment boundary and must preserve manual-place precedence.

## Exit criteria

- [ ] Place assignment/filtering no longer requires scrolling a long dropdown.
- [ ] Manual dates preserve year-only/year-month uncertainty with provenance and survive metadata reinspection.
- [ ] Smart Collection date filtering is structured and searches effective dates consistently.
- [ ] Smart Collections can match any of multiple selected Place hierarchies.
- [ ] Manual slideshow collections remain explicit revision lists rather than mutable Smart Collection results.
- [ ] Explicit collections can be curated and launched through normal slideshow playback.
- [ ] GPS-backed photos can retain useful country/administrative Place hierarchy when no populated-place match exists, without inventing locality precision.
