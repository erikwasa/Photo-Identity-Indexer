# Architecture overview

Photo Identity Indexer is a local-first modular monolith. The Windows computer is the trusted control plane and can run the complete functional system without Azure.

It owns the canonical SQLite catalogue, people, human review history and local derived artefacts; installs and verifies exact model revisions; runs resumable processing; hosts the API and responsive browser application; evaluates model revisions; answers person-based collection queries; and creates or imports portable processing bundles.

```text
Personal OneDrive or local folder
        │ Windows sync client / local filesystem
        ▼
Trusted Windows control plane
        ├── PhotoIdentity.Cli
        │     scanning, processing, matching, evaluation and bundles
        ├── PhotoIdentity.Api + PhotoIdentity.Web
        │     review, people, progress and collections
        ├── SQLite canonical catalogue
        │     sources, revisions, people, review history and run state
        ├── governed artefact storage
        │     crops, embeddings, reports and temporary publish output
        └── exact model manifests and installed ONNX files
                    │
                    │ optional explicit job bundle
                    ▼
Temporary local or Azure worker
        ├── validates checksums and exact model revisions
        ├── decodes media and runs detector/embedder inference
        ├── checkpoints disposable processing work
        └── writes a checksummed result bundle
                    │
                    │ explicit result import
                    ▼
Trusted Windows control plane
```

Azure is optional scale-out. It never authenticates to personal OneDrive, opens the canonical SQLite database, or owns people and human review history.

## Runtime applications

- **`PhotoIdentity.Cli`** orchestrates local scans, resumable batch work, exact-model suggestion regeneration, evaluation export/evaluation, portable bundles and administration.
- **`PhotoIdentity.Worker`** performs headless bundle processing locally or on temporary compute without access to canonical identity data.
- **`PhotoIdentity.Api`** hosts local review, people, audit, progress, photo delivery and collection endpoints.
- **`PhotoIdentity.Web`** is the responsive Blazor application used from Windows and a Pixel on a trusted private network.

See [Applications](applications.md) for executable responsibilities and [Module boundaries](module-boundaries.md) for project dependencies.

## Data ownership

Canonical local data includes:

- source, asset and immutable revision identity;
- people and active human assignments/rejections;
- append-only review and person-maintenance history;
- processing-run and job state needed for safe resume; and
- governed import and provenance records.

Derived, replaceable data includes:

- detector observations;
- aligned crops;
- model-versioned embeddings;
- ranked identity suggestions;
- collection thumbnails;
- portable processing outputs; and
- evaluation exports and reports.

Derived artefacts remain sensitive even when they are replaceable. They must not be committed or exposed outside the trusted boundary.

See [Canonical data model](data-model.md) and the [Glossary](../glossary.md).

## Immutable revisions and model provenance

A changed photo creates a new asset revision. Detection, crops and embeddings attach to immutable revision and face-occurrence identities so old results cannot silently be reused for changed content.

A model revision is identified by its model ID, exact SHA-256 hash and material preprocessing contract. Baseline and candidate embeddings can coexist in one catalogue. Suggestions and evaluation reports always identify the exact revision that produced them.

Scores from different revisions are not assumed to share one threshold or distribution.

## Human review boundary

People and human decisions are canonical. Assignments, rejections, undo, rename and merge operations are recorded as governed history. Current state is derived without treating model output as an authoritative label.

Identity suggestions are advisory derived data:

- only human-confirmed faces are exemplars;
- rejected face-person evidence is preserved;
- suggestions are exact-model scoped and regenerable; and
- no score or threshold creates a label automatically.

See [Recognition and identity matching](identity-matching.md).

## Collection boundary

The local collection API provides explicit any-person or all-person semantics with confirmed-only results as the safe default. Advisory evidence requires an exact model revision and threshold.

The browser uses bounded server-generated thumbnails. The versioned neutral collection manifest contains opaque IDs and HTTP resource URLs, not source roots, source keys, crop paths or filenames.

## Portable compute boundary

Portable job bundles contain explicitly selected neutral inputs, exact model manifests and checksums. Result bundles contain derived processing results and checkpoints. Import validates provenance, revision identity and checksums before changing local derived state.

The worker cannot access OneDrive credentials, people, assignments, rejections or the canonical database. See [Portable processing bundles](portable-bundles.md).

## Deployment and trust

The browser application is unauthenticated and is intended for localhost or a trusted private network only. The SQLite catalogue stays on local disk rather than a network share or synchronised cloud folder.

Original photos are read-only inputs. The system does not modify them.

See [Security and privacy](security-and-privacy.md) and the [Local operator guide](../operations/local-operator-guide.md).
