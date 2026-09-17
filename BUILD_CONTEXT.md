# Build context

This file is intentionally a short handoff for the next development or verification session. Formal work-item lifecycle status is resolved through `PhotoIdentity.Docs`.

## Current focus

**M26 Creative Collections is continuing with WI-0119: exact Smart Collection anchors plus bounded same-moment context.**

WI-0118 moment clustering is merged in PR #354 but remains `in_progress` because representative private 30-minute versus 90-minute policy evaluation is intentionally deferred for a few work items. WI-0134 adaptive slideshow timing is merged in PR #355 and likewise retains its later subjective rhythm/device verification.

WI-0119 keeps saved Smart Collection semantics untouched. Exact matches are direct anchors; only WI-0118 moments containing an anchor can add context. The first versioned policy, `m26-anchor-context-balanced-v1`, admits at most six context photos per anchored moment using deterministic capture-time proximity. Context-only photos never recurse into later moments, and the preview records the admitting moment plus direct anchor revision IDs.

## Next concrete step

Validate PR #356 through normal build/Core/integration/docs gates. After a few Creative Collection work items are in place, use `/api/smart-collections/{id}/creative-preview` on representative private family collections and compare the 30-minute and 90-minute moment policies. Keep WI-0119 in progress until at least one sequence demonstrates useful, understandable context beyond strict person filtering.

## Relevant files

- docs/delivery/work-items/WI-0119-anchor-context-generation.md
- docs/delivery/status/work-items/active/WI-0119.yaml
- docs/operations/creative-collection-preview.md
- src/PhotoIdentity.Core/Collections/CreativeCollectionCandidateGeneration.cs
- src/PhotoIdentity.Api/CreativeCollectionPreviewEndpoints.cs
- src/PhotoIdentity.Api/MomentPreviewEndpoints.cs
- tests/PhotoIdentity.Core.Tests/CreativeCollectionCandidateGeneratorTests.cs
- tests/PhotoIdentity.Integration.Tests/CreativeCollectionPreviewApplicationTests.cs

## Repository validation

    ./build.ps1
    ./test.ps1
    dotnet run --project tools/PhotoIdentity.Docs -- validate
    dotnet run --project tools/PhotoIdentity.Docs -- generate --check
    ./verify-postgres.ps1
