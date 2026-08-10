# Canonical data model

The SQLite catalogue is sensitive application data and the canonical local record of source identity, immutable photo revisions, people, assignment/review history and resumable processing state. Model-produced results are retained under exact provenance but remain derived and replaceable unless an explicit governed policy promotes a decision into canonical history.

This document describes logical ownership and invariants. Physical tables and migrations remain implementation details of `PhotoIdentity.Persistence.Sqlite`.

## Sources, assets and revisions

A **source** defines a trusted local filesystem boundary. Personal OneDrive is represented by a locally synchronised folder; the catalogue stores no Microsoft Graph credentials.

The permanent archive uses one stable source root plus normalized relative included folders. Expanding coverage does not create a second archive source identity.

An **asset** is the stable catalogue identity of one source media item. Its current source key or path can change while the internal asset ID remains stable after reconciliation.

An **asset revision** is an immutable observed content version of an asset. It records provenance such as observation time, media type, dimensions, size and content fingerprint when available. Lightweight metadata may indicate that re-verification is required, but immutable revision identity is established from authoritative bytes rather than metadata alone.

Processing attaches to revisions so changed content cannot silently reuse older detections, crops or embeddings.

## Face occurrences and detector observations

A **face occurrence** is the stable identity of one face within an immutable asset revision. It is the object that receives crops, embeddings, suggestions and canonical review/assignment actions.

Detector output is stored as model-versioned **observations** containing the exact detector revision, confidence, bounding box, landmarks and observation time. A later detector can add another observation without replacing the face occurrence or its canonical review state.

## Crops and embeddings

A **face crop** records a derived review or alignment artefact with its protocol, dimensions, hash and storage reference. Crop files remain sensitive and must be backed up with the catalogue or regenerated from retained source and provenance.

A **face embedding** belongs to:

- one face occurrence;
- one crop/alignment contract; and
- one exact embedding-model revision.

Baseline and candidate embeddings coexist. Embeddings from different revisions must not be compared using an assumed shared score scale.

## People and assignment history

A **person** has a stable internal ID and human-maintained display name. People are shared across all model revisions.

Canonical identity decisions are append-only actions. Human assignment, rejection, undo and person-maintenance operations such as rename or merge remain canonical review actions. An explicitly enabled identity-suggestion policy may also promote a qualifying High rank-1 suggestion into a canonical assignment through the same governed acceptance boundary.

Automatic assignments record the automatic actor plus exact model revision, rank-1 score, rank-1/rank-2 margin, policy version and thresholds. A later manual reassignment supersedes the earlier active assignment through history rather than erasing it.

Current state is derived from active history. An active assignment is canonical identity data regardless of whether its allowed actor was human or the enabled governed automatic policy. A rejection preserves negative evidence. Merged people resolve to a surviving canonical person without rewriting model provenance or historical assignment evidence.

## Exemplars, suggestions and confidence policy

An **exemplar** is an actively assigned face eligible to provide positive identity evidence for one exact embedding revision. Human and automatic assignments can both become exemplars, but an automatic assignment created after one regeneration's scoring phase cannot enter that same regeneration's exemplar snapshot. It becomes eligible on a later run.

An **identity suggestion** is derived model-scoped evidence. It identifies:

- the target face occurrence;
- the suggested person;
- the exact embedding model ID and hash;
- score, rank and rank-1/rank-2 margin evidence; and
- lifecycle state such as pending or superseded by later regeneration/review.

Each exact embedding-model revision has its own **identity suggestion policy** and monotonic policy-version stream. This keeps score calibration isolated when multiple model revisions coexist. Each exact-model policy persists:

- whether automatic assignment is enabled;
- the minimum High rank-1 score;
- the minimum High rank-1/rank-2 score gap;
- the Medium score floor; and
- who changed the policy and when.

High classification requires both the High score and High margin conditions. A missing or insufficient rank-2 margin cannot be High. Suggestions that meet the Medium score floor but not both High conditions are Medium; lower scores are Low.

A suggestion is not canonical merely because it exists. Only an explicit human action or the enabled exact-model automatic-assignment policy may create canonical assignment history. Changing one model revision's policy changes future classification and promotion decisions for that exact revision only; it does not rewrite historical assignments or alter another model revision's thresholds. Rejected face-person pairs remain excluded under the governed matching rules.

The planned Unknown review state represents a real but currently unidentified person without creating a synthetic Person row. Unknown faces are not exemplars or person-collection evidence until later assigned.

## Processing runs and jobs

A **processing run** persists source scope, output location, selected model IDs and operational policy. Its jobs and attempts record pending, running, completed and failed work so an interrupted run can resume without changing models or duplicating canonical revision identity.

Run state is canonical operational data. Individual model outputs remain derived.

## Review proxies and authoritative originals

A **review proxy** is a versioned derived image used for normal local browsing and review context. It is tied to an immutable source revision and explicit proxy-generation profile.

The authoritative original remains the source photo. A proxy does not replace the source revision and must never be used to establish source content identity. Full-resolution originals may be hydrated temporarily under the bounded archive-storage policy for verification, analysis or explicit viewing.

## Evaluation data

Evaluation exports are neutral private manifests derived from a reviewed catalogue. They record:

- dataset and pipeline identifiers;
- exact detector and embedder revisions;
- immutable source scope;
- deterministic split policy and seed; and
- gallery, validation and held-out test membership.

Evaluation reports are derived and reproducible. Validation may select thresholds; held-out test data only reports final performance.

## Collection data

Collection queries read canonical people/assignment state plus optional exact-model suggestion evidence. They return opaque asset/revision identifiers, media metadata and matched-person evidence.

The collection browser uses versioned review proxies where configured. Neutral manifests expose opaque HTTP resource URLs rather than local source roots, source keys, filenames or crop paths.

Future EXIF/location and visible-content tagging data extend asset/revision metadata without changing identity ownership. Model-generated tags remain derived with provenance; manually curated tags may be canonical user data when that capability is introduced.

## Portable bundles and imports

Job bundles contain explicitly selected immutable revision inputs, neutral names, exact model manifests and checksums. Result bundles contain detector/embedding outputs, errors, timings and checkpoints.

Validated import matches known revision IDs, verifies checksums and provenance, and changes only permitted derived state. Bundles never contain people, canonical assignments/rejections or append-only review history.

## Canonical versus regenerable

| Canonical or governed | Derived and regenerable |
|---|---|
| Source, asset and revision identity | Detector observations |
| People | Crops, thumbnails and review proxies |
| Assignments and rejections | Embeddings |
| Append-only assignment/review history | Suggestions and rankings |
| Exact-model identity-suggestion policies and versions | Confidence classification under the current exact-model policy |
| Processing-run/job state | Evaluation manifests and reports |
| Bundle import/provenance records | Portable processing outputs |

Derived data is still biometric or private and must be protected accordingly.

## Provenance requirement

Every derived result must be traceable to the immutable source revision, exact model ID and hash, material preprocessing/alignment contract and processing run or import that produced it.

Every automatic canonical assignment must additionally retain the exact model revision, score and rank-gap evidence plus the policy version and thresholds that promoted it from derived suggestion evidence into canonical history.

See the [Glossary](../glossary.md), [Recognition and identity matching](identity-matching.md), [ADR-0006](../decisions/ADR-0006-canonical-auto-assignment.md), [ADR-0007](../decisions/ADR-0007-permanent-archive-bounded-storage.md) and [SQLite persistence operations](../operations/sqlite-persistence.md).