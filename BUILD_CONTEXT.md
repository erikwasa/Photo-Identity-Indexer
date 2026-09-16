# Build context

This file is intentionally a short handoff for the next development or verification session. Formal work-item lifecycle status is resolved through `PhotoIdentity.Docs`.

## Current focus

**M26 Creative Collections is now active with WI-0118: timestamp-first photo moment clustering.**

PR #354 implements a pure regenerable moment clusterer over canonical `TakenAtLocal` capture metadata, deterministic revision-ID tie-breaking, conservative unclustered handling for missing timestamps, optional supporting-evidence seams, and a bounded read-only `/api/moments/preview` comparison surface. The initial private evaluation candidates are 30-minute and 90-minute time-gap policies; neither is an accepted default yet.

WI-0131 in M27 remains separately pending its planned maintainer visual/device review; do not treat that deferred review as WI-0118 work.

## Next concrete step

After the normal CI/docs gates pass, compare the 30-minute and 90-minute moment policies against representative private archive samples, including ordinary home/family sequences and available outings. Record only aggregate split/merge observations, select or tune the initial policy, then complete WI-0118 if the resulting boundaries are useful enough for WI-0119 anchor/context generation.

## Relevant files

- docs/delivery/work-items/WI-0118-moment-clustering.md
- docs/delivery/status/work-items/active/WI-0118.yaml
- docs/operations/moment-clustering-evaluation.md
- src/PhotoIdentity.Core/Collections/PhotoMomentClustering.cs
- src/PhotoIdentity.Api/MomentPreviewEndpoints.cs
- tests/PhotoIdentity.Core.Tests/PhotoMomentClustererTests.cs

## Repository validation

    ./build.ps1
    ./test.ps1
    dotnet run --project tools/PhotoIdentity.Docs -- validate
    dotnet run --project tools/PhotoIdentity.Docs -- generate --check
    ./verify-postgres.ps1
