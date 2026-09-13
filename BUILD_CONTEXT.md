# Build context

This file is intentionally a short handoff for the next development or verification session. Formal work-item lifecycle status is resolved through `PhotoIdentity.Docs`.

## Current focus

**M00 Repository and architecture and M22 Protected Smart Collection slideshow are completed.**

The 2026-09-13 M00 closeout reconciles two stale maintenance records with repository state:

- WI-0070 is complete. The current PR gate has timing evidence, two isolated required integration shards, shared generic API-host isolation, `PublishedMinimum` PR review smoke, comprehensive `main` review verification, path-aware launcher/package PR checks and comprehensive deployment verification on `main`.
- WI-0071 is complete. PR #193 / workflow #1196 restored the final quarantined API integration test; `.github/flaky-integration-tests.txt` now has no active entries, and generic endpoint tests inherit the worker-disabled compatibility host by default without retries.

M22 remains completed and accepted after PRs #314-#316. M23 remains intentionally deferred. The production execution strategy remains local under ADR-0010.

## Next concrete step

After the M00 closeout PR merges, the only non-terminal work is M21 WI-0081 and the intentionally deferred M23 source-copy lifecycle/privacy work. The recommended next substantive task is WI-0081: measure the reported identity-suggestion accuracy degradation before changing ranking, thresholds, reference selection or models.

## Relevant files

- docs/delivery/milestones/M00-repository.md
- docs/delivery/work-items/WI-0070-pr-validation-streamlining.md
- docs/delivery/work-items/WI-0071-stabilize-quarantined-integration-tests.md
- docs/delivery/status/work-items/archive/WI-0070.yaml
- docs/delivery/status/work-items/archive/WI-0071.yaml
- docs/delivery/status/milestones.yaml
- docs/delivery/status/work-items-index.md
- .github/workflows/build.yml
- .github/flaky-integration-tests.txt
- tests/PhotoIdentity.Integration.Tests/PhotoIdentityApiTestFactory.cs
- docs/delivery/work-items/WI-0081-suggestion-accuracy-degradation.md

## Repository validation

    ./build.ps1
    ./test.ps1
    dotnet run --project tools/PhotoIdentity.Docs -- validate
    dotnet run --project tools/PhotoIdentity.Docs -- generate --check
    ./verify-review.ps1 -Mode Smoke -Configuration Release
