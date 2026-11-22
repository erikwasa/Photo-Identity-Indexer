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

- [x] The operator selects a catalogue, exact detector and embedder revisions and an explicit photo-revision or processing-run scope.
- [x] Gallery, validation and test assignment is deterministic from a required recorded seed.
- [x] Faces from the same source photo cannot leak across gallery, validation or test splits.
- [x] A person's gallery exemplars cannot be duplicated into validation or test under another sample ID.
- [x] The export records model hashes, pipeline version, source revision identifiers and a canonical catalogue-input digest.
- [x] Repeating the export with unchanged inputs produces byte-for-byte identical manifest data.
- [x] The command reports insufficient known or unknown examples clearly instead of silently weakening a split.
- [x] Real manifests remain outside the repository and contain no unnecessary absolute source paths.

## Implemented export

`photoid evaluate export` reads active human assignments for one exact detector and embedder revision. The operator selects exactly one scope: a processing run or one or more immutable asset revisions.

The required seed is combined with stable identifiers through SHA-256 ordering. Assignment does not depend on runtime random-number implementations. Whole asset revisions are reserved for one split, so every face from the same source photo remains isolated from the other splits.

Known validation and test samples belong to people represented in the gallery. Unknown samples are also human-assigned faces, but their people are deliberately absent from the gallery. Face-level rejection is not treated as an unknown identity because it represents a rejected detection rather than a confirmed unknown person.

The manifest includes exact detector and embedder hashes, embedding dimensions, pipeline version, source revision IDs and content hashes, split configuration, timing policy and a digest over the canonical eligible catalogue inputs. It excludes source roots and crop storage paths.

## Verification state

Automated coverage protects run and explicit-revision scopes, deterministic bytes, source-photo isolation, canonical face uniqueness, evaluator compatibility, privacy and insufficient-unknown errors. The human maintainer then exercised export and evaluation against the private pilot catalogue on 2026-07-30. Repeated manifests and reports were byte-for-byte identical, split-isolation and privacy checks passed, and real manifests remained outside the repository.
