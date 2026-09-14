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

- [ ] Provisional cluster state is persisted with exact embedding model and clustering-policy provenance.
- [ ] A full rebuild is deterministic for unchanged inputs/policy and replaces derived cluster state without rewriting canonical review history.
- [ ] Processing is bounded and restart-safe on archive-scale face counts.
- [ ] Newly analysed eligible faces can be incorporated without requiring a destructive canonical reset.
- [ ] Previously noise/unclustered faces can be retried when new evidence makes a dense group possible.
- [ ] Canonical Unknown faces are included only through explicit discovery policy and remain canonically Unknown until a later human action.
- [ ] Rejected false detections are excluded from clustering.
- [ ] Relevant negative/conflict evidence prevents prohibited memberships according to the WI-0113 contract.
- [ ] Run progress/failure metrics expose operational state without personal filenames, crops or embeddings.
- [ ] Integration coverage proves rebuild, incremental update, restart, exact-model isolation and canonical-state preservation.

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

## Verification requirements

Before WI-0114 is completed, the maintainer should verify both layers:

1. Run `./verify-postgres.ps1` from the PR branch and confirm the live PostgreSQL suite passes. This exercises the database-backed WI-0114 tests rather than only compiling their skipped CI bodies.
2. On the real PostgreSQL catalogue, start/observe a provisional cluster run for the current exact embedding model with `includeUnknown=false`; record the run ID, target count, cluster count and noise count. Then add/analyse a small new photo batch and confirm a replacement run becomes current, the target/discovery groups update as expected, and no canonical Person assignment, Unknown decision or rejection history changes solely because clustering ran.
3. Optionally start the explicit `includeUnknown=true` scope and confirm its target population increases when canonical Unknown faces exist while those faces remain canonically Unknown.

The operator API is `/api/review/provisional-clusters`. The status response exposes `runId`, `status`, `isActive`, `isCurrent`, `targetCount`, `processedTargetCount`, `clusterCount`, `noiseCount`, timestamps and a non-sensitive error field. `/groups` exposes only derived cluster keys and aggregate member/Core/Border counts.

Human runtime verification remains required before checking the acceptance boxes and marking the work item completed.
