# Applications

Photo Identity Indexer is one modular system with several executable entry points. The Windows computer can run the complete local workflow; optional remote compute uses the same processing contracts through portable bundles.

## `PhotoIdentity.Cli`

The PowerShell-oriented command-line application for repeatable local operations:

- initialize and use the SQLite catalogue;
- scan local or OneDrive-synchronised folders;
- start, inspect and resume persisted processing runs;
- select exact detector and embedder model IDs;
- regenerate exact-model ranked suggestions;
- export and evaluate reviewed datasets deterministically;
- create and import portable bundles; and
- report processing status and failures.

The CLI orchestrates work. Long-running image decoding and inference are performed through application and adapter services rather than embedded in documentation-only scripts.

## `PhotoIdentity.Worker`

A headless .NET processing application for portable job bundles.

It:

- validates bundle manifests and checksums;
- validates exact model revisions;
- decodes supported media;
- detects, aligns and embeds faces;
- records timings, errors and checkpoints; and
- writes a checksummed result bundle.

The worker can run locally or on temporary Azure compute. It does not connect to OneDrive, open the canonical SQLite catalogue, or receive people and human review history.

## `PhotoIdentity.Api`

The ASP.NET Core host for the trusted local application boundary.

It provides endpoints for:

- review queues and progress;
- face crops and photo previews;
- people, assignment, rejection, undo, rename and merge operations;
- audit and person-maintenance views;
- exact-model suggestion filters;
- collection-ready photo queries;
- bounded collection thumbnails and original-content streaming; and
- the versioned neutral collection manifest.

The API reads local canonical and derived state. Heavy batch inference must not run inside interactive HTTP requests.

## `PhotoIdentity.Web`

A responsive hosted Blazor WebAssembly application for Windows and Pixel browsers.

It supports:

- continuous and paged face review;
- individual and preview-first grouped decisions;
- exact-model ranked suggestions;
- person creation, correction, rename, merge and audit;
- review-state and model-revision progress filters; and
- person-based collection browsing with any/all semantics and fixed thumbnails.

The application is unauthenticated and is intended only for localhost or a trusted private network.

## Verification and governance tools

- **`PhotoIdentity.Docs`** validates canonical delivery registries and generated status documents.
- **`PhotoIdentity.Models`** supports pinned model installation and verification workflows.
- **`PhotoIdentity.ReviewVerification`** exercises the published local review application with disposable fixtures and privacy-boundary assertions.
- **`Invoke-MultiModelComparison.ps1`** coordinates fixed-scope, resumable exact-model comparisons and private evidence generation.
- **`tools/model-lab`** remains an optional isolated Python workspace for conversion or analysis when Python is materially better. It exchanges documented neutral files and does not own canonical data.

## Shared operational rule

Executables may share application contracts and infrastructure adapters, but only the trusted Windows control plane owns the canonical catalogue and human review history. Optional workers receive explicit portable inputs and return derived outputs for validated import.
