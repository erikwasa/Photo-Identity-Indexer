# Build context

This file is intentionally a short handoff for the next development or verification session. Formal work-item lifecycle status is resolved through `PhotoIdentity.Docs`.

## Current focus

**M25 Face discovery and cluster-assisted identity review is active.**

WI-0110 similar-face discovery is accepted and complete after PRs #319 and #320 plus maintainer Windows/mobile verification. The bounded exact PostgreSQL scan remains the accepted implementation at the measured archive scale.

The production execution strategy remains local under ADR-0010, PostgreSQL remains the sole writable production catalogue, M23 remains intentionally deferred, and WI-0081 remains the separate quality investigation that gates any later expansion of automatic identity assignment.

## Next concrete step

Start WI-0111: coalesce qualifying identity-evidence changes into a later durable exact-model match-regeneration run. Preserve the existing fixed evidence/model snapshot, stale-run behavior and no-same-run-cascade invariant; do not trigger one full regeneration per review click.

The implementation should build on the existing regeneration repository/hosted-service state machine, add durable coalescing/restart semantics and an operator-visible queued/running/current state, then verify a manual assignment produces a later completed regeneration without an explicit regenerate click.

## Relevant files

- docs/delivery/milestones/M25-face-discovery-and-cluster-assisted-review.md
- docs/delivery/work-items/WI-0111-event-driven-match-regeneration.md
- docs/delivery/status/work-items/active/WI-0111.yaml
- docs/decisions/ADR-0006-automatic-identity-assignment.md
- src/PhotoIdentity.Core/Review/IIdentityMatchRegenerationRepository.cs
- src/PhotoIdentity.Core/Review/IIdentityMatchRegenerationExecution.cs
- src/PhotoIdentity.Persistence.Postgres/PostgresIdentityMatchRegenerationRepository.cs
- src/PhotoIdentity.Persistence.Postgres/PostgresCatalogueDatabase.IdentityRegenerationSchema.cs
- src/PhotoIdentity.Api/IdentityMatchRegenerationEndpoints.cs
- src/PhotoIdentity.Api/IdentityMatchRegenerationHostedService.cs

## Repository validation

    ./build.ps1
    ./test.ps1
    dotnet run --project tools/PhotoIdentity.Docs -- validate
    dotnet run --project tools/PhotoIdentity.Docs -- generate --check
    ./verify-review.ps1 -Mode Smoke -Configuration Release
