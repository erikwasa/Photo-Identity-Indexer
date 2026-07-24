# Canonical data model

## Assets and revisions

`Asset` records source identity, current relative path, file metadata, availability and deletion state. `AssetRevision` records a stable revision fingerprint and optional content hash. Detections belong to a revision so changed files cannot silently reuse old results.

Without Graph item IDs, the source adapter initially uses source root, relative path, size, last-write time and optional content hashes. Internal `AssetId` values remain canonical after a move is reconciled.

## Face occurrences and observations

`FaceOccurrence` is the stable object that can receive a human label. Detector-specific `DetectionObservation` rows record model, run, bounding box, landmarks, confidence and quality properties.

This separation permits another detector to create new observations without destroying labels.

## Crops and embeddings

`FaceCrop` records the crop artefact, alignment protocol, dimensions, hash and padding policy. Retain at least one generously padded crop for review and future reprocessing.

`FaceEmbedding` is keyed by face occurrence, model and crop. Store little-endian `float32` vectors initially. Never compare embeddings from different model IDs.

## People and labels

`Person` stores a stable ID and display metadata. `FaceLabel` stores confirmed, rejected or disputed canonical human assignments.

Automatic `IdentitySuggestion` rows are disposable and model-versioned. `RejectedIdentity` preserves negative evidence.

## Operational records

Track model definitions, processing runs, jobs, attempts, source scans, checkpoints, evaluation datasets and runs, job bundles and result imports.

Every derived result must be traceable to model hash, configuration, code version and processing run.
