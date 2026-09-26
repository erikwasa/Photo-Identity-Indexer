# Provisional face clustering

Status: WI-0114 production clustering accepted; WI-0115 cluster-assisted review integration in progress.

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
- an explicit durable not-same face/identity constraint,
- same-photo co-occurrence between two different reviewed identities where the detections are reliable.

The initial WI-0114 production population contains only Unreviewed faces plus explicitly included canonical Unknown faces. Assigned and Rejected faces are filtered before clustering, so reviewed-person conflicts and same-photo conflicts between differently assigned reviewed identities cannot become provisional memberships in this implementation. Existing rejected identity suggestions remain durable negative face-person evidence but are not generalized into arbitrary face-face not-same edges, matching the WI-0113 contract.

WI-0115 introduces explicit face-to-face `not same` discovery evidence. The evidence is canonicalized as an unordered pair of face occurrence IDs and is durable independently of any run-scoped cluster key. Before recording feedback, the review repository verifies that the anchor and selected exception faces still belong to the same current provisional cluster. Recording the constraint does not reject either face, mark either face Unknown, create a Person, or rewrite review history.

A replacement clustering run applies all relevant durable not-same pairs after the selected DBSCAN density result. Each conflicting derived component is deterministically partitioned in stable face-ID order so that no surviving partition contains an explicit conflict pair. A resulting partition smaller than the selected minimum cluster size becomes derived Noise. The implementation does not increase `eps`, lower `min_samples`, or otherwise weaken the accepted policy to preserve a group after negative feedback.

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

The initial implementation has explicit safety bounds:

- at most **20,000 faces** in one run,
- at most **2,000,000 undirected neighbour edges** retained by the exact DBSCAN computation,
- at most **100,000 relevant durable not-same constraints** loaded for one clustering run.

The pairwise scan uses normalized SIMD dot products and bounded parallel row chunks. It does not retain a dense distance matrix. Exceeding any configured bound fails the run instead of silently changing `eps`, `min_samples`, cluster semantics, or negative-evidence handling.

## Durable production run model

WI-0114 persists three derived structures in PostgreSQL:

- a run row containing exact model ID/hash, policy parameters, inclusion policy, captured evidence version, operational status/counts and timestamps,
- membership rows containing only run-scoped face membership, derived cluster key and Core/Border/Noise role,
- a current-scope pointer identifying the published replacement for one exact model/policy/`includeUnknown` scope.

WI-0115 additionally persists face-to-face not-same constraints as durable discovery evidence. Source run/key metadata is retained only for audit context; the pair itself is independent of disposable cluster IDs and remains applicable to later replacement runs while both faces participate.

A deterministic rebuild follows this sequence:

1. Capture one exact model revision, immutable clustering policy and current review/embedding evidence version.
2. Count the eligible population and reject a run above the face bound.
3. Persist a durable Pending run before computation starts.
4. Re-read the same bounded snapshot after restart if necessary and mark the run Stale if its captured canonical/embedding evidence no longer matches.
5. Read bounded not-same evidence relevant to the eligible face snapshot.
6. Compute DBSCAN deterministically after sorting by stable face-occurrence ID, then apply deterministic conflict partitioning.
7. Re-check the canonical/embedding evidence version before publishing.
8. Write all derived memberships and atomically move the current-scope pointer to the replacement run.
9. Supersede the previously current run only after the replacement is complete.

Cluster keys are disposable and run-scoped. No production path updates canonical people, person labels, review actions, Unknown state, rejection history or suggestion decisions merely because clustering ran or a not-same pair was recorded.

## Incremental refresh semantics

"Incremental" refers to maintaining current discovery evidence as the catalogue changes; it does not mean mutating long-lived cluster identities. New exact-model embeddings or canonical review changes make the current run stale. When identity-match regeneration is idle, the background scheduler detects the changed evidence version and starts a bounded replacement run for the same scope.

Automatic review-driven refresh is coalesced behind a 30-second review quiet period. The scheduler derives the latest review-mutation time from the durable `ReviewMutationVersion` captured in cluster evidence, so repeated assignment/Unknown/rejection/reversal actions keep extending the same quiet boundary even across process restart. The current provisional run remains readable while stale; after review activity stops, one replacement opportunity is started per stale scope. An embedding-only evidence change is not delayed when the captured review evidence still matches. Explicit operator clustering starts and the explicit not-same replacement path are not subject to this automatic review debounce.

Because every replacement rebuilds the eligible snapshot, newly analysed faces are incorporated and previous Noise faces are retried automatically. A face that was Noise with too few neighbours can therefore become Core/Border when later evidence creates a dense enough group, without resetting any canonical identity state.

Review reversals also change the captured review mutation version, so reversing an earlier decision invalidates affected derived evidence predictably even when no new review-action row is inserted.

WI-0115 not-same feedback explicitly queues a replacement run for the same exact model/policy scope after the durable constraint is recorded. If an equivalent run is active, it is stopped conservatively and replaced so a run that began before the constraint cannot publish a grouping that ignores the new conflict evidence.

## Cluster review workspace

The `People to identify` workspace is a review projection over the current provisional run, not a second identity system. It exposes bounded, size-prioritized cluster cards with representative faces, member/Core/Border counts, Core-share evidence and explicit provisional/derived labelling. Opening a card loads a bounded member subset and links every member back to existing face/photo context.

Canonical assignment from this workspace reuses the existing audited bulk-review preview/commit API. The operator chooses the member subset explicitly and may assign it to an existing Person or create a Person first; unselected and borderline members remain unreviewed. The workspace never interprets a cluster as permission to assign every member automatically.

`Not same as anchor` records discovery evidence only. It is deliberately separate from false-detection rejection, canonical Unknown and identity-suggestion rejection. This preserves the distinction between “these are valid faces that should not be grouped together” and canonical review decisions about what each face represents.

## Provider boundary and scheduling

Provisional clustering and cluster review use the unconditional PostgreSQL runtime catalogue. The temporary SQLite integration-test compatibility host returns HTTP 409 and does not run the clustering worker; it is not an operator-selectable runtime mode.

The clustering worker is advanced by the existing identity-regeneration hosted service only when no identity-regeneration run is active. This keeps clustering lower priority than review matching and avoids a second competing background loop. An interrupted active clustering run remains durable and is resumed from its captured snapshot after process restart.

## Operator diagnostics

The API exposes `/api/review/provisional-clusters` diagnostic/control and review endpoints for:

- exact model revisions,
- latest run status and progress,
- explicitly starting a run with or without Unknown participation,
- current aggregate group summaries,
- paged current review-group cards with representative face image URLs,
- bounded member loading for one current group,
- durable not-same feedback that queues replacement clustering.

Operational state includes target/progress/cluster/noise counts, timestamps and a bounded failure message. The worker does not log face IDs, filenames, source paths, crop data, embeddings or personal labels.

## Verification

Automated coverage is split intentionally:

- Core tests prove deterministic selected-policy behavior, retry of previous Noise faces after denser evidence arrives, fail-closed neighbour-budget behavior, and deterministic enforcement of durable not-same conflicts without weakening DBSCAN parameters.
- PostgreSQL persistence tests exercise durable restart, atomic replacement, new-embedding refresh, exact-model isolation, explicit Unknown inclusion, Rejected/Assigned exclusion, review-reversal invalidation, current review-group/member projections, durable negative feedback and canonical review-history preservation when a live test PostgreSQL connection is supplied.
- Integration tests assert the catalogue-provider boundary and canonical bulk-review behavior; the PostgreSQL cluster-review endpoint path is exercised when the live PostgreSQL test connection is available.

`verify-postgres.ps1` is the repository's explicit local entry point for the live PostgreSQL test suite because it supplies `PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING` only to the child verification process. GitHub CI without that environment variable still builds the live tests but intentionally skips their database bodies.

Final WI-0115 acceptance requires maintainer runtime verification on the real catalogue: browse representative groups, open a group with correct members plus at least one intentional exception, selectively assign only obvious members, record not-same feedback for an exception and observe replacement grouping, then repeat the representative flow at mobile width/touch without canonical history being rewritten for unselected members.

## WI-0113 evaluation method

The private evaluator used one reviewed exact-model sample and one shared cosine-distance matrix for all candidates. It compared:

- DBSCAN,
- HDBSCAN,
- a conservative mutual-neighbour graph with distance/shared-neighbour constraints.

For every candidate it reported false-merge pairs/rate separately from false splits, labelled/overall coverage, noise, singleton/cluster-size distribution, and same-photo conflicts. Candidate ordering treated false merges as the first risk criterion; coverage was considered only after merge risk.

The exporter intentionally wrote no source paths, filenames, crop paths, person names, or raw catalogue IDs. Person/face/photo identifiers were replaced with local synthetic labels. The resulting JSON still contains biometric embeddings and therefore belongs only under ignored/private storage.

See `tools/cluster-evaluation/README.md` for the reproducible local evaluation procedure.
