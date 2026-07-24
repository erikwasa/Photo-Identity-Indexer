# Applications

## `PhotoIdentity.Cli`

Administration and repeatable batch operations: database setup, source scanning, staging, local processing, evaluation, bundle creation, result import, status and local web startup.

## `PhotoIdentity.Worker`

A headless .NET process that reads a portable bundle, validates models, decodes images, detects and aligns faces, creates embeddings, checkpoints work and writes a result bundle. It runs locally or on temporary Azure compute and never connects to OneDrive.

## `PhotoIdentity.Api`

An ASP.NET Core API for review queues, face crops, photo previews, people, confirmations, rejections, processing state and person-based queries. Heavy inference must not run inside HTTP requests.

## `PhotoIdentity.Web`

A responsive Blazor WebAssembly PWA for Windows and Pixel browsers. Initial screens cover suggestions, unknown faces, people, photo details, progress and failures.

## `tools/model-lab`

An optional Python workspace for model conversion, experimental models, clustering and statistical analysis. It consumes and produces documented neutral files.
