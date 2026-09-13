# Build context

This file is intentionally a short handoff for the next development or verification session. Formal work-item lifecycle status is resolved through `PhotoIdentity.Docs`.

## Current focus

**M25 Face discovery and cluster-assisted identity review is active.**

WI-0110 has passed its first real-catalogue discovery verification and is in a narrow corrective slice. On 2026-09-13 the maintainer searched from a representative face and received 100 results from 9,847 eligible faces in 151 ms; the first 20 results were correct. Exact-model search, Unknown rediscovery, subset review and the remaining requested desktop/mobile checks behaved as expected.

Two findings remain before WI-0110 can complete: the Similar Faces bulk bar must stay fixed in the viewport like ordinary Review, and Assign must include the unreviewed source face in the same audited preview/commit transaction. Unknown/reject actions must remain explicit-selection-only, and an already-reviewed source must never be silently rewritten.

The production execution strategy remains local under ADR-0010, PostgreSQL remains the sole writable production catalogue, M23 remains intentionally deferred, and WI-0081 remains the separate quality investigation that gates any later expansion of automatic identity assignment.

## Next concrete step

Finish and verify the WI-0110 corrective PR. Recheck that the Similar Faces bulk controls remain visible while scrolling and that assigning selected matches also assigns an unreviewed source face while preserving stale-state revalidation. The measured 151 ms exact scan over 9,847 eligible faces is practical, so no pgvector/ANN change is warranted in this work item.

After that verification passes, complete WI-0110 and proceed to WI-0111 event-driven bounded follow-up regeneration.

## Relevant files

- docs/delivery/milestones/M25-face-discovery-and-cluster-assisted-review.md
- docs/delivery/work-items/WI-0110-similar-face-explorer.md
- docs/delivery/status/work-items/active/WI-0110.yaml
- src/PhotoIdentity.Core/Review/ISimilarFaceRepository.cs
- src/PhotoIdentity.Persistence.Postgres/PostgresSimilarFaceRepository.cs
- src/PhotoIdentity.Api/SimilarFaceEndpoints.cs
- src/PhotoIdentity.Web/SimilarFaceBulkSelection.cs
- src/PhotoIdentity.Web/Pages/SimilarFaces.razor
- src/PhotoIdentity.Web/Pages/SimilarFaces.razor.css
- tests/PhotoIdentity.Persistence.Tests/PostgresSimilarFaceRepositoryTests.cs
- tests/PhotoIdentity.Integration.Tests/SimilarFaceApplicationTests.cs
- tests/PhotoIdentity.Integration.Tests/SimilarFaceBulkSelectionTests.cs

## Repository validation

    ./build.ps1
    ./test.ps1
    dotnet run --project tools/PhotoIdentity.Docs -- validate
    dotnet run --project tools/PhotoIdentity.Docs -- generate --check
    ./verify-review.ps1 -Mode Smoke -Configuration Release
