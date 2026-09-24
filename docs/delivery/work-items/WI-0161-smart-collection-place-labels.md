---
id: WI-0161
title: Show compact effective Place labels on Smart Collection photos
milestone: M28
status_source: ../status/work-items.yaml
depends_on: [WI-0144, WI-0157]
related_adrs: []
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Api, PhotoIdentity.Web, PhotoIdentity.Persistence.Postgres, PhotoIdentity.Integration.Tests, docs]
---

# WI-0161: Show compact effective Place labels on Smart Collection photos

## Objective

Replace raw latitude/longitude text under Smart Collection photo cards with a compact human-readable Place label whenever the photo has an effective named Place.

## Why

Coordinates are useful evidence but poor browsing labels. The application already has a canonical Places hierarchy populated by manual assignment and GPS-backed enrichment. Smart Collection cards should surface that human-readable location while respecting the same manual-versus-automatic precedence used elsewhere.

## In scope

- Return the effective named Place for each Smart Collection result, using the latest Place action regardless of whether it came from manual assignment or automatic metadata/GPS enrichment.
- Preserve manual-place precedence and existing clear/set semantics; do not infer a second competing display location directly from coordinates.
- Display a compact label derived from the Place hierarchy rather than raw coordinates.
- Default compact formatting to the first and last meaningful hierarchy components when there are more than two components, for example `Stockholm · Sweden`; retain both components for a two-level path and the single component for a one-level path.
- Use display/canonical Place names rather than normalized storage keys.
- Hide the location line when no effective named Place exists; do not show raw GPS coordinates as the normal card fallback.
- Keep full Place hierarchy/details available in Photo Details where appropriate.

## Out of scope

- Reverse geocoding during page rendering.
- Replacing stored GPS evidence.
- Changing Place enrichment or manual Place assignment semantics.
- Showing every administrative hierarchy component on the compact photo card.

## Acceptance criteria

- [ ] A Smart Collection result with a manually assigned Place shows a compact human-readable Place label rather than latitude/longitude.
- [ ] A result with an automatically enriched Place shows the same compact format.
- [ ] Manual Place overrides continue to win over automatic Place evidence according to the existing effective-Place model.
- [ ] A multi-level Place hierarchy is compacted to first and last meaningful display components without exposing normalized storage paths.
- [ ] A photo with coordinates but no effective named Place shows no coordinate label in the result card.
- [ ] Photo Details and stored metadata continue to retain GPS evidence unchanged.
- [ ] Automated tests cover manual, automatic, cleared/missing and multi-level Place cases.

## Verification requirements

Maintainer verification with representative photos covering a manual Place, a GPS-enriched Place, a reduced-precision administrative fallback and GPS with no resolved Place. Confirm labels are useful and compact on both desktop and phone.
