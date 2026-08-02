# Recognition and identity matching

Identity matching produces advisory evidence from model-versioned face embeddings. Human review remains the only authority for canonical identity assignments.

## Processing pipeline

The accepted local baseline uses:

- detector: `yunet-2023mar-fp32`;
- five-point SFace alignment: `sface-five-point-v1`;
- embedder: `sface-2021dec-fp32`; and
- matcher: cosine similarity against human-confirmed exemplars from the same exact embedder revision.

The governed candidate `sface-2021dec-int8` uses the same detector, alignment and external tensor contract while retaining a distinct model ID and SHA-256.

## Exact model scope

A matching or suggestion operation must identify the embedder by both:

- model ID; and
- exact SHA-256 hash.

Weights, quantisation, preprocessing, alignment, input dimensions or material runtime behavior are part of model identity. Embeddings, scores and thresholds from different revisions are not interchangeable.

Baseline and candidate embeddings can coexist for the same face occurrence without changing people or review history.

## Exemplars

Only active human-confirmed assignments become exemplars.

Suggestions do not become exemplars automatically. Rejected or unreviewed faces are not positive training evidence. A later model revision derives its own embeddings and suggestions while using the same canonical people and confirmed assignments.

## Ranked suggestions

Suggestion regeneration:

1. loads embeddings for one exact model revision;
2. builds person evidence from active confirmed exemplars;
3. scores eligible unreviewed target faces;
4. records ranked candidate people and score evidence;
5. preserves rejected face-person exclusions; and
6. leaves people, assignments, rejections and append-only review history unchanged.

The browser and collection API expose exact-model suggestion provenance. Advisory collection results require an explicit model ID, exact hash and minimum score.

## Review-state rules

- **Assigned** faces use active human-confirmed assignments.
- **Unreviewed** faces have no active assignment or rejection and may have exact-model pending suggestions.
- **Rejected** faces do not provide positive collection evidence for a person.
- **All** collection evidence combines confirmed assignments with qualifying unreviewed suggestions only when the exact suggestion policy is supplied.

No threshold creates a canonical label automatically.

## Negative evidence

A rejected face-person pair is durable human evidence. Regeneration must not propose the same pair again under the governed matching rules.

A general face rejection and a person-specific suggestion rejection are distinct review meanings, but neither can become positive evidence without a later explicit human assignment.

## Scoring and thresholds

Cosine scores are meaningful only within one exact embedding revision and preprocessing contract. Validation can select an operating threshold for that revision. Held-out test results report performance without selecting a replacement threshold.

When comparing revisions:

- use the same immutable source and detector scope;
- preserve the same people and review history;
- export the same deterministic split;
- select thresholds independently under the same validation procedure; and
- compare task quality, unknown rejection, confusion, throughput, storage and review effort.

See the [multi-model comparison workflow](../operations/multi-model-comparison.md).

## Accepted model recommendation

The accepted private FP32-versus-INT8 comparison found both revisions correct on 20 representative manually reviewed faces and found no material practical quality advantage for INT8.

Retain `sface-2021dec-fp32` as the current default embedder. Keep `sface-2021dec-int8` as a governed candidate for later runtime, Azure-consistency, cost or broader-corpus evidence.

Final production selection remains a later decision; persisted human review data is independent of that decision.

## Improvement boundary

Future improvements can include:

- more diverse confirmed exemplars;
- age, pose and quality coverage;
- improved negative-evidence handling;
- person-specific or cohort-aware thresholds;
- prototype/exemplar selection;
- quality-aware ranking; and
- rematching unknown faces under a new governed revision.

Fine-tuning is deferred until application-level and data-quality improvements have been measured and exhausted.

## Invariants

- Human assignments and rejections are canonical.
- Suggestions are derived, exact-model scoped and regenerable.
- Suggestions never train suggestions automatically.
- Rejected pairs remain excluded.
- Model changes do not erase people or review history.
- Scores from different revisions are never silently mixed.

See [Canonical data model](data-model.md), [Model manifests and governance](../models/model-governance.md) and the [Glossary](../glossary.md).
