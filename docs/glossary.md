# Glossary

This glossary defines terms used across Photo Identity Indexer documentation, commands and user interfaces.

## Assignment

A canonical action that identifies one face occurrence as a person. The current runtime creates assignments through human review; ADR-0006 permits an explicitly enabled High-confidence automatic policy to create assignments once WI-0043 is implemented. Active assignments are canonical identity data. Later undo or correction is recorded through append-only history.

## Asset

The catalogue identity of one source photo or supported media item. An asset can be observed at different paths or times while retaining a stable internal identifier.

## Asset revision

An immutable observed content version of an asset. Processing, face occurrences and collection results refer to a revision so changed file content cannot silently reuse older derived results. Authoritative bytes, not lightweight file metadata alone, establish immutable revision identity.

## Bundle

A portable, checksummed package used to move explicitly selected processing inputs or derived results between the trusted Windows control plane and another worker. A bundle contains neutral identifiers and model provenance; it does not grant access to OneDrive or the canonical catalogue.

## Canonical data

Data whose loss cannot be repaired safely by rerunning a model. In this system it includes people, active assignments and rejections, append-only assignment/review history, source/revision identity and governed processing records. A future governed automatic assignment is canonical because an explicit policy promotes the decision into history; its underlying suggestion and embedding remain derived.

## Catalogue

The local SQLite database plus the referenced governed artefact state used by Photo Identity Indexer. It records sources, assets, immutable revisions, faces, people, assignment/review history, exact-model derived data and processing state. It is sensitive application data, not a disposable cache.

## Collection

A query result containing photos that match one or more selected people under explicit any/all, review-state and optional exact-suggestion policies. The browser presents collections locally; neutral manifests expose opaque HTTP resource URLs without filesystem paths.

## Derived artefact

A result that can be regenerated from canonical inputs and exact provenance, such as an aligned crop, embedding, suggestion, review proxy, export or evaluation report. Derived does not mean public or non-sensitive.

## Embedding

A model-produced numeric vector representing one aligned face crop. Embeddings are keyed by face occurrence and exact embedder revision. Vectors from different model revisions must not be compared as though they shared one score scale.

## Exemplar

An actively assigned face used as positive identity evidence for matching under one exact embedding revision. The current implementation uses human-assigned exemplars. After WI-0043, eligible automatic assignments may become exemplars in later regeneration runs; a newly automatic assignment never feeds back into the same regeneration run that created it.

## Face occurrence

The stable catalogue identity of a face within one immutable asset revision. Detector observations, crops, embeddings, suggestions and assignment/review actions attach to this identity.

## Intersection over union (IoU)

A measure of how much two bounding boxes overlap. It is calculated as the area shared by both boxes divided by the total area covered by either box: `intersection area / union area`. The result ranges from `0`, meaning no overlap, to `1`, meaning identical boxes. Detector comparison uses an IoU threshold to decide which candidate and ground-truth boxes may belong to the same face; the default threshold `0.50` requires their intersection to cover at least half of their combined union area.

## Manifest

A structured document that declares content and provenance. Model manifests pin model files and preprocessing contracts; evaluation manifests pin datasets and splits; collection manifests describe path-free photo results; bundle manifests describe portable inputs or outputs.

## Model ID

A stable human-readable identifier for a governed model configuration, such as `sface-2021dec-fp32`. It is necessary but not sufficient to identify exact bytes.

## Model revision

The exact model identity used to produce derived results: model ID, SHA-256 hash and material preprocessing/runtime contract. A change to weights, quantisation, alignment, dimensions or material preprocessing creates a different revision.

## Observation

A model-versioned measurement attached to a stable catalogue object. A detector observation can contain confidence, bounding box and landmarks without replacing the face occurrence or its canonical assignment/review state.

## Person

A canonical human-maintained identity with a stable internal ID and display name. People are shared across model revisions and remain local to the trusted control plane.

## Processing run

A persisted batch configuration and its jobs. It records selected model IDs and operational state so interrupted work can resume without silently changing models or scope.

## Rejection

A human review action stating that a face is not the selected person, or that a face should remain rejected from assignment. Rejected face-person evidence is preserved and prevents the same advisory pair from being proposed again under the governed matching rules.

## Review action

An append-only canonical decision such as assignment, rejection, undo, rename or merge. Most current review actions are human. Future automatic assignment is recorded as an explicitly attributed canonical action rather than hidden model state. Current state is derived from active history.

## Review proxy

A versioned derived image retained locally for normal browsing and review context while the authoritative original can remain online-only in OneDrive. A review proxy never replaces source identity or the immutable original revision.

## Source

A configured local filesystem boundary containing photos. Personal OneDrive is represented through the Windows synchronisation client as a local source; the application does not use Microsoft Graph credentials. The permanent archive is represented by one stable root plus normalized relative included folders.

## Suggestion

Derived identity evidence generated from one exact embedding-model revision. Suggestions carry scores, ranking/margin evidence and provenance. A suggestion is not itself canonical. The current runtime requires human review to create assignments; WI-0043 will allow a qualifying High suggestion to create a separate canonical automatic assignment when the explicit policy is enabled.

## Unknown

A planned face-review state for a real person whose identity is not currently known. Unknown is not a synthetic Person identity, is not positive exemplar evidence and does not satisfy a person-based collection until the face is later assigned. This state is planned under WI-0047.

## Thumbnail

A bounded server-generated preview used by the local collection browser. For permanent archive operation, durable versioned review proxies are preferred for normal browsing so original source files need not be hydrated.

## Trusted control plane

The Windows computer that owns the canonical catalogue, people, assignment/review history, local artefacts, model installation, API and browser host. Optional remote compute receives explicit portable bundles only and never becomes the canonical owner.
