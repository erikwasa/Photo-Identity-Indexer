# Applications

Photo Identity Indexer is one modular system with several executable entry points. The Windows computer can run the complete local workflow; optional portable processing uses the same neutral bundle contracts.

## `PhotoIdentity.Cli`

The PowerShell-oriented command-line application for repeatable local operations:

- run PostgreSQL-backed catalogue workflows such as detector rollout, metadata enrichment and bounded evaluation experiments;
- measure archive proxy profiles without catalogue writes;
- decode and inspect media with the governed imaging/recognition stack;
- process portable job bundles without database access; and
- report deterministic command results and failures.

SQLite-opening migration, backup, archive, batch, match, evaluation-export and bundle import/export commands were retired under WI-0149 after PostgreSQL became the sole supported catalogue. Historical migration/cutover evidence remains in delivery and operations records rather than executable compatibility paths.

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

The worker runs on maintainer-controlled local hardware. It does not connect to OneDrive, open the canonical PostgreSQL catalogue, or receive people and human review history.

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

The API reads local canonical and derived state from PostgreSQL-backed application services. Heavy batch inference must not run inside interactive HTTP requests.

## `PhotoIdentity.Web`

A responsive hosted Blazor WebAssembly application for Windows and supported phone browsers.

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
- **`PhotoIdentity.ReviewVerification`** exercises the published local review application with disposable PostgreSQL fixtures and privacy-boundary assertions.
- **`Invoke-MultiModelComparison.ps1`** coordinates fixed-scope, resumable exact-model comparisons and private evidence generation.
- **`tools/model-lab`** remains an optional isolated Python workspace for conversion or analysis when Python is materially better. It exchanges documented neutral files and does not own canonical data.

## Shared operational rule

Executables may share application contracts and infrastructure adapters, but only the trusted Windows control plane owns the canonical PostgreSQL catalogue and human review history. Portable workers receive explicit neutral inputs and return derived outputs for validated downstream use.
