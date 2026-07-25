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

- `CatalogueSource`, `CatalogueAsset` and `CatalogueAssetRevision` retain the Core strong identifiers and validate persistence invariants;
- `SqliteAssetCatalogueRepository` resolves sources and assets by both strong identifier and source-owned natural key;
- `SaveRevisionAsync` upserts source and asset metadata and inserts the immutable revision in one transaction;
- repeated writes of the same asset and content SHA-256 return the existing revision;
- revision history remains intact when source roots or source keys change.

This boundary allows WI-0012 to catalogue files without owning SQL or converting typed identifiers to strings.

## Transactional face inspection repository

Draft pull request [#19](https://github.com/erikwasa/Photo-Identity-Indexer/pull/19) adds the persistence boundary for complete inspection output:

- typed occurrence, detector observation, aligned crop and embedding records;
- one transaction for the occurrence, observation, crop and embedding graph;
- natural-key resolution that preserves stable occurrence and crop identities on reruns;
- refreshed detector output and crop storage metadata for an existing model/crop result;
- immutable embeddings for an exact crop, model ID and model hash;
- normalized geometry encoded as JSON and embedding floats encoded as deterministic little-endian binary;
- temporary-database tests for round trips, idempotence, relationship validation and rollback.

## Validation

```powershell
dotnet test tests/PhotoIdentity.Integration.Tests/PhotoIdentity.Integration.Tests.csproj
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
```

## Remaining work

- Add typed people, human-label and suggestion repositories.
- Add processing-run and processing-job repositories with update and concurrency coverage.
- Document backup, concurrent writer and schema-upgrade behaviour before WI-0011 is completed.
