# Build context

This file is intentionally a short handoff for the next development or verification session. It should describe only the current focus, the next concrete step and the small set of documents needed to continue.

Formal work-item lifecycle status and evidence are resolved by `PhotoIdentity.Docs` from the current registry plus archived terminal history.

## Current focus

**WI-0067 — Add featured representative faces for people** is the active M19 implementation item.

WI-0066 implementation is merged through PRs #170, #171 and #173. Per maintainer direction, its focused browser verification is deferred to the consolidated M19 manual verification pass after the remaining M19 implementation work is ready.

WI-0067 Slice 1 is implemented on `agent/WI-0067-featured-person-face`: a durable explicit person-to-face presentation preference, assignment-safe representative resolver, deterministic automatic fallback, reusable representative-face GET/PUT API contracts and focused integration coverage. A stale explicit face is ignored during resolution rather than ever showing another person's face.

WI-0065 remains in review pending the same consolidated M19 verification pass. WI-0069 is completed with CI timing evidence recorded.

## Next concrete step

1. Validate WI-0067 Slice 1 in GitHub Actions and merge after automated validation and code review.
2. Implement Slice 2: Face Details `Set as featured photo` / clear-to-automatic controls using the Slice 1 API.
3. Implement Slice 3: show resolved portraits in Maintain People and apply deterministic merge semantics.
4. Move WI-0067 to `in_review` with manual verification deferred to the consolidated M19 pass.
5. Implement WI-0068 searchable portrait-led Smart Collection people selection using the representative-face contract.
6. Run the consolidated M19 manual verification pass once the remaining M19 work items are ready.

## Relevant files

- `docs/delivery/status/work-items.yaml`
- `docs/delivery/work-items/WI-0067-featured-person-face.md`
- `src/PhotoIdentity.Persistence.Sqlite/SqlitePersonFeaturedFaceSchema.cs`
- `src/PhotoIdentity.Persistence.Sqlite/SqlitePersonFeaturedFaceRepository.cs`
- `src/PhotoIdentity.Api/PersonMaintenanceEndpoints.cs`
- `src/PhotoIdentity.Web/ReviewContracts.cs`
- `src/PhotoIdentity.Web/Pages/FaceDetails.razor`
- `src/PhotoIdentity.Web/Pages/People.razor`
- `tests/PhotoIdentity.Integration.Tests/PersonFeaturedFaceApplicationTests.cs`

## Repository validation

```powershell
./build.ps1
./test.ps1
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
./verify-review.ps1 -Mode Smoke -Configuration Release
```
