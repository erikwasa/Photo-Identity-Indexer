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

## Verification requirements

Automated PostgreSQL integration/scale coverage plus human operator verification that a newly analysed batch updates provisional discovery groups without changing canonical assignments.
