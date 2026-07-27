# Architecture overview

The Windows computer is the trusted control plane and can run the complete functional system without Azure. It reads the personal OneDrive synchronised folder or another local source, stores the canonical database and derived artefacts, runs model inference, hosts the review application, evaluates model revisions, answers collection queries and creates portable processing bundles.

```text
Personal OneDrive or local folder
        │ Windows sync client / local filesystem
        ▼
Local Windows control plane
        ├── CLI and resumable worker
        ├── API and responsive browser UI
        ├── SQLite canonical database
        ├── crop and artefact store
        ├── identity matcher and review history
        ├── model-lab export and evaluation
        ├── collection queries and neutral exports
        └── bundle packager/importer
                    │ explicit portable bundle
                    ▼
Optional temporary Azure compute
        ├── same worker contract
        ├── ONNX Runtime
        └── temporary input/output only
                    │ result bundle
                    ▼
Local import into canonical database
```

## Data ownership

SQLite on the Windows control plane owns people, immutable photo revisions, face occurrences, model-versioned embeddings and suggestions, plus append-only human review actions. Derived crops and reports can be regenerated; people and human review history cannot be reconstructed safely from model output and therefore remain canonical local data.

Model revisions coexist by explicit model ID and exact hash. Human people and labels are shared across models. A suggestion always identifies the model revision that produced it and never becomes a label automatically.

## Local-first delivery policy

The current delivery phase proves the full workflow locally on approximately 500 images, then repeats the same corpus with an additional model. Azure is optional scale-out and consistency validation. It is deliberately deferred until the local workflow, model comparison and documentation have been accepted.

Azure never authenticates to personal OneDrive and does not own canonical data.

The implementation is a modular monolith with separate executables and infrastructure adapters sharing one application model.
