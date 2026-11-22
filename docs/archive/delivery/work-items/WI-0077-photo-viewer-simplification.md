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

The compact visible metadata should retain the fields that are most useful at a glance, including photographic capture time, camera make/model and exact GPS coordinates when present. The named Place remains a first-class Location presentation rather than being reduced to raw metadata.

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

The first implementation was merged through PR #199.

- `PhotoMetadataPanel` keeps only capture time, camera make/model and exact GPS coordinates in the always-visible grid.
- Lens, exposure, aperture, ISO, focal length/35 mm equivalent, orientation, flash and GPS altitude are rendered as structured technical details inside the collapsed `All metadata` disclosure. The bounded raw-tag table remains beneath those structured values when raw tags are available.
- `PhotoPlaceEditor` defaults to read mode, showing the effective Place or `No location assigned` plus one `Edit location` button.
- Entering edit mode reveals the existing Place picker/path field and Set/Replace/Clear controls plus Cancel. Entering or cancelling edit mode does not mutate catalogue data.
- Successful Set/Replace/Clear returns to read mode immediately. Existing API, first-class Place, manual-precedence and single-effective-Place semantics are unchanged.

No persistence, original-file access, hydration or GeoNames behavior changes were part of that first implementation.

## Maintainer review — 2026-08-21

The metadata-field visibility and location edit/read behavior were verified successfully. The remaining corrective work is presentation/layout.

### At-a-glance metadata layout

- Avoid ordinary desktop values wrapping across three lines.
- Prefer a compact two-column desktop arrangement with one-column fallback on narrow screens.
- `Photo taken` should normally fit on one line at desktop widths.
- Camera make/model may be combined into one **Camera** value when that improves readability and fit.
- GPS coordinates must not be forced to span the entire grid; use an ordinary metadata cell.
- Do not use aggressive `overflow-wrap:anywhere` for short structured values as the normal desktop behavior.

### Location position

Refactor component/page ownership as needed so the visible order is:

1. capture metadata/GPS;
2. Location;
3. People.

The named Place should appear close to the GPS coordinates rather than after the People editor.

### Compact location label

The complete canonical hierarchy remains authoritative for persistence, editing and Smart Collection filtering. Read mode should not normally print the entire hierarchy when it is long.

Example stored value:

```text
Sverige/Stockholms län/Stockholms stad/Brännkyrka/Långbro
```

Target read-mode presentation is **city + most-specific locality**, for example:

```text
Stockholm · Långbro
```

Requirements:

- do not infer the city by blindly taking a fixed positional segment; GeoNames administrative depth varies by country;
- for GeoNames-derived Places, expose or persist enough semantic display information to identify the appropriate city/locality reliably, or use a deterministic provider-aware compact-label rule;
- retain the full hierarchy in edit mode and optionally as tooltip/secondary detail;
- manual Places without provider semantic metadata need a deterministic compact fallback while the full stored path remains authoritative;
- do not change WI-0063 hierarchy/query semantics merely to shorten display text.

Full review notes are in `../milestones/M20-maintainer-review-2026-08-21.md`.

## Acceptance criteria

- [x] Lens, Exposure, Aperture, ISO, Focal length, Orientation, Flash and GPS altitude are not shown in the always-visible key metadata grid.
- [x] Those fields remain available in collapsed `All metadata` when present in the persisted raw/structured metadata.
- [x] Capture time, camera make/model and exact GPS coordinates remain visible when present.
- [x] Location defaults to a read-only presentation of the current effective Place.
- [x] Location input fields and mutation buttons are hidden until `Edit location` is activated.
- [x] Edit mode supports Set/Replace/Clear plus Cancel without changing existing Place semantics.
- [x] Successful location mutation exits edit mode and immediately reflects the effective Place.
- [x] No private source path or original-file hydration is introduced by the presentation change.
- [ ] At-a-glance metadata uses a compact desktop layout without unnecessary multi-line wrapping, and GPS no longer spans the full width.
- [ ] Location appears immediately after capture metadata/GPS and before People.
- [ ] Read mode shows a compact city + most-specific-locality label where semantic location data supports it, while preserving the full canonical hierarchy for editing/querying.
- [ ] Final browser verification covers assigned/unassigned Place states, edit/cancel/mutation flow, collapsed metadata and narrow-screen layout after the corrective slice.

## Non-goals

- Do not remove detailed metadata from persistence/API solely because it moves out of the key view.
- Do not change Places hierarchy or manual/automatic precedence rules.
- Do not redesign the full Photo Details navigation contract in this item; archive return-context polish belongs to WI-0073.
