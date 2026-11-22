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

Regeneration clears the active ranking projection for the selected model revision before rebuilding it, so a face that has since been assigned or rejected cannot retain stale ranked suggestions. Historical suggestion rows with reviewed status remain durable evidence.

## Schema version 5

Schema version 5 adds `identity_suggestion_rankings`, a model-versioned projection that maps rank one or two to an existing `identity_suggestions` row and stores the best-versus-second score margin plus generation time. Ranking rows are separate from canonical labels and cascade with their face occurrence or suggestion.

The migration is forward-only and transactional. Integration coverage verifies fresh schema creation and an upgrade from a version-4 catalogue while preserving an existing rejected suggestion.

## Validation

`SqliteIdentityMatcherTests` covers:

- best and second-best ranking with a deterministic score margin;
- current review assignments and legacy confirmed labels as exemplars;
- exclusion of undone assignments and non-confirmed labels;
- persistent filtering of rejected face-person pairs;
- repeated generation without changes to human-label or review-action counts;
- removal of rankings after a target becomes reviewed;
- absence of automatic labels on suggestion targets.

`SqliteIdentityMatcherMigrationTests` verifies the version-4 to version-5 upgrade and preservation of existing suggestion state.

Pull request [#33](https://github.com/erikwasa/Photo-Identity-Indexer/pull/33) merged at `50ca5ca422c8a7026120ff303de87b2a52755473`. GitHub Actions run `30225300153` passed dependency restore, Release build with warnings as errors, all tests, documentation validation, generated-document checks, the published review application smoke path and Windows mixed-media verification.

## Completion

WI-0016 and M05 completed on 2026-07-27 after the maintainer merged pull request #33. The exact matcher remains review-only; threshold calibration and held-out performance measurement continue in WI-0017/M06.

## Deliberate limitations

- Exact scanning establishes correctness before approximate-nearest-neighbour indexing.
- No threshold is interpreted as automatic acceptance; suggestions remain review-only.
- Threshold calibration and measured false-accept/false-reject performance belong to WI-0017/M06.
- Suggestion presentation through the review UI or a dedicated operator command can be added as a later integration slice.
