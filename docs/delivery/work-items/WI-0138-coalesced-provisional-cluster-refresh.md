---
id: WI-0138
title: Coalesce provisional clustering refreshes during active face review
milestone: M25
status_source: ../status/work-items.yaml
depends_on: [WI-0114, WI-0115]
related_adrs: []
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Api, PhotoIdentity.Persistence.Postgres, PhotoIdentity.Persistence.Tests, docs]
---

# WI-0138: Coalesce provisional clustering refreshes during active face review

## Objective

Prevent canonical face-review activity from repeatedly triggering full provisional-cluster replacement runs while the operator is actively reviewing faces.

## Why

Real-catalogue review on 2026-09-18 showed progressive interaction latency while Suggested groups was being processed. The captured application log showed ordinary suggestion-list reads remaining roughly stable while full provisional clustering was repeatedly published for both the default and Unknown-inclusive scopes. Individual suggestion accepts were normally fast but occasionally spiked into the hundreds of milliseconds while those large replacement runs were being published.

WI-0114 intentionally makes canonical review changes invalidate provisional clustering, but the automatic scheduler currently interprets every changed review-evidence version as permission to rebuild immediately whenever identity regeneration is idle. During sustained review this can turn a sequence of small human decisions into repeated multi-thousand-face DBSCAN rebuilds and membership writes.

## In scope

- Coalesce automatic provisional-cluster refreshes caused by canonical review mutations behind a short review quiet period.
- Derive the quiet-period boundary from durable review evidence so process restart does not reset the debounce.
- Preserve immediate refresh behavior for embedding-only evidence changes when review evidence itself has not changed recently.
- Preserve explicit operator-started clustering and explicit not-same replacement semantics.
- Keep clustering lower priority than identity-match regeneration and retain existing stale/fixed-snapshot safety checks.
- Add live PostgreSQL regression coverage for review churn and restart-safe coalescing.

## Out of scope

- Incrementally mutating long-lived cluster memberships instead of deterministic replacement runs.
- Changing DBSCAN policy, thresholds, cluster semantics or review-state participation.
- Changing identity-match regeneration debounce behavior.
- Suppressing manual clustering requests.
- Browser-side review virtualization or archive-status query optimization.

## Acceptance criteria

- [x] Automatic provisional-cluster refresh caused by canonical review evidence waits for a 30-second period with no newer review mutation before a replacement run is started.
- [x] The quiet-period calculation is based on the durable review-mutation evidence version and therefore survives repository/process restart.
- [x] Multiple review decisions inside the quiet period coalesce into one later replacement opportunity per current cluster scope rather than one replacement per decision.
- [x] An embedding-only stale cluster remains eligible for automatic refresh without waiting on the review debounce when its captured review evidence still matches.
- [x] Explicit clustering starts and existing not-same replacement behavior remain outside the automatic review debounce.
- [x] PostgreSQL persistence coverage verifies no refresh before the quiet boundary and refresh after the boundary, including across a fresh repository instance.
- [ ] Maintainer sustained-review verification confirms that ordinary face review no longer produces a stream of full provisional-clustering publications or corresponding interaction stalls.

## Verification requirements

1. Run repository CI and the live PostgreSQL suite through `verify-postgres.ps1`.
2. Start the packaged app against the representative catalogue and review Suggested groups continuously for at least several dozen face decisions.
3. Confirm the log does not publish a replacement provisional-clustering run after each face decision.
4. Stop reviewing for at least 30 seconds and confirm stale provisional clustering is eventually refreshed.
5. Confirm People to identify remains usable after the deferred refresh and explicit not-same feedback still queues replacement clustering.
6. Compare face-review request latency before/after; occasional unrelated variance is acceptable, but repeated 300-700 ms accept spikes correlated with every review mutation should be absent.

## Implementation notes

- The existing `ReviewMutationVersion` is already a microsecond Unix-epoch timestamp of the newest review create/reversal mutation. WI-0138 reuses that durable value rather than introducing another timer table.
- `ProvisionalFaceClusteringWorker` supplies a 30-second quiet period only for automatic refresh discovery.
- `PostgresProvisionalFaceClusterRepository.TryStartNextRefreshAsync` compares the current evidence to the published run. When review evidence changed too recently, it leaves the current run stale and returns without starting work. The hosted service naturally retries later.
- The debounce does not weaken evidence consistency: any active run still fails closed as stale if evidence changes before publication.
