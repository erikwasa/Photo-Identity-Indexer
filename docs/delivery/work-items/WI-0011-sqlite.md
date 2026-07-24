---
id: WI-0011
title: Add SQLite persistence
milestone: M02
status_source: ../status/work-items.yaml
depends_on: [WI-0003]
affected_modules: [PhotoIdentity.Persistence.Sqlite]
---

# WI-0011: Add SQLite persistence

## Objective

Implement migrations and repositories for assets, revisions, face occurrences, observations, crops, embeddings, people, labels, suggestions and processing records.

## Acceptance criteria

- [ ] A fresh database can be created and migrated.
- [ ] Human labels are independent of model-derived rows.
- [ ] Embeddings are versioned by model and crop.
- [ ] Integration tests use temporary databases.
