---
id: WI-0028
title: Export reviewed catalogues to model-lab
milestone: M06
status_source: ../status/work-items.yaml
depends_on: [WI-0013, WI-0015, WI-0017]
affected_modules: [PhotoIdentity.Cli, PhotoIdentity.Persistence.Sqlite, tools/model-lab, PhotoIdentity.Integration.Tests]
---

# WI-0028: Export reviewed catalogues to model-lab

## Objective

Create a reproducible command that builds an evaluation manifest from a reviewed local catalogue without manually copying embeddings or identity identifiers.

## Acceptance criteria

- [ ] The operator selects a catalogue, model revision and explicit photo or run scope.
- [ ] Gallery, validation and test assignment is deterministic from a recorded seed or checked-in private split manifest.
- [ ] Faces from the same source photo cannot leak across validation and test splits.
- [ ] A person's gallery exemplars cannot be duplicated into validation or test under another sample ID.
- [ ] The export records model hashes, pipeline version, source revision identifiers and input digest.
- [ ] Repeating the export with unchanged inputs produces byte-for-byte identical manifest data.
- [ ] The command reports insufficient known or unknown examples clearly instead of silently weakening a split.
- [ ] Real manifests remain outside the repository and contain no unnecessary absolute source paths.
