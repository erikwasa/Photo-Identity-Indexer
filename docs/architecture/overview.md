# Architecture overview

The Windows computer is the trusted control plane. It reads the personal OneDrive synchronised folder, stores the canonical database and derived artefacts, hosts the local review application, creates portable processing bundles and imports results.

```text
Personal OneDrive
        │ Windows sync client
        ▼
Local Windows control plane
        ├── CLI and worker
        ├── API and browser UI
        ├── SQLite canonical database
        ├── crop and artefact store
        └── bundle packager/importer
                    │ explicit portable bundle
                    ▼
Optional temporary Azure compute
        ├── same worker contract
        ├── ONNX Runtime
        └── temporary input/output
                    │ result bundle
                    ▼
Local import into canonical database
```

Azure never authenticates to personal OneDrive and does not own canonical data.

The initial implementation is a modular monolith with separate executables and infrastructure adapters sharing one application model.
