# Recognition and identity matching

Identity matching produces model-versioned evidence for canonical person assignments. Canonical assignments are independent of the recognition model that proposed them and remain auditable/reversible through review history.

## Permanent archive processing profile

The governed permanent archive profile currently uses:

- detector: `centerface-2019-fp32`;
- detector confidence: `0.5`;
- detector pipeline: `single-pass`;
- five-point SFace alignment: `sface-five-point-v1`; and
- embedder: `sface-2021dec-fp32`.

The generic historical batch defaults still reference YuNet and must not be confused with the permanent archive profile.

## Exact model scope

A matching or suggestion operation identifies the embedder by both model ID and exact SHA-256 hash. Embeddings, scores and thresholds from different revisions are not interchangeable.

Model-specific embeddings and suggestions can coexist for the same canonical face occurrence without changing people or review history. Each exact embedding-model revision has an independent confidence policy and policy-version stream.

## Canonical assignment actors

Human review and the explicitly enabled exact-model identity-suggestion policy may create canonical assignments. Automatic assignments use actor `identity-matcher:auto` and the same canonical suggestion-acceptance path as a normal accepted suggestion.

Both human and automatic assignments use append-only canonical history. Automatic assignments retain the exact model revision, rank-1 score, rank-1/rank-2 margin, policy version and thresholds that justified the decision. A later manual correction supersedes the earlier automatic assignment through a newer canonical review action rather than deleting history.

Unknown and false-detection decisions are also append-only canonical review actions, but they do not create person identity evidence. The latest unreversed face decision determines current review state. A later manual assignment may therefore supersede Unknown without deleting the earlier Unknown action; undoing that later assignment reveals the still-active earlier Unknown decision again.

## Exemplars

An active canonical assignment may provide positive exemplar evidence when the required exact-model embedding exists, regardless of whether the active assignment was created by human review or the enabled automatic policy.

Automatic assignments become eligible exemplars only on a later regeneration. Rejected faces, rejected face-person pairs, unreviewed faces and Unknown faces are not positive exemplar evidence. An older assignment hidden by a newer Unknown decision is not an exemplar while Unknown is the active state.

## Ranked suggestions and regeneration

Normal matcher regeneration:

1. loads embeddings for one exact model revision;
2. builds person evidence from the current active eligible exemplars;
3. scores eligible **unreviewed** targets from that fixed exemplar snapshot;
4. records up to rank 1 and rank 2 candidate people, scores and rank-1/rank-2 margin evidence;
5. preserves rejected face-person exclusions; and
6. after all targets have been scored, applies that exact model revision's current persisted policy to qualifying High rank-1 suggestions when automatic assignment is enabled.

Unknown faces are excluded from normal regeneration targets. The persistence boundary also exposes an explicit `UnreviewedAndUnknown` target scope for future intentional rematching. That opt-in may regenerate advisory rankings for Unknown faces, but it does not change their stored Unknown review state. Automatic assignment separately requires no active assignment, Unknown or rejection, so an intentionally rematched Unknown face remains human-controlled until a manual review action supersedes or undoes Unknown.

The fixed snapshot is deliberate: newly automatic assignments cannot affect candidate scoring until a later regeneration. A later manual reassignment becomes the latest active identity and therefore changes the exemplar identity used by later matching.

The CLI remains a valid diagnostic path through `match regenerate`. The browser workflow uses a separate durable run controller rather than spawning the CLI or holding one HTTP request/SQLite transaction open for the whole catalogue. Starting a web regeneration snapshots the exact-model policy version, an identity-evidence version and the current eligible target face IDs. A background worker then scores one target in a short transaction, records durable progress and can reclaim an interrupted running target after application restart.

The web run exposes target, processed-target, suggested-target, suggestion, automatic-assignment and error counts. One active run is allowed per exact model revision. A second request for the same revision is rejected until the first is no longer active.

Canonical review decisions, suggestion decisions, person merges or new exact-model embeddings change the run's identity-evidence version. If any of those changes while a run is active, the worker stops the run as stale instead of silently changing exemplar evidence halfway through scoring. A policy-version change likewise prevents finalization under different thresholds. After the final target, the worker revalidates evidence and policy, removes obsolete rankings, then applies automatic assignment once for the complete fixed-snapshot result. This preserves the same-run non-cascade invariant: automatic assignments cannot become exemplars until a later regeneration.

The `Regenerate matches` web workspace selects an exact model revision, shows current/stale/queued/running/completed state and durable progress, and links back to the confidence-group Faces queue after completion. Review reads remain available while scoring proceeds because regeneration uses short per-target transactions.

The explicit Unknown-inclusive scope is an internal future-safe boundary rather than an automatic rematch workflow.

## Review states

Current states are Unreviewed, Assigned, Unknown and Rejected.

- **Assigned** provides canonical person evidence.
- **Unreviewed** is undecided and may participate in normal exact-model suggestion generation.
- **Unknown** is a real face whose person is not known. It has no PersonId, is not a person identity, exemplar or person-collection match, and is excluded from normal matching by default.
- **Rejected** represents a false/useless detection and provides no positive person evidence.

Unknown is intentionally distinct from Rejected: marking a face Unknown says the detection is valid while the identity is unresolved. Both decisions are reversible. A later manual assignment can supersede Unknown while preserving audit history.

A person-specific rejected suggestion remains durable negative evidence so the same face-person pair is not immediately proposed again under the governed rules.

## Confidence groups and automatic policy

Each exact embedding-model revision has a persisted versioned identity-suggestion policy editable from the local review application or through explicit CLI policy overrides. A new exact-model policy starts with automatic assignment disabled.

The default values for a newly initialized exact-model policy are:

- High score threshold: `0.70`;
- High rank-1/rank-2 margin threshold: `0.10`; and
- Medium score threshold: `0.50`.

A rank-1 suggestion is **High** only when both conditions hold:

1. rank-1 score is at or above the configured High score threshold; and
2. the persisted rank-1/rank-2 score margin exists and is at or above the configured High margin threshold.

A suggestion that meets the Medium score threshold but fails either High condition is **Medium**. Scores below the Medium threshold are **Low**. A missing rank-2 margin can therefore never qualify as High.

Only High rank-1 suggestions are eligible for automatic canonical assignment, and only when that exact model revision's policy toggle is enabled. Threshold changes govern future classification and automatic decisions for that revision only; they do not retroactively undo historical assignments or alter another model revision's policy.

The unified Faces queue shows the computed group and can filter High, Medium or Low suggestions and order the queue by confidence group with High first. Classification and automatic assignment load the same exact-model persisted policy so UI grouping cannot drift from the automation gate.

Before automatic assignment is enabled for routine archive use, the score and margin thresholds must be tuned against a private reviewed sample as required by WI-0043.

## Model comparison boundary

When comparing embedding revisions:

- use the same immutable source and detector population;
- preserve the same canonical people/review history;
- export the same deterministic split;
- select thresholds independently under the same validation procedure; and
- compare quality, unknown rejection, confusion, throughput, storage and review effort.

The completed FP32-versus-INT8 comparison used the earlier YuNet detector population and retained `sface-2021dec-fp32` because INT8 showed no material practical advantage. Because M16 later changed the face population to CenterFace, a production-model reaffirmation must use the selected CenterFace detections before claiming that the old comparison fully represents the permanent archive.

## Invariants

- Canonical people and identity/review history survive model replacement.
- Derived embeddings and suggestions are exact-model scoped and regenerable.
- Each exact model revision has an independent confidence-policy version stream.
- Automatic assignments are canonical decisions with explicit model and policy provenance rather than hidden derived labels.
- High confidence requires both an absolute rank-1 score gate and a rank-1/rank-2 gap gate.
- Regeneration uses a fixed exemplar snapshot before automatic assignments are applied.
- Browser-triggered regeneration is durable, restart-safe and does not depend on one long HTTP request or transaction.
- An active browser regeneration becomes stale if identity evidence changes before finalization.
- Manual correction supersedes an automatic assignment and changes later exemplar evidence.
- Rejected face-person pairs remain excluded.
- Unknown and rejected faces do not become exemplars or person-collection evidence.
- Normal matcher regeneration excludes Unknown; explicit Unknown-inclusive rematching is advisory and never enables automatic assignment by itself.
- Scores and thresholds from different model revisions are never silently mixed.

See [ADR-0002](../decisions/ADR-0002-model-independent-labels.md), [ADR-0006](../decisions/ADR-0006-canonical-auto-assignment.md), [Canonical data model](data-model.md) and [Model governance](../models/model-governance.md).