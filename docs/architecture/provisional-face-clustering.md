# Provisional face clustering

Status: WI-0114 production implementation candidate. Maintainer acceptance remains pending.

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

- **Assigned** faces may be included in the private evaluation sample as ground truth. Their canonical Person IDs are used only to measure merge/split quality and are pseudonymized before an evaluation file is written. They are excluded from the initial WI-0114 production discovery population.
- **Unreviewed** faces are the primary WI-0114 production discovery population.
- **Unknown** faces may participate only when the run explicitly enables `includeUnknown`. They remain canonically Unknown and contribute only as derived discovery evidence until a later human review action.
- **Rejected** faces are false detections and are excluded from production clustering input.

Canonical review state always wins over derived cluster state.

## Conflict evidence

False merges are the primary clustering risk. The production boundary must fail closed rather than silently weaken the selected policy when relevant conflict evidence exists.

Strong conflict evidence identified by WI-0113 includes:

- two reviewed faces assigned to different canonical people,
- an explicit durable not-same face/identity constraint if a future work item introduces one,
- same-photo co-occurrence between two different reviewed identities where the detections are reliable.

The initial WI-0114 production population contains only Unreviewed faces plus explicitly included canonical Unknown faces. Assigned and Rejected faces are filtered before clustering, so reviewed-person conflicts and same-photo conflicts between differently assigned reviewed identities cannot become provisional memberships in this implementation. Existing rejected identity suggestions remain durable negative face-person evidence but are not generalized into arbitrary face-face not-same edges, matching the WI-0113 contract. If a later work item expands clustering input to reviewed identities or introduces explicit face-face constraints, that work must add conflict-edge enforcement before those faces can participate.

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

## Neighbour-search decision

The private WI-0113 evaluator measured 0.528 seconds for the 5,000-face pairwise cosine-distance calculation and projected approximately **2.111 seconds at 10,000 faces** under its quadratic diagnostic model.

That benchmark is not a direct PostgreSQL query benchmark, so it is not treated as a latency guarantee. It nevertheless provided no evidence that ANN complexity was required at the expected initial archive scale. WI-0114 therefore uses a bounded exact approach: PostgreSQL supplies one exact-model snapshot and the Core clusterer performs deterministic exact cosine comparisons in process. ANN remains optional and should be introduced only if measured production retrieval/computation exceeds the required runtime budget.

The initial implementation has two explicit safety bounds:

- at most **20,000 faces** in one run,
- at most **2,000,000 undirected neighbour edges** retained by the exact DBSCAN computation.

The pairwise scan uses normalized SIMD dot products and bounded parallel row chunks. It does not retain a dense distance matrix. Exceeding either bound fails the run instead of silently changing `eps`, `min_samples`, or cluster semantics.

## Durable production run model

WI-0114 persists three derived structures in PostgreSQL:

- a run row containing exact model ID/hash, policy parameters, inclusion policy, captured evidence version, operational status/counts and timestamps,
- membership rows containing only run-scoped face membership, derived cluster key and Core/Border/Noise role,
- a current-scope pointer identifying the published replacement for one exact model/policy/`includeUnknown` scope.

A deterministic rebuild follows this sequence:

1. Capture one exact model revision, immutable clustering policy and current review/embedding evidence version.
2. Count the eligible population and reject a run above the face bound.
3. Persist a durable Pending run before computation starts.
4. Re-read the same bounded snapshot after restart if necessary and mark the run Stale if its captured evidence no longer matches.
5. Compute DBSCAN deterministically after sorting by stable face-occurrence ID.
6. Re-check the evidence version before publishing.
7. Write all derived memberships and atomically move the current-scope pointer to the replacement run.
8. Supersede the previously current run only after the replacement is complete.

Cluster keys are disposable and run-scoped. No production path updates canonical people, person labels, review actions, Unknown state, rejection history or suggestion decisions.

## Incremental refresh semantics

"Incremental" refers to maintaining current discovery evidence as the catalogue changes; it does not mean mutating long-lived cluster identities. New exact-model embeddings or canonical review changes make the current run stale. When identity-match regeneration is idle, the background scheduler detects the changed evidence version and starts a bounded replacement run for the same scope.

Because every replacement rebuilds the eligible snapshot, newly analysed faces are incorporated and previous Noise faces are retried automatically. A face that was Noise with too few neighbours can therefore become Core/Border when later evidence creates a dense enough group, without resetting any canonical identity state.

Review reversals also change the captured review mutation version, so reversing an earlier decision invalidates affected derived evidence predictably even when no new review-action row is inserted.

## Provider boundary and scheduling

Provisional clustering is PostgreSQL-only. The API resolves the repository only when `PhotoIdentity:CatalogueProvider` selects PostgreSQL; a SQLite-selected host returns HTTP 409 for provisional-clustering endpoints and does not run the clustering worker. This keeps PostgreSQL as the sole production authority and prevents cross-provider writes.

The clustering worker is advanced by the existing identity-regeneration hosted service only when no identity-regeneration run is active. This keeps clustering lower priority than review matching and avoids a second competing background loop. An interrupted active clustering run remains durable and is resumed from its captured snapshot after process restart.

## Operator diagnostics

The API exposes `/api/review/provisional-clusters` diagnostic/control endpoints for:

- exact model revisions,
- latest run status and progress,
- explicitly starting a run with or without Unknown participation,
- current group summaries containing only run ID, derived cluster key and aggregate member/Core/Border counts.

Operational state includes target/progress/cluster/noise counts, timestamps and a bounded failure message. The worker does not log face IDs, filenames, source paths, crop data, embeddings or personal labels.

## Verification

Automated coverage is split intentionally:

- Core tests prove deterministic selected-policy behavior, retry of previous Noise faces after denser evidence arrives, and fail-closed neighbour-budget behavior.
- PostgreSQL persistence tests exercise durable restart, atomic replacement, new-embedding refresh, exact-model isolation, explicit Unknown inclusion, Rejected/Assigned exclusion, review-reversal invalidation and canonical review-history preservation when a live test PostgreSQL connection is supplied.
- Integration tests assert the catalogue-provider boundary; the PostgreSQL endpoint path is exercised when the live PostgreSQL test connection is available.

`verify-postgres.ps1` is the repository's explicit local entry point for the live PostgreSQL test suite because it supplies `PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING` only to the child verification process. GitHub CI without that environment variable still builds the live tests but intentionally skips their database bodies.

Final WI-0114 acceptance also requires a maintainer runtime check on the real catalogue: complete a provisional run, add/analyse a small new photo batch, observe a replacement run and updated discovery groups, and confirm canonical assignments/Unknown/rejection history did not change as a side effect.

## WI-0113 evaluation method

The private evaluator used one reviewed exact-model sample and one shared cosine-distance matrix for all candidates. It compared:

- DBSCAN,
- HDBSCAN,
- a conservative mutual-neighbour graph with distance/shared-neighbour constraints.

For every candidate it reported false-merge pairs/rate separately from false splits, labelled/overall coverage, noise, singleton/cluster-size distribution, and same-photo conflicts. Candidate ordering treated false merges as the first risk criterion; coverage was considered only after merge risk.

The exporter intentionally wrote no source paths, filenames, crop paths, person names, or raw catalogue IDs. Person/face/photo identifiers were replaced with local synthetic labels. The resulting JSON still contains biometric embeddings and therefore belongs only under ignored/private storage.

See `tools/cluster-evaluation/README.md` for the reproducible local evaluation procedure.
