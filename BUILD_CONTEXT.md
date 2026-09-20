# Build context

This file is intentionally a short handoff for the next development or verification session. Formal work-item lifecycle status is resolved through `PhotoIdentity.Docs`.

## Current focus

**M28 Library curation and metadata editing is underway with WI-0140: searchable shared Place picker.**

PR #391 replaces the shared full-hierarchy Place `<select>` with a local searchable combobox used unchanged by both Photo Details and Smart Collections. Search matches leaf names, canonical paths and parent paths; selection still emits the exact canonical Place path, while Photo Details keeps its separate free-form Place path field for creating new vocabulary.

The result list is bounded to 24 rendered matches, provides explicit clear-selection behavior and supports Arrow Up/Down, Home, End, Enter and Escape. Focused model coverage protects fragment filtering, duplicate locality disambiguation, canonical-value preservation and keyboard navigation. Desktop and phone interaction acceptance remains pending before WI-0140 can complete.

M26 remains active with WI-0128 tracked separately. M29 is ready. M30 video support remains intentionally blocked until explicit maintainer reactivation.

## Next concrete step

Validate PR #391 through the normal build/integration/docs gates. Then verify the Place picker on desktop and phone in both Photo Details and Smart Collections before completing WI-0140. WI-0141 can proceed independently if another M28 item is desired before manual review.

## Relevant files

- docs/delivery/milestones/M28-library-curation-metadata-editing.md
- docs/delivery/work-items/WI-0140-searchable-place-picker.md
- docs/delivery/status/work-items/active/WI-0140.yaml
- src/PhotoIdentity.Web/Components/PlacePicker.razor
- src/PhotoIdentity.Web/Components/PlacePicker.razor.css
- src/PhotoIdentity.Web/Components/PlacePickerModel.cs
- tests/PhotoIdentity.Integration.Tests/PlacePickerModelTests.cs

## Repository validation

    ./build.ps1
    ./test.ps1
    dotnet run --project tools/PhotoIdentity.Docs -- validate
    dotnet run --project tools/PhotoIdentity.Docs -- generate --check
    ./verify-postgres.ps1
