# Build context

This file is intentionally a short handoff for the next development or verification session. It should describe only the current focus, the next concrete step and the small set of documents needed to continue.

Formal work-item lifecycle status and evidence are resolved by `PhotoIdentity.Docs` from the current registry plus archived terminal history.

## Current focus

**WI-0062 — Add manual photo-level people** is the active M19 implementation item.

WI-0061 implementation merged through PR #154. Its local browser verification is intentionally deferred and will be performed together with the remaining M19 work items in the consolidated maintainer pass.

WI-0062 keeps photo-level manual presence separate from face evidence:

- append-only revision/person add/remove actions retain audit history;
- Photo Details consolidates confirmed-face and manual-presence evidence without duplicate people;
- manual add/remove does not create face occurrences, crops, embeddings, review actions or identity suggestions;
- Smart Collections People filters query the union of confirmed faces and active manual presence with existing all/any semantics;
- person merge transfers effective manual presence to the canonical target while preserving historical source-person actions;
- all operations are catalogue-only and do not open or hydrate originals.

## Next concrete step

1. Validate draft PR #155 build, integration tests, living documentation and review smoke in GitHub Actions.
2. Merge WI-0062 after automated validation and code review; defer its local browser verification to the consolidated M19 pass.
3. Continue with WI-0063 first-class Places.
4. After WI-0063, implement WI-0064 GeoNames place enrichment.
5. Perform the consolidated local M19 browser/operator verification, then close the remaining in-review work items and milestone as appropriate.

## Relevant files

- `docs/delivery/status/work-items.yaml`
- `docs/delivery/work-items/WI-0062-manual-photo-people.md`
- `src/PhotoIdentity.Persistence.Sqlite/SqlitePhotoPersonRepository.cs`
- `src/PhotoIdentity.Persistence.Sqlite/SqlitePhotoDetailsRepository.cs`
- `src/PhotoIdentity.Persistence.Sqlite/SqliteSmartCollectionQueryRepository.cs`
- `src/PhotoIdentity.Api/PhotoDetailsEndpoints.cs`
- `src/PhotoIdentity.Web/Components/ManualPhotoPeopleEditor.razor`
- `src/PhotoIdentity.Web/Pages/Photo.razor`
- `tests/PhotoIdentity.Integration.Tests/ManualPhotoPeopleApplicationTests.cs`

## Repository validation

```powershell
./build.ps1
./test.ps1
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
./verify-review.ps1 -Mode Smoke -Configuration Release
```
