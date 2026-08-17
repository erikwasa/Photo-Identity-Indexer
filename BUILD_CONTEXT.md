# Build context

This file is intentionally a short handoff for the next development or verification session. It should describe only the current focus, the next concrete step and the small set of documents needed to continue.

Formal work-item lifecycle status and evidence are resolved by `PhotoIdentity.Docs` from the current registry plus archived terminal history.

## Current focus

**WI-0064 — Add GeoNames reverse-geocoded Places enrichment** is the active M19 implementation item.

WI-0061, WI-0062 and WI-0063 implementation are merged. Their remaining local browser/operator verification is intentionally deferred to the consolidated M19 pass after the GeoNames operator workflow is complete.

Slice 1 merged through PR #160 and established the provider/persistence foundation. Slice 2 is draft PR #161 on `agent/WI-0064-geonames-settings` and adds the operator workflow:

- Settings reports GeoNames configured/disabled state without exposing the configured username;
- bounded 1–250 candidate execution and explicit automatic-place force refresh are available from the browser;
- latest in-session candidate/provider/cache/assignment/skip/deferred/failure counts are visible;
- the browser states the external-GPS privacy boundary before execution and keeps credentials as startup/server-side configuration only;
- GeoNames attribution is presented with provider-derived place enrichment;
- endpoint tests cover safe status, disabled execution and batch bounds without live provider calls.

The persistence/provider foundation remains unchanged: enrichment reads persisted GPS only, never opens or hydrates originals, preserves manual place precedence, reuses safe cache entries and leaves deferred/failed attempts retryable.

## Next concrete step

1. Validate draft PR #161 build, integration tests, living/generated documentation, review smoke and Windows verification in GitHub Actions.
2. Merge Slice 2 after automated validation and code review.
3. Configure a maintainer GeoNames account locally and run a small bounded live sample from Settings; compare several resulting place paths with expected real-world locations.
4. In the same consolidated M19 pass, perform the deferred WI-0061/WI-0062/WI-0063 browser/operator checks and record evidence.
5. Close the M19 work items whose maintainer verification succeeds.

## Relevant files

- `docs/delivery/status/work-items.yaml`
- `docs/delivery/work-items/WI-0064-geonames-place-enrichment.md`
- `src/PhotoIdentity.Core/Places/ReverseGeocoding.cs`
- `src/PhotoIdentity.Persistence.Sqlite/SqlitePhotoPlaceEnrichmentRepository.cs`
- `src/PhotoIdentity.Persistence.Sqlite/SqliteAutomaticPhotoPlaceRepository.cs`
- `src/PhotoIdentity.Api/GeoNamesReverseGeocoder.cs`
- `src/PhotoIdentity.Api/PhotoPlaceEnrichmentService.cs`
- `src/PhotoIdentity.Api/PhotoPlaceEnrichmentEndpoints.cs`
- `src/PhotoIdentity.Web/PlaceEnrichmentContracts.cs`
- `src/PhotoIdentity.Web/Components/GeoNamesPlaceEnrichmentSettings.razor`
- `src/PhotoIdentity.Web/Pages/Settings.razor`
- `tests/PhotoIdentity.Integration.Tests/GeoNamesReverseGeocoderTests.cs`
- `tests/PhotoIdentity.Integration.Tests/PhotoPlaceEnrichmentTests.cs`
- `tests/PhotoIdentity.Integration.Tests/PhotoPlaceEnrichmentEndpointTests.cs`

## Repository validation

```powershell
./build.ps1
./test.ps1
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
./verify-review.ps1 -Mode Smoke -Configuration Release
```
