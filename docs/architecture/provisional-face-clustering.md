# Provisional face clustering

Status: WI-0113 evaluated contract. Production persistence/execution is deferred to WI-0114.

## Boundary

A provisional face cluster is **derived exact-model evidence**, not a Person and not an identity assignment. Cluster IDs are disposable implementation identifiers. Rebuilding clustering must never create, merge, rename, delete, or rewrite canonical people, review actions, Unknown decisions, suggestion decisions, or audit history.

The contract is keyed by:

- embedding model ID,
- embedding model SHA-256,
- clustering policy/version,
- algorithm identifier and parameters,
- generation time.

Results from different exact embedding revisions or clustering-policy versions must not be silently mixed.

## Member semantics

Every evaluated face has one of three algorithm-level roles:

- **Core**: the face satisfies the selected policy's local-support rule and participates in forming the cluster.
- **Border**: the face does not independently satisfy the core rule but is attached to a supported cluster by the selected algorithm.
- **Noise**: the face is not admitted to a cluster under the selected policy.

`Noise` is a derived clustering outcome only. It is not the canonical `Unknown` review state. Likewise, a canonical Unknown face may be Core, Border, or Noise in a provisional clustering run without changing its canonical review decision.

## Canonical review-state participation

- **Assigned** faces may be included in the private evaluation sample as ground truth. Their canonical Person IDs are used only to measure merge/split quality and are pseudonymized before an evaluation file is written.
- **Unreviewed** faces are the primary production discovery population for WI-0114, but they are not labelled ground truth in WI-0113's reviewed-sample metrics.
- **Unknown** faces may participate in discovery as unlabeled points. They contribute to coverage/noise metrics but not to labelled false-merge or false-split denominators. A cluster must never silently turn Unknown into Assigned.
- **Rejected** faces are false detections and are excluded from discovery/evaluation clustering input.

Canonical review state always wins over derived cluster state.

## Conflict evidence

False merges are the primary clustering risk. The initial production design must be able to reject or split candidate edges when conflict evidence exists.

Strong conflict evidence includes:

- two reviewed faces assigned to different canonical people,
- an explicit durable not-same face/identity constraint if a future work item introduces one,
- same-photo co-occurrence between two different reviewed identities where the detections are reliable.

Existing rejected identity suggestions remain durable negative face-person evidence but are not automatically generalized into arbitrary face-face not-same edges.

The WI-0113 evaluator reports same-photo conflicting merges separately so a parameter set cannot hide an obvious merge error inside aggregate coverage.

## Deterministic rebuild contract

WI-0114 production persistence should use a replaceable run boundary rather than mutate long-lived cluster identities:

1. Capture one exact model revision and one immutable clustering policy/version.
2. Capture the eligible face/embedding evidence snapshot.
3. Compute clusters deterministically for that snapshot and policy. Tie-breaking must use stable face-occurrence IDs.
4. Write a new derived run plus cluster/member rows.
5. Atomically make that run current for its exact model/policy scope.
6. Remove or supersede the previous derived run for the same scope only after the replacement is complete.

A rebuild never updates canonical review tables. Cluster keys may change after any rebuild; downstream UI must treat them as run-scoped derived identifiers.

A future persisted run should record at least: exact model ID/hash, policy version, algorithm/parameters, evidence snapshot/version, generated time, cluster count, noise count, and member role/strength where the selected algorithm provides it.

## WI-0113 evaluation method

The private evaluator uses one reviewed exact-model sample and one shared cosine-distance matrix for all candidates. It compares:

- DBSCAN,
- HDBSCAN,
- a conservative mutual-neighbour graph with distance/shared-neighbour constraints.

For every candidate it reports false-merge pairs/rate separately from false splits, labelled/overall coverage, noise, singleton/cluster-size distribution, and same-photo conflicts. Candidate ordering treats false merges as the first risk criterion; coverage is considered only after merge risk.

The exporter intentionally writes no source paths, filenames, crop paths, person names, or raw catalogue IDs. Person/face/photo identifiers are replaced with local synthetic labels. The resulting JSON still contains biometric embeddings and therefore belongs only under ignored/private storage.

See `tools/cluster-evaluation/README.md` for the reproducible local procedure.

## Selected initial production policy

Maintainer evaluation on 2026-09-13 used a private sample of 5,000 reviewed faces with 107 assigned identity labels and 1,294 canonical Unknown faces. The selected conservative policy is **DBSCAN with `eps=0.30` and `min_samples=3`**.

Measured quality for that candidate:

- false-merge pairs: **0 / 443,525**,
- false-merge rate: **0.000000**,
- false-split rate: **0.687251**,
- labelled coverage: **0.542**,
- noise rate: **0.579**,
- same-photo conflicting merges: **0**.

The policy intentionally accepts substantial split/noise cost to preserve the primary safety goal of avoiding false merges. Noise remains derived and non-canonical; canonical Unknown remains unchanged and may participate only as unlabeled discovery evidence.

Age, pose and image-quality variation were not established from the available sample metadata. This limitation must remain visible and should be revisited when later cluster-review work has suitable labelled examples; it is not inferred from embeddings.

## Neighbour-search decision for WI-0114

The private evaluator measured 0.528 seconds for the 5,000-face pairwise cosine-distance calculation and projects approximately **2.111 seconds at 10,000 faces** under its quadratic diagnostic model.

That benchmark is not a direct PostgreSQL query benchmark, so it must not be treated as a latency guarantee. It nevertheless provides no evidence that ANN complexity is required at the expected initial archive scale. WI-0114 should therefore start with **bounded exact PostgreSQL/vector-neighbour retrieval**, preserve deterministic exact-model semantics, and keep ANN optional. Introduce an ANN index/dependency only if measured production incremental retrieval fails the required runtime budget.
