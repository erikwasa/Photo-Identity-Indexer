---
id: WI-0114
title: Implement scalable incremental provisional face clustering
milestone: M25
status_source: ../status/work-items.yaml
depends_on: [WI-0113]
related_adrs: []
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Api, PhotoIdentity.Persistence.Postgres, PhotoIdentity.Integration.Tests]
---

# WI-0114: Implement scalable incremental provisional face clustering

## Objective

Implement the clustering semantics selected by WI-0113 as bounded, restart-safe, exact-model-derived background work that can maintain discovery groups as new faces arrive without changing canonical identities.

## In scope

- Add PostgreSQL persistence for provisional cluster runs, policy/provenance and derived memberships as required by the accepted WI-0113 contract.
- Build/rebuild clusters for eligible face embeddings with bounded memory and database work.
- Use measured neighbour-search/indexing infrastructure appropriate to the catalogue scale while keeping the application contract index-agnostic.
- Preserve deterministic full-rebuild semantics for one exact embedding/clustering policy revision.
- Support incremental maintenance for newly analysed/unreviewed faces and intentional retry of previously unclustered/noise faces.
- Permit explicit inclusion of canonical Unknown faces for rediscovery while preserving their canonical Unknown state.
- Exclude rejected false detections and respect applicable durable negative/conflict evidence.
- Make interrupted runs recoverable/restartable and expose concise progress/failure metrics without logging sensitive face data.
- Ensure canonical review changes invalidate or refresh affected derived cluster evidence predictably.

## Out of scope

- Cluster review/assignment UI beyond diagnostic surfaces.
- Automatically creating people or assignments from cluster membership.
- Cluster-assisted known-person scoring.

## Acceptance criteria

- [x] Provisional cluster state is persisted with exact embedding model and clustering-policy provenance.
- [x] A full rebuild is deterministic for unchanged inputs/policy and replaces derived cluster state without rewriting canonical review history.
- [x] Processing is bounded and restart-safe on archive-scale face counts.
- [x] Newly analysed eligible faces can be incorporated without requiring a destructive canonical reset.
- [x] Previously noise/unclustered faces can be retried when new evidence makes a dense group possible.
- [x] Canonical Unknown faces are included only through explicit discovery policy and remain canonically Unknown until a later human action.
- [x] Rejected false detections are excluded from clustering.
- [x] Relevant negative/conflict evidence prevents prohibited memberships according to the WI-0113 contract.
- [x] Run progress/failure metrics expose operational state without personal filenames, crops or embeddings.
- [x] Integration coverage proves rebuild, incremental update, restart, exact-model isolation and canonical-state preservation.

## Implementation handoff

PR #330 implements the selected `m25-dbscan-v1` policy as durable PostgreSQL-derived evidence:

- exact model ID/hash, DBSCAN policy parameters, inclusion policy and captured review/embedding evidence are persisted per run;
- deterministic replacements are published atomically through a current-scope pointer and supersede the previous derived run only after successful completion;
- the exact DBSCAN scan is capped at 20,000 faces and 2,000,000 retained undirected neighbour edges, uses bounded parallel/SIMD work and fails closed instead of weakening the selected policy;
- active runs survive process restart, stale captured evidence is rejected before publish, and review reversals invalidate the review mutation version;
- new exact-model embeddings trigger a replacement run when identity-match regeneration is idle, so newly analysed faces and prior Noise faces are reconsidered together;
- default input is Unreviewed only; canonical Unknown is a separate explicit `includeUnknown` scope, while Assigned and Rejected faces are excluded;
- the current production population therefore cannot contain differently assigned reviewed identities. This satisfies the WI-0113 conflict boundary without inventing face-face constraints from rejected face-person suggestions. Any later expansion to reviewed identities must add explicit conflict-edge enforcement first;
- diagnostic endpoints expose model/run/progress/group-count state without filenames, paths, crops, embeddings or personal labels;
- clustering is available only when PostgreSQL is the selected catalogue provider. SQLite receives HTTP 409 and no clustering worker is created.

Automated coverage includes deterministic/noise-retry/fail-closed Core tests, live PostgreSQL persistence coverage for restart/rebuild/refresh/exact-model/Unknown/Rejected/Assigned/reversal/canonical-history semantics, and an API provider-boundary integration test. The live PostgreSQL test bodies are intentionally gated by `PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING`, so normal GitHub CI proves they compile while `verify-postgres.ps1` is the explicit local live-database acceptance entry point.

Maintainer live verification exposed two PostgreSQL-only runtime defects after PR #330: malformed SQL concatenation in `ReadLatestAsync` and Npgsql reader lifetime ordering in `ListCurrentGroupsAsync`/`TryStartNextRefreshAsync`. Corrective PRs #331 and #332 fixed those issues. The final `verify-postgres.ps1` rerun on merged `main` passed successfully.

## Verification

Maintainer acceptance completed successfully on 2026-09-14.

1. `./verify-postgres.ps1` passed after corrective PRs #331 and #332, exercising the live PostgreSQL WI-0114 persistence/runtime paths.
2. Real-catalogue `includeUnknown=false` clustering started from run `09a848a0-4e62-42ba-8330-44fa2c7ec0be` with 5,183 targets, 60 clusters and 4,792 Noise faces.
3. After a small new analysed photo batch, replacement run `7b6c56d8-1fae-43c5-a776-8d16149b6655` became current with 5,191 targets, 60 clusters and 4,800 Noise faces. The eight newly eligible faces were therefore incorporated without a destructive canonical reset; unchanged cluster count is acceptable because new faces may remain Noise or join existing groups.
4. Canonical review totals were identical before and after clustering: Assigned 10,185; Unknown 4,116; Rejected 643. Clustering therefore did not rewrite canonical Person assignment, Unknown or rejection state.
5. Representative derived groups remained available through `/groups`, including a 107-member group with 88 Core/19 Border and a 60-member group with 33 Core/27 Border, confirming persisted current-run membership summaries are queryable without exposing sensitive face data.

The optional `includeUnknown=true` manual runtime check was not required because the live PostgreSQL integration coverage already exercises explicit Unknown inclusion while preserving canonical Unknown state.

WI-0114 is complete.
