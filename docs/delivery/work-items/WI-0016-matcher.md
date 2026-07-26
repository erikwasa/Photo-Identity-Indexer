---
id: WI-0016
title: Add identity matcher
milestone: M05
status_source: ../status/work-items.yaml
depends_on: [WI-0009, WI-0015]
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Persistence.Sqlite, PhotoIdentity.Integration.Tests]
---

# WI-0016: Add identity matcher

## Objective

Compare unlabelled embeddings with human-confirmed exemplars using exact cosine similarity and persist ranked suggestions with score margins.

## Acceptance criteria

- [x] Best and second-best candidates are recorded.
- [x] Rejected face-person pairs are filtered.
- [x] Suggestions can be regenerated without changing labels.
- [x] Only human-confirmed examples are used as exemplars.

## Matching policy

`SqliteIdentityMatcher` operates on one explicit embedding model identifier and model hash. It reads the newest matching embedding for each face occurrence and performs an exact local cosine scan using `EmbeddingVector.CosineSimilarity`.

Current active manual assignments from the append-only review history are eligible exemplars. Legacy `person_labels` rows with label kind `confirmed` are also eligible when that face has no review-action history. Undone assignments, non-confirmed labels and merged people are excluded.

Each target person score is the maximum cosine similarity across that person's eligible exemplars. Candidates are sorted by descending score, then by stable person identifier for deterministic ties. At most two distinct people are persisted, together with the best-versus-second score margin.

## Suggestion safety

Suggestions never create, change or delete `person_labels` or `review_actions`. Targets with a current assignment or rejection are skipped. A suggestion explicitly marked `rejected` records a durable face-person exclusion and is not proposed again during later regeneration. Existing reviewed suggestion status is preserved when scores are refreshed.

Ranking metadata is stored separately from canonical labels. The current implementation creates the auxiliary ranking table idempotently inside the SQLite adapter before use, so existing schema-version-4 catalogues can adopt the matcher without changing label or review semantics.

## Validation

`SqliteIdentityMatcherTests` covers:

- best and second-best ranking with a deterministic score margin;
- current review assignments and legacy confirmed labels as exemplars;
- exclusion of undone assignments and non-confirmed labels;
- persistent filtering of rejected face-person pairs;
- repeated generation without changes to human-label or review-action counts;
- absence of automatic labels on suggestion targets.

Draft pull request [#33](https://github.com/erikwasa/Photo-Identity-Indexer/pull/33) contains the implementation and production-shaped integration coverage. The full repository workflow builds with warnings as errors and runs all tests, documentation checks, review-host smoke verification and Windows mixed-media verification.

## Deliberate limitations

- Exact scanning establishes correctness before approximate-nearest-neighbour indexing.
- No threshold is interpreted as acceptance; suggestions remain review-only.
- Threshold calibration and measured false-accept/false-reject performance belong to WI-0017/M06.
- Suggestion presentation through the review UI or a dedicated operator command can be added as the next integration slice if required.
