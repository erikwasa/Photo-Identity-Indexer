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

Model-specific embeddings and suggestions can coexist for the same canonical face occurrence without changing people or review history.

## Canonical assignment actors

The current implementation creates identity assignments through human review. ADR-0006 establishes the accepted next direction: WI-0043 will allow an explicitly enabled exact-model policy to create canonical High-confidence automatic assignments.

Both human and future automatic assignments use append-only canonical history. Automatic assignments must retain their model/score/policy provenance. Manual correction supersedes an earlier automatic assignment rather than deleting history.

## Exemplars

An active canonical assignment may provide positive exemplar evidence when the required exact-model embedding exists.

Until WI-0043 is implemented, the active exemplars are human-assigned faces only. After WI-0043, eligible active automatic assignments may also become exemplars in later regeneration runs.

Rejected faces, rejected face-person pairs, unreviewed faces and the planned Unknown review state are not positive exemplar evidence.

## Ranked suggestions and regeneration

The current matcher regeneration:

1. loads embeddings for one exact model revision;
2. builds person evidence from active eligible exemplars;
3. scores eligible unreviewed targets;
4. records ranked candidate people, score and margin evidence;
5. preserves rejected face-person exclusions; and
6. leaves canonical assignments/review history unchanged.

WI-0043 adds a policy phase after scoring. One regeneration must use a fixed exemplar snapshot: score all targets first, then apply qualifying High automatic assignments. Newly automatic exemplars cannot affect candidate scoring until a later regeneration.

WI-0045 later exposes regeneration through the normal browser workflow instead of requiring the CLI.

## Review states

Current states are Unreviewed, Assigned and Rejected. WI-0047 adds Unknown as a distinct real-person-but-unidentified state.

- **Assigned** provides canonical person evidence.
- **Unreviewed** is undecided and may have exact-model suggestions.
- **Unknown** (planned) is a real face whose person is not known; it is not a person identity, exemplar or person-collection match.
- **Rejected** represents a false/useless detection and provides no positive person evidence.

A person-specific rejected suggestion remains durable negative evidence so the same face-person pair is not immediately proposed again under the governed rules.

## Confidence groups and automatic policy

WI-0043 introduces configurable High, Medium and Low score groups for one exact model policy. The High boundary is also the only group eligible for automatic canonical assignment when the toggle is enabled.

Threshold changes govern future decisions and do not retroactively undo assignments. Scores remain exact-model-specific.

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
- Automatic assignments, once implemented, are canonical decisions with explicit provenance rather than hidden derived labels.
- Regeneration uses a fixed exemplar snapshot before automatic assignments are applied.
- Manual correction supersedes an automatic assignment and changes later exemplar evidence.
- Rejected face-person pairs remain excluded.
- Unknown and rejected faces do not become exemplars.
- Scores from different model revisions are never silently mixed.

See [ADR-0002](../decisions/ADR-0002-model-independent-labels.md), [ADR-0006](../decisions/ADR-0006-canonical-auto-assignment.md), [Canonical data model](data-model.md) and [Model governance](../models/model-governance.md).