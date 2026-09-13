# Build context

This file is intentionally a short handoff for the next development or verification session. Formal work-item lifecycle status is resolved through `PhotoIdentity.Docs`.

## Current focus

**M25 Face discovery and cluster-assisted identity review is active.**

WI-0110 similar-face discovery is accepted and complete after PRs #319 and #320 plus maintainer Windows/mobile verification. WI-0111 is now in progress: qualifying identity-evidence changes should produce one later coalesced exact-model regeneration without weakening the existing fixed-snapshot/stale-run boundary.

The WI-0111 implementation reuses the durable regeneration controller. A newer evidence version than the latest run's expected post-auto evidence is the durable queued condition; a short configurable debounce coalesces bursts before the existing `StartAsync` path snapshots the next run. Completed automatic assignments are included in the expected evidence version so they do not recursively schedule themselves.

The production execution strategy remains local under ADR-0010, PostgreSQL remains the sole writable production catalogue, M23 remains intentionally deferred, and WI-0081 remains the separate quality investigation that gates any later expansion of automatic identity assignment.

## Next concrete step

Finish PR #322 CI and automated coverage, then perform the WI-0111 human Windows pass: make a manual assignment, confirm the regeneration page reports automatic follow-up as queued without pressing Regenerate, wait for the bounded run to start/complete, and confirm it returns to current. Also verify `PhotoIdentity__IdentityMatchRegeneration__AutomaticFollowUpEnabled=false` leaves explicit regeneration available while automatic follow-up reports disabled.

## Relevant files

- docs/delivery/milestones/M25-face-discovery-and-cluster-assisted-review.md
- docs/delivery/work-items/WI-0111-event-driven-match-regeneration.md
- docs/delivery/status/work-items/active/WI-0111.yaml
- docs/decisions/ADR-0006-canonical-auto-assignment.md
- docs/architecture/identity-matching.md
- src/PhotoIdentity.Core/Review/IIdentityMatchRegenerationRepository.cs
- src/PhotoIdentity.Core/Review/IIdentityMatchEvidenceVersionReader.cs
- src/PhotoIdentity.Persistence.Postgres/PostgresIdentityMatchRegenerationRepository.cs
- src/PhotoIdentity.Api/IdentityMatchFollowUpPlanner.cs
- src/PhotoIdentity.Api/IdentityMatchRegenerationEndpoints.cs
- src/PhotoIdentity.Api/IdentityMatchRegenerationHostedService.cs
- src/PhotoIdentity.Web/Pages/MatchRegeneration.razor
- tests/PhotoIdentity.Integration.Tests/IdentityMatchFollowUpPlannerTests.cs

## Repository validation

    ./build.ps1
    ./test.ps1
    dotnet run --project tools/PhotoIdentity.Docs -- validate
    dotnet run --project tools/PhotoIdentity.Docs -- generate --check
    ./verify-review.ps1 -Mode Smoke -Configuration Release
