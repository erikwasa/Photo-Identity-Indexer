---
id: WI-0011
title: Add SQLite persistence
milestone: M02
status_source: ../status/work-items.yaml
depends_on: [WI-0003]
affected_modules: [PhotoIdentity.Persistence.Sqlite, PhotoIdentity.Integration.Tests]
---

# WI-0011: Add SQLite persistence

## Objective

Implement migrations and repositories for assets, revisions, face occurrences, observations, crops, embeddings, people, labels, suggestions and processing records.

## Acceptance criteria

- [x] A fresh database can be created and migrated.
- [x] Human labels are independent of model-derived rows.
- [x] Embeddings are versioned by model and crop.
- [x] Integration tests use temporary databases.

## Implemented foundation

Pull request [#17](https://github.com/erikwasa/Photo-Identity-Indexer/pull/17) added `SqliteCatalogueDatabase`, schema version 1 and temporary-database integration coverage. It merged as `d1fa036ea256f8d5c9f8133ab184747908f0d64e` after GitHub Actions run `30173090694` passed.

The schema contains sources, assets, revisions, face occurrences, detector observations, crops, embeddings, people, human labels, identity suggestions, processing runs and processing jobs. Foreign keys are enabled for every opened connection, migrations run transactionally and repeated initialisation is idempotent.

Human labels reference people and face occurrences directly rather than model observations, embeddings or suggestions. Embeddings are unique by face crop, model ID and model hash so multiple model versions can coexist.

## Typed asset catalogue repository

Pull request [#18](https://github.com/erikwasa/Photo-Identity-Indexer/pull/18) added the scanner-facing source, asset and revision repository. It merged as `2de0194b0835a8c9b8d13f08b6fa5311e855f889` after GitHub Actions run `30173892338` passed.

- sources, assets and revisions retain Core strong identifiers;
- source, asset and immutable revision writes are committed in one transaction;
- source-owned natural keys and repeated content hashes resolve stable identities;
- revision history remains intact when mutable source metadata changes.

## Transactional face inspection repository

Pull request [#19](https://github.com/erikwasa/Photo-Identity-Indexer/pull/19) added complete face-inspection persistence. It merged as `382011588f7055d783a0eae4d567f4bbc0adc0c9` after GitHub Actions run `30174420996` passed.

- occurrence, observation, crop and embedding records are written in one transaction;
- reruns resolve stable occurrence and crop identities;
- detector and crop metadata can refresh while exact crop/model/model-hash embeddings remain immutable;
- normalized geometry and embedding vectors round-trip through deterministic representations.

## Identity and human-label repository

Pull request [#20](https://github.com/erikwasa/Photo-Identity-Indexer/pull/20) added people, labels and identity suggestions. It merged as `e4c2d1311a18b53b1492523789385b65edc9a7fc` after GitHub Actions run `30176173088` passed.

- people and human labels can be written transactionally without model-derived rows;
- repeated labels retain a stable row while reviewer metadata can be corrected;
- suggestions are versioned by face, person, model ID and model hash;
- suggestion reruns refresh scores without overwriting reviewed status or original creation time.

## Durable processing repository

Draft pull request [#21](https://github.com/erikwasa/Photo-Identity-Indexer/pull/21) adds the run and job persistence boundary required by resumable processing:

- typed pending, running and terminal run/job states;
- atomic run-plus-job creation with idempotence by run ID and run/revision pair;
- oldest-due job claiming with attempt increments only when work is claimed;
- delayed retries that preserve attempt history;
- guarded running-to-success/failure transitions;
- run completion only after every job is terminal, with failed-job outcomes propagated;
- temporary-database tests including concurrent workers claiming distinct jobs.

## Validation

```powershell
dotnet test tests/PhotoIdentity.Integration.Tests/PhotoIdentity.Integration.Tests.csproj
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
```

## Remaining work

- Resolve CI or review findings for pull request #21.
- Document backup, concurrent writer and schema-upgrade behaviour.
- Complete WI-0011 after the operational persistence policy is documented and verified.
