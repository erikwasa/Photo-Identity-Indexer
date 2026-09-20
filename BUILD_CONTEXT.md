# Build context

This file is intentionally a short handoff for the next development or verification session. Formal work-item lifecycle status is resolved through `PhotoIdentity.Docs`.

## Current focus

**M28 Library curation and metadata editing is continuing with WI-0142: manual and imprecise capture-date editing in Photo Details.**

WI-0140's searchable shared Place picker merged in PR #391 and WI-0141's provenance/precision capture-date model merged in PR #392. Their requested maintainer verification is intentionally being bundled with the later M28 verification pass.

PR #394 adds the Photo Details consumer for WI-0141. The details response now exposes effective capture-date source, precision, range and preserved extracted timestamp. PostgreSQL-backed PUT/DELETE mutations set or clear the manual date. The new responsive editor accepts `YYYY`, `YYYY-MM` or `YYYY-MM-DD`, shows manual versus extracted provenance explicitly and never rewrites source metadata. The existing metadata grid labels the raw source value as `Extracted capture time`.

Automated coverage validates editor input, server validation, year/month/day replacement, persistence across an API-host restart and clear-to-extracted behavior. The remaining WI-0142 acceptance item is the bundled desktop/phone verification.

M26 remains active separately. M29 is ready. M30 video support remains intentionally blocked until explicit maintainer reactivation.

## Next concrete step

Validate PR #394 through CI and merge when green. After merge, WI-0143 can consume the effective date-range model for structured Smart Collection controls while manual verification remains deferred until the requested M28 batch review.

## Relevant files

- docs/delivery/milestones/M28-library-curation-metadata-editing.md
- docs/delivery/work-items/WI-0142-manual-capture-date-editor.md
- docs/delivery/status/work-items/active/WI-0142.yaml
- src/PhotoIdentity.Api/PhotoDetailsEndpoints.cs
- src/PhotoIdentity.Web/Components/PhotoCaptureDateEditor.razor
- src/PhotoIdentity.Web/Components/PhotoCaptureDateEditor.razor.css
- src/PhotoIdentity.Web/PhotoCaptureDateEditorModel.cs
- src/PhotoIdentity.Web/PhotoDetailsContracts.cs
- tests/PhotoIdentity.Integration.Tests/ManualCaptureDateApplicationTests.cs
- tests/PhotoIdentity.Integration.Tests/PhotoCaptureDateEditorModelTests.cs

## Repository validation

    ./build.ps1
    ./test.ps1
    dotnet run --project tools/PhotoIdentity.Docs -- validate
    dotnet run --project tools/PhotoIdentity.Docs -- generate --check
    ./verify-postgres.ps1
