# Build context

This file is intentionally a short handoff for the next development or verification session. Formal work-item lifecycle status is resolved through `PhotoIdentity.Docs`.

## Current focus

**Post-M24 housekeeping is reconciling stale delivery status and the production execution strategy before returning to M22.**

The production system is local: PostgreSQL on maintainer-controlled hardware is the sole writable catalogue, Personal OneDrive is accessed through the Windows sync client, and normal model/archive processing runs locally. ADR-0010 supersedes the earlier disposable-Azure strategy.

Housekeeping closes the stale implemented M18 items WI-0046/WI-0048, closes M20/WI-0076 from its measured benchmark evidence, retires M09-M11 Azure planning, closes M12/WI-0023 as superseded by M24 production catch-up, and retires M13/WI-0024 as separate periodic-sync roadmap work.

M23 remains intentionally deferred.

## Next concrete step

After the housekeeping PR is merged, return to **M22 WI-0107**. It owns the two remaining slideshow acceptance gaps: direct fullscreen acquisition from the initiating Start slideshow gesture and persistence/revalidation of successful standalone prepared-original state across slideshow navigation.

After WI-0107 is implemented, run the focused real-phone M22 re-verification and close the consolidated M22 items if those two scenarios pass.

## Relevant files

- docs/decisions/ADR-0010-local-production-execution.md
- docs/delivery/local-first-plan.md
- docs/delivery/status/milestones.yaml
- docs/delivery/status/work-items-index.md
- docs/delivery/work-items/WI-0107-m22-slideshow-acceptance-gaps.md
- docs/delivery/milestones/M22-protected-smart-collection-slideshow.md

## Repository validation

    ./build.ps1
    ./test.ps1
    dotnet run --project tools/PhotoIdentity.Docs -- validate
    dotnet run --project tools/PhotoIdentity.Docs -- generate --check
    ./verify-review.ps1 -Mode Smoke -Configuration Release

Production PostgreSQL verification when runtime changes require it:

    ./verify-postgres.ps1
