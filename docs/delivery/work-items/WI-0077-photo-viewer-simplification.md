---
id: WI-0077
title: Simplify Photo Viewer metadata and location editing
milestone: M20
status_source: ../status/work-items.yaml
depends_on: [WI-0061, WI-0063, WI-0072]
related_adrs: []
affected_modules: [PhotoIdentity.Web, PhotoIdentity.Api]
---

# WI-0077: Simplify Photo Viewer metadata and location editing

## Objective

Reduce visual density on Photo Details so the page emphasizes the information most useful during normal browsing, while keeping detailed photographic metadata and Place editing available on demand.

## Metadata presentation

Keep the primary/always-visible metadata compact. Do **not** show these as separate always-visible rows:

- Lens;
- Exposure;
- Aperture;
- ISO;
- Focal length;
- Orientation;
- Flash;
- GPS altitude.

These values remain persisted and available inside the existing collapsed **All metadata** section.

The compact visible metadata should retain the fields that are most useful at a glance, including photographic capture time, camera make/model and exact GPS coordinates when present. The named Place remains part of the separate Location section rather than being duplicated as raw metadata.

## Location presentation

Normal/read mode:

- show the current effective Location/Place clearly;
- if no Place is assigned, show an explicit neutral state such as `No location assigned`;
- hide location input fields and mutation buttons;
- show one **Edit location** action.

Edit mode after `Edit location`:

- reveal the existing location search/input controls and applicable Set/Replace/Clear buttons;
- initialize the editor from the current effective Place without changing it merely by entering edit mode;
- provide Cancel so the maintainer can leave edit mode without mutation;
- after successful Set/Replace/Clear, return to read mode and show the new effective Location;
- preserve WI-0063 single-effective-Place, hierarchy and manual-precedence semantics.

## Implementation — 2026-08-20

The implementation is on `agent/WI-0077-photo-viewer-simplification` and is intentionally awaiting the maintainer's later consolidated M20 browser review.

- `PhotoMetadataPanel` now keeps only capture time, camera make/model and exact GPS coordinates in the always-visible grid.
- Lens, exposure, aperture, ISO, focal length/35 mm equivalent, orientation, flash and GPS altitude are rendered as structured technical details inside the collapsed `All metadata` disclosure. The bounded raw-tag table remains beneath those structured values when raw tags are available.
- `PhotoPlaceEditor` now defaults to read mode, showing the effective Place or `No location assigned` plus one `Edit location` button.
- Entering edit mode reveals the existing Place picker/path field and Set/Replace/Clear controls plus Cancel. Entering or cancelling edit mode does not mutate catalogue data.
- Successful Set/Replace/Clear returns to read mode immediately. Existing API, first-class Place, manual-precedence and single-effective-Place semantics are unchanged.
- Narrow-screen CSS stacks the location value and Edit action rather than forcing them into one row.

No persistence, API, original-file access, hydration or GeoNames behavior changes are part of this implementation.

## Acceptance criteria

- [x] Lens, Exposure, Aperture, ISO, Focal length, Orientation, Flash and GPS altitude are not shown in the always-visible key metadata grid.
- [x] Those fields remain available in collapsed `All metadata` when present in the persisted raw/structured metadata.
- [x] Capture time, camera make/model and exact GPS coordinates remain visible when present.
- [x] Location defaults to a read-only presentation of the current effective Place.
- [x] Location input fields and mutation buttons are hidden until `Edit location` is activated.
- [x] Edit mode supports Set/Replace/Clear plus Cancel without changing existing Place semantics.
- [x] Successful location mutation exits edit mode and immediately reflects the effective Place.
- [x] No private source path or original-file hydration is introduced by the presentation change.
- [ ] Consolidated browser verification covers assigned and unassigned Place states, edit/cancel/mutation flow, collapsed metadata and narrow-screen layout.

## Non-goals

- Do not remove detailed metadata from persistence/API solely because it moves out of the key view.
- Do not change GeoNames enrichment, Places hierarchy or manual/automatic precedence rules.
- Do not redesign the full Photo Details navigation contract in this item; archive return-context polish belongs to WI-0073.
