# Recognition and identity matching

Identity matching produces exact-model evidence for canonical person assignments. Canonical assignments are independent of the recognition model that proposed them and remain auditable/reversible through review history.

## Permanent archive processing profile

The governed permanent archive profile currently uses:

- detector: `centerface-2019-fp32`;
- detector confidence: `0.5`;
- detector pipeline: `single-pass`;
- five-point SFace alignment: `sface-five-point-v1`; and
- embedder: `sface-2021dec-fp32`.

Historical evaluation evidence may reference earlier YuNet populations; those settings must not be confused with the permanent archive profile.

## Exact model scope

A matching or suggestion operation identifies the embedder by both model ID and exact SHA-256 hash. Embeddings, scores and thresholds from different revisions are not interchangeable.

Model-specific embeddings and suggestions can coexist for the same canonical face occurrence without changing people or review history. Each exact embedding-model revision has an independent confidence policy and policy-version stream.

## Canonical assignment actors

Human review and the explicitly enabled exact-model identity-suggestion policy may create canonical assignments. Automatic assignments use actor `identity-matcher:auto` and the same governed suggestion-acceptance boundary as a normal accepted suggestion.

Both human and automatic assignments use append-only canonical history. Automatic assignments retain exact model revision, rank-1 score, rank-1/rank-2 margin, policy version and thresholds. A later manual correction supersedes an earlier automatic assignment through newer canonical history rather than deleting it.

Unknown and false-detection decisions are also append-only canonical review actions, but they do not create person identity evidence. The latest unreversed face decision determines current review state.

## Exemplars

An active canonical assignment may provide positive exemplar evidence when the required exact-model embedding exists, regardless of whether the active assignment was created by human review or the enabled automatic policy.

Automatic assignments become eligible exemplars only on a later regeneration. Rejected faces, rejected face-person pairs, unreviewed faces and Unknown faces are not positive exemplar evidence. An older assignment hidden by a newer Unknown decision is not an exemplar while Unknown is active.

## Ranked suggestions and durable regeneration

Normal matcher regeneration:

1. loads embeddings for one exact model revision;
2. builds person evidence from the current active eligible exemplars;
3. scores eligible **unreviewed** targets from that fixed exemplar snapshot;
4. records rank-1/rank-2 candidate people, scores and margin evidence;
5. preserves rejected face-person exclusions; and
6. after all targets are scored, applies that exact model revision's current persisted policy to qualifying High rank-1 suggestions when automatic assignment is enabled.

Unknown faces are excluded from normal regeneration targets. An explicit internal `UnreviewedAndUnknown` scope exists for intentional advisory rematching, but it does not change canonical Unknown state or enable automatic assignment for that face.

The browser workflow uses the durable PostgreSQL-backed regeneration controller. Starting regeneration snapshots the exact-model policy version, identity-evidence version and eligible target face IDs. A background worker scores one target in short transactions, records durable progress and can reclaim interrupted work after application restart.

The fixed snapshot is deliberate: newly automatic assignments cannot affect candidate scoring until a later regeneration. Canonical review decisions, suggestion decisions, person merges or new exact-model embeddings change the identity-evidence version. If evidence changes during an active run, that run becomes stale instead of silently changing exemplar evidence halfway through scoring. A policy-version change likewise prevents finalization under different thresholds.

WI-0111 adds bounded automatic follow-up around the same controller. Evidence-version drift is the durable queued condition; a short configurable debounce only coalesces bursts before the normal bounded `StartAsync` path captures the next snapshot. A completed run's own automatic assignments are included in its expected evidence and do not recursively schedule another run by themselves.

The web `Regenerate matches` workspace selects an exact model revision, presents current/stale/queued/running/completed state and durable progress, and links back to confidence-group review after completion. Review reads remain available while scoring proceeds because work is split into short transactions.

The old SQLite-opening `match regenerate` CLI path was retired under WI-0149. New matcher tooling must use PostgreSQL/provider-neutral application contracts rather than restoring a provider-specific command path.

## Review states

Current states are Unreviewed, Assigned, Unknown and Rejected.

- **Assigned** provides canonical person evidence.
- **Unreviewed** is undecided and may participate in normal exact-model suggestion generation.
- **Unknown** is a real face whose person is not known. It has no PersonId, is not a person identity, exemplar or person-collection match, and is excluded from normal matching by default.
- **Rejected** represents a false/useless detection and provides no positive person evidence.

A person-specific rejected suggestion remains durable negative evidence so the same face-person pair is not immediately proposed again under the governed rules.

## Confidence groups and automatic policy

Each exact embedding-model revision has a persisted, versioned identity-suggestion policy. A new exact-model policy starts with automatic assignment disabled.

Default values for a newly initialized exact-model policy are:

- High score threshold: `0.70`;
- High rank-1/rank-2 margin threshold: `0.10`; and
- Medium score threshold: `0.50`.

A rank-1 suggestion is **High** only when both conditions hold:

1. rank-1 score is at or above the configured High score threshold; and
2. the persisted rank-1/rank-2 score margin exists and is at or above the configured High margin threshold.

A suggestion that meets the Medium score threshold but fails either High condition is Medium. Scores below the Medium threshold are Low. A missing rank-2 margin can never qualify as High.

Only High rank-1 suggestions are eligible for automatic canonical assignment, and only when that exact model revision's policy toggle is enabled. Threshold changes govern future classification/automatic decisions for that revision only; they do not retroactively undo historical assignments or alter another model revision's policy.

## Model comparison boundary

When comparing embedding revisions:

- use the same immutable source and detector population;
- preserve the same canonical people/review history;
- use one deterministic evaluation boundary;
- select thresholds independently under the same validation procedure; and
- compare quality, unknown rejection, confusion, throughput, storage and review effort.

The completed FP32-versus-INT8 comparison used an earlier YuNet detector population and is retained as historical evidence. Because M16 later changed the face population to CenterFace, any future production-model reaffirmation must use current governed detections and supported tooling.

## Invariants

- Canonical people and identity/review history survive model replacement.
- Derived embeddings and suggestions are exact-model scoped and regenerable.
- Each exact model revision has an independent confidence-policy version stream.
- Automatic assignments are canonical decisions with explicit model/policy provenance rather than hidden derived labels.
- High confidence requires both an absolute score gate and rank-1/rank-2 gap gate.
- Regeneration uses a fixed exemplar snapshot before automatic assignments are applied.
- Browser-triggered regeneration is durable, restart-safe and PostgreSQL-backed.
- An active regeneration becomes stale if identity evidence changes before finalization.
- A completed run's own automatic assignments do not recursively schedule another run.
- Manual correction supersedes an automatic assignment and changes later exemplar evidence.
- Rejected face-person pairs remain excluded.
- Unknown and rejected faces do not become exemplars or person-collection evidence.
- Normal regeneration excludes Unknown; explicit Unknown-inclusive rematching is advisory and never enables automatic assignment by itself.
- Scores and thresholds from different model revisions are never silently mixed.

See [ADR-0002](../decisions/ADR-0002-model-independent-labels.md), [ADR-0006](../decisions/ADR-0006-canonical-auto-assignment.md), [Canonical data model](data-model.md) and [Model governance](../models/model-governance.md).
