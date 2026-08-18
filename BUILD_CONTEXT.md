# Build context

This file is intentionally a short handoff for the next development or verification session. It should describe only the current focus, the next concrete step and the small set of documents needed to continue.

Formal work-item lifecycle status and evidence are resolved by `PhotoIdentity.Docs` from the current registry plus archived terminal history.

## Current focus

**WI-0066 — Add Smart Collection visibility preference for people** is the active M19 implementation item.

Slice 1 merged through PR #170 and establishes schema v16, a narrowly scoped durable `HiddenFromSmartCollections` preference, maintenance API read/write contracts, deterministic target-wins merge semantics and integration coverage. Hidden people remain part of ordinary review/identity people lists.

Slice 2 merged through PR #171. Maintain People now shows whether each active person is available or hidden from Smart Collections and provides a reversible hide/show control backed by the Slice 1 endpoint. Hidden people remain fully present in Maintain People.

Slice 3 is implemented on `agent/WI-0066-smart-collection-discovery`. `/api/review/people` still returns every active person but now exposes the persisted Smart Collection visibility flag. The Smart Collection people picker hides those people from normal discovery while retaining and marking a hidden person already selected by a restored saved or transient definition. Removing that hidden selection makes it unavailable for re-selection until unhidden. Saved definitions and query semantics are not rewritten by the preference.

WI-0065 implementation merged through PR #166 and is in review pending maintainer verification of unattended pickup and restart/resume behavior.

WI-0069 is completed: the CI optimization merged through PR #169 and successful workflow #1075 verified the mixed-media checkpoint reuses prior build/test/documentation validation instead of rerunning the integration suite. Timing and merge evidence are recorded in the work item.

## Next concrete step

1. Validate WI-0066 Slice 3 in GitHub Actions, including build/tests, living/generated documentation, review smoke and Windows verification.
2. Merge Slice 3 after automated validation and code review.
3. Run the focused maintainer browser pass for WI-0066: hide a person, confirm Maintain People state survives reload, confirm face/review workflows remain unchanged, confirm the person is absent from a new Smart Collection, and confirm a previously saved collection still shows that hidden selected person and reevaluates normally.
4. Record PR/verification evidence and move WI-0066 to `in_review`.
5. Continue with WI-0067 featured representative faces, followed by WI-0068 searchable portrait-led Smart Collection people selection.

## Relevant files

- `docs/delivery/status/work-items.yaml`
- `docs/delivery/work-items/WI-0066-smart-collection-person-visibility.md`
- `src/PhotoIdentity.Persistence.Sqlite/SqlitePersonSmartCollectionVisibilityRepository.cs`
- `src/PhotoIdentity.Api/ReviewEndpoints.cs`
- `src/PhotoIdentity.Api/PersonMaintenanceEndpoints.cs`
- `src/PhotoIdentity.Web/ReviewContracts.cs`
- `src/PhotoIdentity.Web/Pages/People.razor`
- `src/PhotoIdentity.Web/Components/SmartCollectionsWorkspace.razor`
- `src/PhotoIdentity.Web/Components/SmartCollectionsWorkspace.razor.cs`
- `tests/PhotoIdentity.Integration.Tests/PersonSmartCollectionVisibilityApplicationTests.cs`
- `tests/PhotoIdentity.Integration.Tests/SmartCollectionHiddenPersonCompatibilityTests.cs`
- `.github/workflows/build.yml`

## Repository validation

```powershell
./build.ps1
./test.ps1
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
./verify-review.ps1 -Mode Smoke -Configuration Release
```
