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

The first M02 slice adds `SqliteCatalogueDatabase`, schema version 1 and temporary-database integration coverage.

The schema contains sources, assets, revisions, face occurrences, detector observations, crops, embeddings, people, human labels, identity suggestions, processing runs and processing jobs. Foreign keys are enabled for every opened connection, migrations run transactionally and repeated initialisation is idempotent.

Human labels reference people and face occurrences directly rather than model observations, embeddings or suggestions. Embeddings are unique by face crop, model ID and model hash so multiple model versions can coexist.

## Validation

```powershell
dotnet test tests/PhotoIdentity.Integration.Tests/PhotoIdentity.Integration.Tests.csproj
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
```

## Remaining work

- Add typed repositories and persistence records over the schema.
- Define transactional write boundaries for complete inspection results.
- Add round-trip, update and concurrency integration tests.
- Document backup and schema-upgrade behaviour before WI-0011 is completed.
