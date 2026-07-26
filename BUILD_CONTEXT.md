# Build context

## Current milestone

**M05 — Identity matching**

## Current work item

**WI-0016 — Add identity matcher**

Status: `in_progress`

## Recently completed milestone

**WI-0018 / M07** completed on 2026-07-27 after the maintainer ran a privacy-safe private-image export, database-free process, import and replay-safe reimport; confirmed the existing human assignment remained canonical; retained only aggregate evidence; and removed the isolated verification workspace and temporary transfer artefacts.

## Branch and pull request

- Implementation branch: `agent/WI-0016-identity-matcher`
- Draft pull request: [#33 — Add exact cosine identity matcher](https://github.com/erikwasa/Photo-Identity-Indexer/pull/33)

## Objective

Compare unlabelled face embeddings with human-confirmed exemplars using exact cosine similarity and persist ranked, reviewable identity suggestions without changing canonical labels.

## Current slice

Build a conservative exact matcher over one explicit embedding model revision. Each person is scored by their best current human-confirmed exemplar. Store at most the best and second-best distinct people, including the best-versus-second score margin, while remembering rejected face-person pairs across regeneration.

## Relevant files

- `src/PhotoIdentity.Core/Recognition/EmbeddingVector.cs`
- `src/PhotoIdentity.Persistence.Sqlite/IdentityCatalogueRecords.cs`
- `src/PhotoIdentity.Persistence.Sqlite/SqliteIdentityCatalogueRepository.cs`
- `src/PhotoIdentity.Persistence.Sqlite/SqliteIdentityMatcher.cs`
- `src/PhotoIdentity.Persistence.Sqlite/SqliteReviewRepository.cs`
- `tests/PhotoIdentity.Integration.Tests/SqliteIdentityMatcherTests.cs`
- `docs/delivery/work-items/WI-0016-matcher.md`
- `docs/delivery/status/work-items.yaml`

## Acceptance test for this slice

- Current active human assignments and legacy `confirmed` labels can act as exemplars.
- Undone assignments, non-confirmed labels and merged people cannot act as exemplars.
- Each target records at most two distinct people in deterministic score order.
- The best-versus-second score margin is retained.
- Rejected face-person pairs are filtered from later regeneration.
- Regeneration never creates, changes or deletes human labels or review actions.
- Suggestions remain versioned by the exact embedding model identifier and hash.

## Verification

The initial implementation and production-shaped integration tests are in pull request #33. GitHub Actions builds with warnings as errors and runs all repository tests, living-document validation, generated-document checks, the published review application smoke path and Windows mixed-media verification.

## Deliberate limitations

- This is an exact local scan intended to establish correctness before approximate-nearest-neighbour indexing.
- It does not auto-accept suggestions or auto-label faces.
- Threshold calibration and measured false-accept/false-reject performance belong to WI-0017/M06.
- The Azure pilot, WI-0020/M09, is also ready but is not the active implementation track.

## Next action

Finish CI and review for pull request #33, then expose the ranked suggestions through the review workflow or a dedicated operator command if required by acceptance testing.
