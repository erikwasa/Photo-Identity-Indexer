# Canonical data model

The SQLite catalogue is sensitive application data and the canonical local record of source identity, immutable photo revisions, people, human review history and resumable processing state. Model-produced results are retained under exact provenance but remain derived and replaceable.

This document describes logical ownership and invariants. Physical tables and migrations remain implementation details of `PhotoIdentity.Persistence.Sqlite`.

## Sources, assets and revisions

A **source** defines a trusted local filesystem boundary. Personal OneDrive is represented by a locally synchronised folder; the catalogue stores no Microsoft Graph credentials.

An **asset** is the stable catalogue identity of one source media item. Its current source key or path can change while the internal asset ID remains stable after reconciliation.

An **asset revision** is an immutable observed content version of an asset. It records provenance such as observation time, media type, dimensions, size and content fingerprint when available.

Processing attaches to revisions so changed content cannot silently reuse older detections, crops or embeddings.

## Face occurrences and detector observations

A **face occurrence** is the stable identity of one face within an immutable asset revision. It is the object that receives crops, embeddings, suggestions and human review actions.

Detector output is stored as model-versioned **observations** containing the exact detector revision, confidence, bounding box, landmarks and observation time. A later detector can add another observation without replacing the face occurrence or its canonical review state.

## Crops and embeddings

A **face crop** records a derived review or alignment artefact with its protocol, dimensions, hash and storage reference. Crop files remain sensitive and must be backed up with the catalogue or regenerated from retained source and provenance.

A **face embedding** belongs to:

- one face occurrence;
- one crop/alignment contract; and
- one exact embedding-model revision.

Baseline and candidate embeddings coexist. Embeddings from different revisions must not be compared using an assumed shared score scale.

## People and review history

A **person** has a stable internal ID and human-maintained display name. People are shared across all model revisions.

Human decisions are append-only review actions. They include assignment, rejection, undo and person-maintenance operations such as rename or merge. Current state is derived from active history; audit records are not replaced by model output.

An active assignment is canonical identity data. A rejection preserves negative evidence. Merged people resolve to a surviving canonical person without rewriting model provenance.

## Exemplars and suggestions

An **exemplar** is a human-confirmed face used as positive identity evidence for matching.

An **identity suggestion** is advisory derived data. It identifies:

- the target face occurrence;
- the suggested person;
- the exact embedding model ID and hash;
- score and ranking evidence; and
- lifecycle state such as pending or superseded by later regeneration/review.

Only human-confirmed assignments become exemplars. Suggestions never train later suggestions automatically. Rejected face-person pairs remain excluded under the governed matching rules.

## Processing runs and jobs

A **processing run** persists source scope, output location, selected model IDs and operational policy. Its jobs and attempts record pending, running, completed and failed work so an interrupted run can resume without changing models or duplicating canonical revision identity.

Run state is canonical operational data. Individual model outputs remain derived.

## Evaluation data

Evaluation exports are neutral private manifests derived from a reviewed catalogue. They record:

- dataset and pipeline identifiers;
- exact detector and embedder revisions;
- immutable source scope;
- deterministic split policy and seed; and
- gallery, validation and held-out test membership.

Evaluation reports are derived and reproducible. Validation may select thresholds; held-out test data only reports final performance.

## Collection data

Collection queries read canonical people/review state plus optional exact-model suggestion evidence. They return opaque asset/revision identifiers, media metadata and matched-person evidence.

The collection browser uses non-persisted bounded thumbnails. The versioned neutral manifest contains HTTP resource URLs rather than local source roots, source keys, filenames or crop paths.

## Portable bundles and imports

Job bundles contain explicitly selected immutable revision inputs, neutral names, exact model manifests and checksums. Result bundles contain detector/embedding outputs, errors, timings and checkpoints.

Validated import matches known revision IDs, verifies checksums and provenance, and changes only permitted derived state. Bundles never contain people, assignments, rejections or append-only review history.

## Canonical versus regenerable

| Canonical or governed | Derived and regenerable |
|---|---|
| Source, asset and revision identity | Detector observations |
| People | Crops and thumbnails |
| Assignments and rejections | Embeddings |
| Append-only review history | Suggestions and rankings |
| Processing-run/job state | Evaluation manifests and reports |
| Bundle import/provenance records | Portable processing outputs |

Derived data is still biometric or private and must be protected accordingly.

## Provenance requirement

Every derived result must be traceable to the immutable source revision, exact model ID and hash, material preprocessing/alignment contract and processing run or import that produced it.

See the [Glossary](../glossary.md), [Recognition and identity matching](identity-matching.md) and [SQLite persistence operations](../operations/sqlite-persistence.md).
