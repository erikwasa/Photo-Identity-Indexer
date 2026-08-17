# Build context

This file is intentionally a short handoff for the next development or verification session. It should describe only the current focus, the next concrete step and the small set of documents needed to continue.

Formal work-item lifecycle status and evidence are resolved by `PhotoIdentity.Docs` from the current registry plus archived terminal history.

## Current focus

**WI-0064 — Add GeoNames reverse-geocoded Places enrichment** is the active M19 implementation item.

WI-0061, WI-0062 and WI-0063 implementation are merged. Their remaining local browser/operator verification is intentionally deferred to the consolidated M19 pass after the GeoNames operator workflow is complete.

Slice 1 is draft PR #160 on `agent/WI-0064-geonames-foundation` and establishes the non-UI reverse-geocoding foundation:

- a provider-neutral reverse-geocoder contract and secure GeoNames `findNearbyPlaceNameJSON` implementation;
- private startup configuration with the public `demo` account and non-HTTPS provider URLs rejected;
- persisted-GPS-only candidate selection: enrichment does not open, hash or hydrate source photos;
- schema v15 cache and per-revision attempt state for bounded/resumable processing;
- cache reuse for identical coordinate/provider-contract inputs and explicit refresh for later automatic reinterpretation;
- automatic place writes respect any latest manual set or manual clear and unresolved WI-0063 migration conflicts;
- quota/transient provider states defer cleanly and remain retryable;
- explicit API status/batch endpoints with counts and no username exposure;
- fake HTTP/provider integration coverage; automated tests never call the live GeoNames service.

## Next concrete step

1. Validate PR #160 build, provider/integration tests, schema migration, living documentation, review smoke and Windows verification in GitHub Actions.
2. Merge Slice 1 after automated validation and code review.
3. Implement WI-0064 Slice 2: Settings/operator execution and refresh controls, external-GPS privacy disclosure and GeoNames attribution.
4. Run a small maintainer-configured live GeoNames sample plus the deferred WI-0061/WI-0062/WI-0063 browser checks as one consolidated M19 verification pass.
5. Close the M19 work items whose maintainer verification succeeds.

## Relevant files

- `docs/delivery/status/work-items.yaml`
- `docs/delivery/work-items/WI-0064-geonames-place-enrichment.md`
- `src/PhotoIdentity.Core/Places/ReverseGeocoding.cs`
- `src/PhotoIdentity.Persistence.Sqlite/SqliteCatalogueDatabase.cs`
- `src/PhotoIdentity.Persistence.Sqlite/SqlitePhotoPlaceEnrichmentRepository.cs`
- `src/PhotoIdentity.Persistence.Sqlite/SqliteAutomaticPhotoPlaceRepository.cs`
- `src/PhotoIdentity.Api/GeoNamesReverseGeocoder.cs`
- `src/PhotoIdentity.Api/PhotoPlaceEnrichmentService.cs`
- `src/PhotoIdentity.Api/PhotoPlaceEnrichmentEndpoints.cs`
- `tests/PhotoIdentity.Integration.Tests/GeoNamesReverseGeocoderTests.cs`
- `tests/PhotoIdentity.Integration.Tests/PhotoPlaceEnrichmentTests.cs`

## Repository validation

```powershell
./build.ps1
./test.ps1
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
./verify-review.ps1 -Mode Smoke -Configuration Release
```
