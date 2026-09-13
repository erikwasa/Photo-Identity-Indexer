---
id: M00
title: Repository and architecture
status_source: ../status/milestones.yaml
depends_on: []
---

# M00: Repository and architecture

## Outcome

The repository has an enforceable structure, living documentation, initial ADRs, a buildable .NET solution skeleton and a durable validation/documentation lifecycle that supports ongoing development without carrying temporary CI quarantine as permanent architecture.

## Work items

- [WI-0001](../work-items/WI-0001-living-documentation.md) — establish living documentation.
- [WI-0002](../work-items/WI-0002-solution-skeleton.md) — establish the .NET solution skeleton.
- [WI-0003](../work-items/WI-0003-core-types.md) — establish core domain types and dependency boundaries.
- [WI-0004](../work-items/WI-0004-docs-tooling.md) — add documentation/status tooling.
- [WI-0057](../work-items/WI-0057-work-item-registry-archive.md) — establish explicit work-item lifecycle/status maintenance.
- [WI-0069](../work-items/WI-0069-ci-runtime-optimization.md) — remove redundant local/CI verification work.
- [WI-0070](../work-items/WI-0070-pr-validation-streamlining.md) — streamline pull-request validation while retaining comprehensive coverage.
- [WI-0071](../work-items/WI-0071-stabilize-quarantined-integration-tests.md) — eliminate transient API-host quarantine and restore all tracked tests to required coverage.
- [WI-0109](../work-items/WI-0109-sharded-work-item-documentation.md) — migrate work-item status to bounded canonical shards with automatic terminal archival.

## Closeout — 2026-09-13

M00 is complete. The two remaining non-terminal maintenance records were audited against current `main` and closed without changing production behavior:

- WI-0070's remaining acceptance is present in the current workflow: deterministic integration coverage runs in two isolated required shards; generic API integration hosts inherit worker-disabled isolation; PR review smoke uses the smaller `PublishedMinimum` profile while `main` uses `Comprehensive`; launcher/package verification is path-aware on pull requests and comprehensive on `main`; and the recorded history contains three independent successful PR samples below the six-minute target.
- WI-0071's four original quarantine cases were restored individually after their shared-host stabilization evidence windows. PR #193 / workflow #1196 restored the final case, left `.github/flaky-integration-tests.txt` with no active entries, and proved 302 required integration tests executed with exact once-only coverage and zero quarantined results.
- No unconditional test retry mechanism was introduced and worker-specific integration coverage must still opt in explicitly when production hosted workers are required.

The current CI architecture remains a maintained repository capability, not an active milestone project. Future CI regressions should be tracked as new work rather than reopening M00 unless the repository/architecture milestone contract itself is invalidated.

## Exit criteria

- Documentation and statuses have canonical locations.
- Solution builds and tests run.
- Core has no infrastructure dependencies.
- No personal photos or model binaries are committed.
- Work-item lifecycle/status is bounded, generated views are reproducible and terminal items archive automatically.
- Required PR validation has explicit fast/integration/published-runtime/deployment layers with comprehensive `main` coverage.
- Generic API integration tests are isolated from unrelated production background workers by default.
- No integration tests remain in temporary quarantine.
