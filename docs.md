# Photo Identity Indexer

## Architecture and Incremental Development Plan — Revision 2

**Primary language:** C# on .NET 10
**Secondary language:** Python only where materially better
**Photo source:** Personal OneDrive
**Cloud compute:** Azure subscription with approximately $50 monthly credit
**Identity constraint:** Enterprise policies prevent creating app registrations and identities in the Azure tenant
**Initial objective:** Detect and identify people in photographs while retaining ownership of all detections, embeddings, labels and evaluation data

---

# 1. Purpose

We are building a private, model-independent face-indexing system for a personal photo archive.

The system will:

1. Discover photographs from a local OneDrive-synchronised folder.
2. Detect faces in photographs.
3. Create reusable face crops.
4. Generate embeddings using replaceable models.
5. Let the user associate faces with named people.
6. Suggest identities for unlabelled faces.
7. Record confirmations and rejections.
8. Evaluate multiple models on the same labelled dataset.
9. Process the full archive only after selecting a suitable model.
10. Make the resulting photo-person relationships available to future applications.

Future applications may create:

- Albums
- Collections
- Slideshows
- Multi-person searches
- Additional image tags
- Date- or event-based selections

Those features are not part of the first version, but the architecture must support them.

---

# 2. Identity and Tenant Constraints

## 2.1 Personal OneDrive and Azure are separate systems

The photo library belongs to a personal Microsoft account.

The Azure subscription is governed by an enterprise tenant with policies that restrict:

- Creating app registrations
- Creating service principals
- Creating managed identities
- Creating other application identities
- Granting arbitrary Microsoft Graph permissions

The design must therefore not rely on:

- An Azure-hosted application authenticating to personal OneDrive
- A Microsoft Graph application registered in the enterprise tenant
- A managed identity reading OneDrive
- A service principal moving data between OneDrive and Azure
- An Azure Function or Container App receiving delegated OneDrive credentials
- Tenant administrators approving permissions

## 2.2 Revised trust boundary

The Windows development computer is the trusted bridge between:

- Personal OneDrive
- The local canonical database
- Optional Azure batch processing

Azure receives only explicitly prepared processing bundles.

```text
Personal OneDrive
        │
        │ OneDrive Windows sync client
        ▼
Local Windows computer
        │
        ├── Canonical database
        ├── Review application
        ├── Model evaluation
        ├── Job packaging
        └── Azure upload/download control
                    │
                    │ Explicit job bundles
                    ▼
             Temporary Azure compute
                    │
                    │ Result bundles
                    ▼
Local Windows computer
```

Azure does not need to know:

- The personal Microsoft account password
- The OneDrive OAuth token
- The complete OneDrive directory structure
- The canonical identity database
- Human identity labels beyond what a particular job requires

## 2.3 No Azure-side OneDrive connector

The earlier proposal for an Azure worker to access OneDrive directly is removed.

The Azure worker will not:

- Call Microsoft Graph
- Authenticate to OneDrive
- Store Microsoft account tokens
- Follow OneDrive download links
- Perform OneDrive delta synchronisation
- Scan OneDrive folders

All photo acquisition happens locally.

## 2.4 No custom Azure application identity

The first version will not create:

- Microsoft Entra application registrations
- Service principals
- Managed identities
- Workload identities
- Federated credentials
- Client secrets
- Client certificates

Azure operations will be initiated from the developer’s Windows computer using the user’s existing interactive Azure access.

Where an Azure worker needs temporary storage access, it will use either:

1. A short-lived, narrowly scoped SAS token created interactively by the user, or
2. Files copied directly to the VM using SSH/SCP, avoiding Blob Storage credentials entirely.

No permanent cloud identity is required.

---

# 3. Definition of the First Working Version

The first working version is a local vertical slice.

It will:

1. Read a representative local folder containing approximately 500–3,000 photos.
2. Detect faces.
3. Save padded and aligned face crops.
4. Generate SFace embeddings.
5. Store metadata in SQLite.
6. Present faces in a local browser-based review interface.
7. Let the user create named people.
8. Let the user assign confirmed face examples.
9. Suggest identities for other faces.
10. Let the user confirm or reject suggestions.
11. Produce an initial accuracy and performance report.
12. Run entirely on a Windows development computer.

The first version will not initially require:

- Azure
- Microsoft Graph
- Azure identity configuration
- A GPU
- A public web application
- A cloud database
- Full processing of the 250GB archive

This isolates the important product question:

> Can the selected face-detection and embedding models identify the important people accurately enough in this specific photo archive?

---

# 4. Success Criteria

The central hypothesis is validated when:

- At least five important people can be created.
- Each person can be enrolled with several confirmed examples.
- The system can suggest identities for previously unseen faces.
- High-confidence suggestions achieve approximately 99% precision on the reviewed evaluation sample.
- Incorrect suggestions can be rejected.
- Rejected suggestions are not immediately repeated.
- Adding confirmed examples improves later suggestions.
- A stopped processing run can resume.
- Reprocessing does not create duplicate assets, faces or embeddings.
- A second model can be added without changing canonical people or labels.
- Model-specific embeddings can be deleted and regenerated.
- Original photos remain unchanged.
- The same local processing bundle can run locally or in Azure.
- Azure processing requires no application identity.

The precision target may be adjusted after real measurements.

Recall is secondary. An unidentified face is preferable to a confidently incorrect identity.

---

# 5. Non-Goals for the Initial Version

The initial version will not:

- Modify files in OneDrive.
- Write people metadata into image files.
- Train or fine-tune a neural network.
- Identify public figures.
- Use Azure Face API.
- Use Amazon Rekognition.
- Use hosted embeddings that cannot be exported.
- Automatically confirm identity suggestions.
- Process videos.
- Build slideshows.
- Build a public cloud-hosted photo gallery.
- Synchronise several simultaneous database writers.
- Run the review application permanently in Azure.
- Depend on Microsoft Graph.
- Depend on Azure tenant administrators.

---

# 6. Architectural Principles

## 6.1 The local system owns canonical data

The canonical system of record is local.

It owns:

- Assets
- Asset revisions
- Face occurrences
- Face crops
- Model definitions
- Embeddings
- Named people
- Confirmed labels
- Rejected labels
- Suggestions
- Evaluation datasets
- Evaluation results
- Processing history

Azure result bundles are imported into this canonical store.

Azure is never the only location containing:

- Human labels
- People records
- Evaluation decisions
- Model manifests
- Embeddings
- Processing results

## 6.2 Azure is disposable compute

An Azure processing environment can be destroyed without losing project data.

The Azure worker:

- Receives a finite input bundle.
- Processes the bundle.
- Produces a finite result bundle.
- Uploads or exposes the results.
- Is deallocated or deleted.

It does not require access to the full application database.

## 6.3 Models are replaceable adapters

The application must not depend directly on a specific recognition model.

Models implement narrow contracts such as:

```csharp
public interface IFaceDetector
{
    ModelDescriptor Descriptor { get; }

    Task<IReadOnlyList<DetectedFaceCandidate>> DetectAsync(
        DecodedImage image,
        CancellationToken cancellationToken);
}

public interface IFaceEmbedder
{
    ModelDescriptor Descriptor { get; }

    Task<EmbeddingVector> EmbedAsync(
        AlignedFace face,
        CancellationToken cancellationToken);
}
```

Changing an embedding model may require generating new embeddings, but must not alter:

- Named people
- Confirmed face labels
- Rejected face-person pairs
- Asset records
- Future album definitions

## 6.4 Human labels are canonical

A human confirmation belongs to a stable face occurrence.

It does not belong to:

- An embedding vector
- A cluster
- A model-specific face ID
- An Azure processing run

Embeddings and clusters are replaceable derived data.

## 6.5 Modular monolith first

The solution will contain well-separated modules but will initially deploy as a small number of processes.

This avoids premature microservices while preserving replaceable boundaries.

## 6.6 C# is the default language

C# will be used for:

- Local source discovery
- Job packaging
- Image-processing orchestration
- ONNX inference
- Database access
- Identity matching
- APIs
- Review UI
- Azure job execution
- Import and export

Python may be used for:

- Candidate models without practical C# support
- Model conversion to ONNX
- Experimental clustering
- Statistical analysis
- Notebook-based model comparison

Python tools must communicate through documented portable files.

## 6.7 No original photo modification

The system is read-only with respect to the photo archive.

All derived data is stored separately.

---

# 7. Applications

## 7.1 `PhotoIdentity.Cli`

The main administration and development application.

Responsibilities:

- Initialise and migrate the database.
- Register local photo sources.
- Scan the OneDrive-synchronised folder.
- Inspect OneDrive Files On-Demand state.
- Hydrate selected files when needed.
- Queue local processing.
- Run local processing.
- Create evaluation datasets.
- Package Azure jobs.
- Upload and download Azure bundles.
- Import Azure results.
- Run model evaluations.
- Generate reports.
- Inspect failures.
- Start the local API and review UI.

This is the first executable to build.

## 7.2 `PhotoIdentity.Worker`

A headless .NET worker.

Responsibilities:

- Read a portable job bundle.
- Validate model files and checksums.
- Decode images.
- Detect faces.
- Save padded face crops.
- Align faces.
- Generate embeddings.
- Write checkpoints.
- Produce result bundles.
- Exit cleanly when complete.

The same worker runs:

- Locally on Windows
- In a local Docker container
- On an Azure CPU VM
- On an Azure GPU VM

The worker does not connect to OneDrive.

## 7.3 `PhotoIdentity.Api`

A local ASP.NET Core API.

Responsibilities:

- Serve review queues.
- Serve face crops and photo previews.
- Create and edit people.
- Confirm and reject identities.
- Query processing state.
- Query photos by person.

Initially, the API runs on the development computer.

## 7.4 `PhotoIdentity.Web`

A responsive Blazor WebAssembly progressive web application.

It will work from:

- The Windows computer
- A Google Pixel on the same trusted network
- Other modern browsers

Initial pages:

- Suggested identity review
- Unknown faces
- Person management
- Photo detail
- Processing status
- Failure inspection

Permanent internet hosting is deferred.

## 7.5 Optional `tools/model-lab`

A Python workspace for:

- Experimental models
- Statistical analysis
- HDBSCAN or other clustering algorithms
- Model conversion
- Notebook-based inspection

It operates on exported datasets and bundles.

---

# 8. Revised High-Level Architecture

```text
┌────────────────────────────────────────────┐
│ Personal OneDrive                         │
│                                            │
│ Managed by Microsoft OneDrive sync client │
└────────────────────┬───────────────────────┘
                     │
                     │ Local filesystem
                     ▼
┌────────────────────────────────────────────┐
│ Local Windows control plane                │
│                                            │
│ PhotoIdentity.Cli                          │
│ PhotoIdentity.Api                          │
│ PhotoIdentity.Web                          │
│ SQLite canonical database                  │
│ Local crop/artifact store                  │
│                                            │
│ Local source adapter                       │
│ Local processing worker                    │
│ Azure bundle packager/importer             │
└────────────────────┬───────────────────────┘
                     │
                     │ Explicit portable bundle
                     │ No OneDrive credentials
                     ▼
┌────────────────────────────────────────────┐
│ Optional temporary Azure compute           │
│                                            │
│ PhotoIdentity.Worker                       │
│ ONNX Runtime                               │
│ CPU or GPU                                 │
│ Temporary input and output storage         │
└────────────────────┬───────────────────────┘
                     │
                     │ Result bundle
                     ▼
┌────────────────────────────────────────────┐
│ Local import into canonical database       │
└────────────────────────────────────────────┘
```

---

# 9. OneDrive Access Strategy

## 9.1 Primary approach: Windows OneDrive sync client

The primary photo-source implementation will use the local OneDrive directory created by the official Windows OneDrive sync client.

Authentication to personal OneDrive remains entirely inside that client.

The application sees ordinary or placeholder filesystem entries.

Benefits:

- No app registration
- No Microsoft Graph permissions
- No OAuth implementation
- No Azure tenant dependency
- No personal OneDrive credentials in the application
- Existing year/month structure remains visible
- Renames and moves are naturally reflected locally

## 9.2 Files On-Demand

OneDrive may represent files as local placeholders that are not yet downloaded.

The source adapter must distinguish:

- Fully local files
- Online-only placeholders
- Files currently being downloaded
- Files unavailable because OneDrive is offline
- Files with synchronisation errors

The system must not assume that discovering a filesystem path means the file content is available.

## 9.3 Hydration strategy

The application will support configurable hydration modes.

### Mode A: user-managed hydration

The user manually selects a test folder and chooses **Always keep on this device** in Windows Explorer.

The application processes only files confirmed to be locally available.

This is the first implementation.

### Mode B: application-triggered hydration

The application opens or copies a placeholder file, causing OneDrive to retrieve it.

The process then waits until:

- The file is readable.
- Its expected size is available.
- The copy completes.
- The content hash can be calculated.

This is added after the first version.

### Mode C: staging copy

For reliable batch processing, the application copies hydrated images into a dedicated staging directory.

```text
OneDrive folder
      │
      ▼
Local staging area
      │
      ├── Stable input for processing
      ├── Stable input for Azure bundles
      └── Deleted after verification/import
```

The staging copy prevents problems when:

- A OneDrive file changes during processing.
- A placeholder is de-hydrated.
- The sync client locks the file.
- A file is moved or renamed.
- Azure packaging is interrupted.

## 9.4 Stable source identity

Without Microsoft Graph item IDs, the local source adapter initially uses:

- Source root ID
- Relative path
- File size
- Last modification time
- Optional content hash

A path change may initially look like a deletion and addition.

To improve move detection, the application will use a content fingerprint:

```text
Full SHA-256 for evaluation subsets and changed files
```

For the full library, an optional staged strategy may use:

1. File size
2. Partial content fingerprint
3. Full SHA-256 only when needed

The canonical `AssetId` remains internal and stable after matching a moved file to its prior content fingerprint.

## 9.5 Optional future Microsoft Graph connector

A direct Graph connector is no longer part of the planned core path.

It may be added only if one of these becomes available:

- A separate personal Azure/Entra environment where the user can register an application
- A permitted consumer application registration
- Another approved authentication mechanism

The rest of the architecture must not depend on this future option.

---

# 10. Azure Authentication and Data Transfer

## 10.1 Local interactive Azure access

The developer’s Windows computer may use existing interactive Azure access through tools such as:

- Azure CLI
- Azure PowerShell
- Azure Storage Explorer
- Azure Portal
- AzCopy

This is a control-plane action performed by the user.

It does not create an application identity.

## 10.2 Preferred Azure transfer modes

### Transfer Mode A: direct VM copy

Use SSH/SCP or SFTP to transfer:

- Job bundle to the VM
- Result bundle back from the VM

Advantages:

- No Storage Account identity required by the worker
- No SAS generation
- Simple first Azure pilot
- Easy to understand

Disadvantages:

- Less convenient for interruption recovery
- VM disk must persist until results are downloaded
- Manual transfer is less suitable for large multi-batch runs

This is the preferred first Azure implementation.

### Transfer Mode B: short-lived SAS

A local user creates a short-lived SAS for a specific private Blob container.

The worker receives only the SAS needed to:

- Read one input bundle
- Write one result bundle
- Write checkpoints

The SAS should be:

- Container- or blob-scoped
- Time-limited
- Permission-limited
- Generated only for the active job
- Removed from logs
- Stored only in temporary process configuration

Advantages:

- Better checkpointing
- Better support for long jobs
- VM can be recreated
- Easier batch processing

Disadvantages:

- The SAS is still a bearer credential
- It must be protected carefully
- Enterprise policies may constrain SAS generation

This is introduced only if permitted.

### Transfer Mode C: mounted or attached data disk

A data disk containing a prepared job may be attached to a temporary VM.

This may be useful for large batches but is not an initial priority.

## 10.3 No Azure worker identity

The worker must be capable of running with:

- No Azure SDK
- No Azure login
- No managed identity
- No service principal
- No tenant metadata

Its fundamental interface is:

```text
photoidentity-worker run \
    --input <bundle-path> \
    --output <result-path>
```

Azure-specific launch scripts are wrappers around this portable command.

---

# 11. Repository Structure

```text
PhotoIdentity/
│
├── PhotoIdentity.sln
├── Directory.Build.props
├── Directory.Packages.props
├── global.json
├── README.md
├── BUILD_CONTEXT.md
│
├── src/
│   ├── PhotoIdentity.Core/
│   ├── PhotoIdentity.Persistence.Sqlite/
│   ├── PhotoIdentity.Source.Local/
│   ├── PhotoIdentity.Source.OneDriveSync/
│   ├── PhotoIdentity.Imaging.OpenCv/
│   ├── PhotoIdentity.Recognition.Onnx/
│   ├── PhotoIdentity.Transfer.Bundles/
│   ├── PhotoIdentity.Cli/
│   ├── PhotoIdentity.Worker/
│   ├── PhotoIdentity.Api/
│   └── PhotoIdentity.Web/
│
├── tests/
│   ├── PhotoIdentity.Core.Tests/
│   ├── PhotoIdentity.Persistence.Tests/
│   ├── PhotoIdentity.Source.Tests/
│   ├── PhotoIdentity.Recognition.Tests/
│   ├── PhotoIdentity.Bundle.Tests/
│   └── PhotoIdentity.Integration.Tests/
│
├── tools/
│   └── model-lab/
│
├── models/
│   ├── manifests/
│   └── README.md
│
├── infra/
│   ├── azure/
│   │   ├── bicep/
│   │   ├── powershell/
│   │   ├── ssh/
│   │   └── README.md
│   └── docker/
│
└── docs/
    ├── architecture.md
    ├── data-model.md
    ├── recognition-pipeline.md
    ├── evaluation-method.md
    ├── onedrive-sync-source.md
    ├── azure-without-identities.md
    ├── privacy.md
    ├── operations.md
    ├── decisions/
    └── modules/
```

---

# 12. Module Dependency Rules

```text
Executables and UI
        │
        ▼
Application/Core
        ▲
        │
Infrastructure adapters
```

Mandatory rules:

- Core references no infrastructure project.
- The OneDrive-sync source is a filesystem adapter.
- The Azure infrastructure scripts do not contain recognition logic.
- Recognition adapters do not know whether they run locally or in Azure.
- Bundle transfer code does not depend on SQLite.
- Persistence does not depend on OpenCV or ONNX Runtime.
- The domain exposes no OpenCV, EF Core, Azure SDK or tensor types.
- Python tools consume neutral bundle formats.
- The cloud worker cannot access the canonical database directly.
- No module assumes the existence of Microsoft Graph.

---

# 13. Core Contracts

```csharp
public interface IAssetSource
{
    IAsyncEnumerable<SourceAsset> EnumerateAsync(
        SourceScanOptions options,
        CancellationToken cancellationToken);

    Task<AssetAvailability> GetAvailabilityAsync(
        SourceAssetReference asset,
        CancellationToken cancellationToken);

    Task<Stream> OpenContentAsync(
        SourceAssetReference asset,
        CancellationToken cancellationToken);
}

public interface IAssetStager
{
    Task<StagedAsset> StageAsync(
        SourceAssetReference asset,
        StagingOptions options,
        CancellationToken cancellationToken);
}

public interface IFaceDetector
{
    ModelDescriptor Descriptor { get; }

    Task<IReadOnlyList<DetectedFaceCandidate>> DetectAsync(
        DecodedImage image,
        CancellationToken cancellationToken);
}

public interface IFaceAligner
{
    Task<AlignedFace> AlignAsync(
        DecodedImage image,
        DetectedFaceCandidate detection,
        CancellationToken cancellationToken);
}

public interface IFaceEmbedder
{
    ModelDescriptor Descriptor { get; }

    Task<EmbeddingVector> EmbedAsync(
        AlignedFace face,
        CancellationToken cancellationToken);
}

public interface IJobBundleWriter
{
    Task<JobBundleReference> CreateAsync(
        JobBundleRequest request,
        CancellationToken cancellationToken);
}

public interface IResultBundleImporter
{
    Task<ResultImportSummary> ImportAsync(
        ResultBundleReference bundle,
        CancellationToken cancellationToken);
}
```

No contract should mention:

- OneDrive OAuth
- Microsoft Graph
- Azure managed identity
- Azure tenant IDs
- OpenCV matrices
- ONNX tensors

---

# 14. Canonical Data Model

## Assets

```text
Asset
-----
AssetId
SourceId
SourceItemKey
CurrentRelativePath
FileName
MediaType
SizeBytes
CapturedAt
Width
Height
ContentHash
LastWriteTime
AvailabilityState
LastObservedAt
DeletedAt
```

## Asset revisions

```text
AssetRevision
-------------
AssetRevisionId
AssetId
RevisionFingerprint
ContentHash
CreatedAt
```

## Face occurrences

```text
FaceOccurrence
--------------
FaceOccurrenceId
AssetRevisionId
CanonicalBoundingBox
CreatedAt
RetiredAt
```

## Detection observations

```text
DetectionObservation
--------------------
DetectionObservationId
FaceOccurrenceId
DetectorModelId
ProcessingRunId
BoundingBox
Landmarks
DetectionConfidence
QualityProperties
```

## Face crops

```text
FaceCrop
--------
FaceCropId
FaceOccurrenceId
AlignmentProtocol
ArtifactKey
ContentHash
Width
Height
PaddingPolicy
CreatedAt
```

## Embeddings

```text
FaceEmbedding
-------------
FaceOccurrenceId
EmbeddingModelId
FaceCropId
Dimensions
DataType
VectorData
VectorNorm
CreatedAt
```

## People

```text
Person
------
PersonId
DisplayName
Notes
CreatedAt
ArchivedAt
```

## Canonical labels

```text
FaceLabel
---------
FaceOccurrenceId
PersonId
Status
Source
CreatedAt
UpdatedAt
```

## Suggestions and negative evidence

```text
IdentitySuggestion
RejectedIdentity
```

## Operational records

```text
ModelDefinition
ProcessingRun
ProcessingJob
ProcessingAttempt
EvaluationDataset
EvaluationRun
SourceScan
SourceCheckpoint
JobBundle
ResultBundleImport
```

---

# 15. Portable Processing Bundles

Cloud independence and tenant restrictions make portable bundles central to the design.

## 15.1 Job bundle

```text
job-bundle/
├── bundle-manifest.json
├── pipeline-config.json
├── assets.ndjson
├── model-manifests/
├── input/
│   ├── asset-000001.jpg
│   ├── asset-000002.heic
│   └── ...
└── checksums.sha256
```

Each asset record includes:

- Internal asset revision ID
- Neutral bundle-relative filename
- Original media type
- Expected content hash
- Capture date when known
- Orientation metadata when needed
- Requested processing steps

The Azure worker does not need original OneDrive paths.

## 15.2 Result bundle

```text
result-bundle/
├── result-manifest.json
├── assets.ndjson
├── detections.ndjson
├── crops/
├── embeddings/
├── errors.ndjson
├── timings.ndjson
├── checkpoints/
└── checksums.sha256
```

## 15.3 Import rules

The local importer must:

1. Verify the result-bundle checksum.
2. Verify the pipeline and model manifests.
3. Match results to known asset revisions.
4. Reject results for changed assets.
5. Import detections and embeddings idempotently.
6. Preserve canonical labels.
7. Record the Azure run and import event.
8. Report partial or corrupt results.

## 15.4 Bundle privacy profiles

The packager will support:

### Full-image bundle

Contains staged originals.

Use when:

- Evaluating detector recall
- Processing small faces
- Comparing full pipelines

### Reduced-image bundle

Contains resized images.

Use when:

- Testing throughput
- Detecting prominent faces
- Reducing transfer size

### Face-crop bundle

Contains only previously detected and aligned faces.

Use when:

- Comparing embedding models
- Reprocessing embeddings
- Evaluating matching
- Avoiding transfer of full family photographs

This will often be the cheapest and most private Azure evaluation mode.

---

# 16. Recognition Strategy

## Baseline detector

YuNet through ONNX Runtime.

## Baseline embedder

SFace through ONNX Runtime.

## Initial matching

Use exact cosine similarity against human-confirmed examples.

```text
candidate score =
    maximum cosine similarity to confirmed exemplars
```

Also record:

- Best person
- Best score
- Second-best person
- Second-best score
- Score margin
- Face quality
- Model ID

## Human confirmation rules

During early versions:

- Only human-confirmed faces become exemplars.
- Automatic suggestions never become exemplars.
- Rejected face-person pairs are retained.
- Suggestions are disposable derived data.
- Confirmed labels survive model changes.

## Improvement over time

Recognition improves through:

- More confirmed examples
- Better age coverage
- Better pose coverage
- Negative evidence
- Person-specific thresholds
- Prototype selection
- Reprocessing unknown faces
- Better models

Deep-model fine-tuning remains deferred.

---

# 17. Model Evaluation Plan

## Evaluation subset

Start with approximately:

- 1,000–3,000 photos
- 3,000–10,000 labelled faces
- Five or more frequently appearing people
- Unknown people
- Group photos
- Small faces
- Low-light images
- Side profiles
- Children across different ages
- Similar-looking relatives
- Old or scanned photos

## Detector evaluation

Measure:

- Face recall
- False detections
- Recall by face size
- Recall by photo category
- Runtime
- Memory
- CPU versus GPU performance

## Embedding evaluation

Measure:

- Same-person similarity distribution
- Different-person similarity distribution
- Top-one precision
- Recall
- Unknown-person false acceptance
- Confusion between relatives
- Performance across age gaps
- Precision at proposed confidence thresholds
- Runtime per face

## Evaluation execution

Evaluation should normally proceed in this order:

1. Detect faces locally once.
2. Review and correct face occurrences.
3. Store padded face crops.
4. Package face crops for candidate embedding models.
5. Run candidate models locally or in Azure.
6. Import embeddings.
7. Evaluate all models against identical labels.

This avoids repeatedly transferring full photographs.

## Production gate

Do not process the complete archive until:

- The detector is acceptable.
- A preferred embedding model is selected.
- High-confidence precision is satisfactory.
- Unknown-person rejection is satisfactory.
- Difficult relatives have been inspected.
- Throughput is measured.
- Azure cost is projected.
- Model licences are recorded.
- Local and Azure results are sufficiently consistent.

---

# 18. Azure Execution Strategy

## Phase 1: no Azure

All early development and validation runs locally.

Goals:

- Verify architecture
- Verify model integration
- Verify accuracy
- Verify bundle format
- Measure CPU performance

## Phase 2: manual temporary VM pilot

Provision:

- One temporary VM
- One managed OS disk
- Optional temporary data disk
- Network access restricted as far as practical
- SSH access
- No managed identity

Workflow:

1. Build the Linux worker container locally or in an allowed registry.
2. Create a small job bundle locally.
3. Copy the bundle to the VM using SCP.
4. Run the worker.
5. Copy the result bundle back.
6. Import locally.
7. Deallocate the VM.
8. Record actual cost.

## Phase 3: repeatable Azure scripts

Add PowerShell scripts for:

- Creating the VM
- Starting the VM
- Retrieving its address
- Copying the job
- Launching the worker
- Copying results back
- Deallocating the VM
- Deleting temporary disks
- Showing resource status

The scripts may use the user’s interactive Azure CLI context.

They must not create or depend on service principals.

## Phase 4: Blob-based checkpointing

Only if permitted, use a private Storage Account with job-scoped SAS access.

The worker still has no identity.

The local computer:

1. Uploads input.
2. Creates limited SAS access.
3. Starts the worker.
4. Downloads results.
5. Revokes or allows SAS expiry.
6. Deletes temporary data.

## Phase 5: full archive batches

The archive is divided into bounded batches.

Each batch has limits such as:

- Maximum photo count
- Maximum input bytes
- Maximum estimated runtime
- Maximum face count
- Maximum Azure spend estimate

Processing may be spread over several monthly credit periods.

---

# 19. Azure Cost Controls

Before any Azure processing:

1. Keep the subscription spending limit enabled where available.
2. Create a budget for the project resource group.
3. Configure cost alerts.
4. Tag all resources.
5. Keep the VM deallocated by default.
6. Avoid unnecessary public IP resources.
7. Use the smallest suitable disk.
8. Set a maximum worker runtime.
9. Set a maximum bundle size.
10. Require an explicit command for every upload.
11. Require an explicit command for a full archive run.
12. Automatically produce a cost projection from every pilot.
13. Reserve part of the $50 credit for disks, storage and mistakes.
14. Prefer face-crop bundles for model evaluation.
15. Delete temporary cloud data after successful import.

The local application will not assume the ability to query Azure billing programmatically through an application identity.

Cost may be entered manually from the Azure portal or collected through commands running under the interactive user session.

---

# 20. Incremental Development Milestones

## Milestone 0: repository and architecture

### Deliverables

- .NET 10 solution
- Project structure
- Dependency rules
- Initial ADRs
- Model licence register
- `BUILD_CONTEXT.md`
- Test projects
- Build scripts

### Acceptance criteria

- Build and tests succeed.
- Core has no infrastructure dependency.
- No personal photos or model binaries are committed.

---

## Milestone 1: single-image inference

Command:

```text
photoid inspect <image-path>
```

It will:

- Decode JPEG or PNG.
- Normalise orientation.
- Run YuNet.
- Save annotated output.
- Save padded face crops.
- Align faces.
- Run SFace.
- Save embeddings.
- Save timings and model metadata.

### Acceptance criteria

- Detection boxes are visually correct.
- Face crops are correctly oriented.
- Embeddings are reproducible within tolerance.
- CPU inference works on Windows.

---

## Milestone 2: local catalogue and jobs

### Deliverables

- SQLite schema
- Local folder source
- Recursive scanner
- Processing jobs
- Crop storage
- Embedding storage
- Resume and retry

### Acceptance criteria

- A 500-photo folder can be indexed.
- Processing resumes after interruption.
- Reruns are idempotent.
- Changed files create new revisions.

---

## Milestone 3: OneDrive-synchronised source

### Deliverables

- Source adapter for the local OneDrive folder
- Placeholder detection
- Availability reporting
- User-managed hydration mode
- Staging-copy workflow
- Content fingerprinting
- Move and duplicate heuristics

Commands:

```text
photoid source add-onedrive-sync <folder>
photoid source scan
photoid source availability
photoid source stage
```

### Acceptance criteria

- Online-only files are distinguished from local files.
- Locally hydrated files can be staged.
- The application does not request OneDrive credentials.
- A source file is verified before processing.
- Temporary staged files can be safely removed.

---

## Milestone 4: minimal review application

### Deliverables

- ASP.NET Core API
- Blazor PWA
- Face gallery
- Person creation
- Manual face labelling
- Rejection
- Undo
- Photo detail

### Acceptance criteria

- The application works on Windows.
- It can be opened from the Pixel on a trusted local network.
- Labels persist after restart.

---

## Milestone 5: identity matching

### Deliverables

- Cosine matcher
- Confirmed exemplar selection
- Candidate ranking
- Score margin
- Suggestions
- Rejection filtering
- Rematching

### Acceptance criteria

- Confirmed examples produce suggestions.
- Rejected pairs are not repeated.
- Suggestions do not become canonical labels automatically.
- Adding examples can improve later suggestions.

This is the first useful complete product slice.

---

## Milestone 6: evaluation harness

### Deliverables

- Evaluation datasets
- Gallery, validation and test splits
- Detector metrics
- Identity metrics
- Threshold sweep
- Confusion reports
- Throughput reports
- Cost projection inputs

### Acceptance criteria

- Baseline results are reproducible.
- Validation and test data remain separate.
- Reports identify model hashes and pipeline versions.

---

## Milestone 7: portable job bundles

### Deliverables

- Job-bundle writer
- Result-bundle writer
- Result importer
- Checksums
- Idempotency
- Full-image, reduced-image and crop-only profiles

### Acceptance criteria

- A local worker processes a bundle without database access.
- Results import into the canonical database.
- Reimporting the same result is harmless.
- Changed asset revisions are rejected.

---

## Milestone 8: second model

### Deliverables

At least one additional embedding model or detector.

### Acceptance criteria

- It runs through the same worker contract.
- Existing labels remain unchanged.
- The evaluation harness compares models.
- Embeddings coexist by model ID.

---

## Milestone 9: Azure VM pilot without identities

### Deliverables

- Linux worker container
- VM provisioning scripts
- SSH configuration
- SCP upload/download scripts
- Runtime limit
- Deallocation script
- Pilot cost report

### Acceptance criteria

- A small bundle runs in Azure.
- No app registration is created.
- No service principal is created.
- No managed identity is assigned.
- No OneDrive credential enters Azure.
- Results match local execution within tolerance.
- The VM is deallocated after completion.

---

## Milestone 10: Azure checkpointing

### Deliverables

Either:

- Durable VM-local checkpoints with safe result retrieval, or
- Private Blob Storage with short-lived SAS access

### Acceptance criteria

- An interrupted job resumes.
- Credentials are not written to logs.
- Temporary cloud data can be deleted.
- The worker still has no permanent identity.

---

## Milestone 11: production-model selection

### Deliverables

- Final model comparison
- Selected detector
- Selected embedder
- Selected thresholds
- Frozen manifests
- Licence record
- Processing profile
- Reprocessing plan

### Acceptance criteria

- Selection uses the private evaluation dataset.
- Accuracy meets the chosen threshold.
- Cost fits the available monthly credit or is divided across months.

---

## Milestone 12: full archive processing

### Deliverables

- Partitioned staging jobs
- Bundle generation
- Monthly processing cap
- Progress tracking
- Failure inventory
- Result imports
- Completeness report

### Acceptance criteria

Every eligible asset has a known state:

```text
Completed
Not hydrated
Unsupported
Permanently failed
Deleted
Explicitly excluded
```

---

## Milestone 13: ongoing local synchronisation

### Deliverables

- Periodic local OneDrive-folder scans
- Detection of new and changed files
- Hydration queue
- Processing of new photos
- Re-evaluation after new exemplars
- Database backup

### Acceptance criteria

- New OneDrive photos can be discovered without cloud API access.
- Changed files create new revisions.
- Existing labels survive source moves where content can be reconciled.

---

## Milestone 14: collection-ready API

### Deliverables

Queries such as:

```text
All photos containing Alice
All photos containing Alice and Bob
All confirmed photos containing Alice between 2018 and 2022
```

The API must distinguish:

- Any requested person
- All requested people
- Confirmed labels only
- Optional high-confidence suggestions

---

# 21. Revised First Development Tickets

## Ticket 1: create solution skeleton

- Create projects.
- Add central package management.
- Configure nullable reference types.
- Add build and test scripts.
- Document dependency rules.

## Ticket 2: define core types

- Strongly typed IDs
- Bounding boxes
- Landmarks
- Embeddings
- Model descriptor
- Processing contracts

## Ticket 3: model installation

- Model manifests
- Download script
- SHA-256 verification
- Licence register

## Ticket 4: image decoder

- JPEG
- PNG
- Orientation normalisation
- Colour conversion
- Unsupported-format reporting

## Ticket 5: YuNet adapter

- Preprocessing
- Inference
- Output parsing
- Landmark conversion
- Tests

## Ticket 6: crop and alignment

- Padded crop
- Five-point alignment
- Deterministic output
- Boundary handling

## Ticket 7: SFace adapter

- Preprocessing
- Inference
- Vector validation
- L2 normalisation
- Cosine tests

## Ticket 8: `photoid inspect`

- Annotated image
- Face crops
- Embeddings
- Timings
- Manifest

## Ticket 9: SQLite persistence

- Assets
- Revisions
- Face occurrences
- Detections
- Crops
- Embeddings
- Processing runs

## Ticket 10: local folder scanner

- Recursive scan
- Stable local source keys
- File changes
- Deletions

## Ticket 11: OneDrive-sync availability

- Placeholder detection
- Local availability
- User-managed hydration workflow
- Staging copy
- Source fingerprint

## Ticket 12: resumable processing

- Job creation
- Job claim
- Retry
- Cancellation
- Checkpoints

## Ticket 13: review UI

- Face gallery
- Person creation
- Manual label
- Rejection
- Photo view

## Ticket 14: matcher

- Confirmed exemplars
- Cosine comparison
- Candidate ranking
- Suggestions

## Ticket 15: evaluation report

- Dataset split
- Precision and recall
- Threshold sweep
- Confusion pairs
- Throughput

## Ticket 16: portable bundles

- Bundle manifest
- Checksums
- Local worker execution
- Result import

Azure work begins only after these tickets produce a useful local application.

---

# 22. Security and Privacy

Requirements:

- Personal OneDrive authentication stays inside the official sync client.
- The application receives no OneDrive password.
- No personal Microsoft account token is uploaded to Azure.
- Azure workers receive no OneDrive paths unless deliberately included.
- Full originals are uploaded only for jobs that require them.
- Face-crop-only bundles are preferred for embedding comparisons.
- Job and result bundles are encrypted during transport.
- Temporary Azure storage is private.
- SAS credentials, when used, are narrowly scoped and short-lived.
- SAS strings are never logged.
- SSH keys are protected locally.
- Original Azure job input is deleted after verified result import.
- The canonical SQLite database remains local and backed up.
- Face crops are treated as sensitive biometric data.
- Logs use internal IDs rather than person names where practical.
- The review API is not publicly exposed during early versions.
- No original OneDrive file is modified.

---

# 23. Risks and Mitigations

## OneDrive placeholders

**Risk:** Files appear in directory scans but are not locally available.

**Mitigation:** Track availability explicitly and stage only hydrated content.

## Local path instability

**Risk:** A renamed or moved photo appears to be a new asset.

**Mitigation:** Use content hashes and reconciliation heuristics.

## OneDrive de-hydration

**Risk:** Windows frees local content after it was indexed.

**Mitigation:** Copy active processing inputs to a separate staging directory.

## Enterprise tenant restrictions

**Risk:** Azure automation examples assume service principals or managed identities.

**Mitigation:** Require interactive local control, SSH transfer or short-lived SAS only.

## Azure worker isolation

**Risk:** The worker cannot query the canonical database.

**Mitigation:** Make bundles self-contained and imports idempotent.

## Lost Azure VM

**Risk:** A temporary VM is deleted before results are retrieved.

**Mitigation:** Use short jobs, checkpoints and optional Blob-based result storage.

## SAS leakage

**Risk:** A SAS grants unintended storage access.

**Mitigation:** Limit scope, permissions and lifetime; prevent logging; delete temporary containers.

## HEIC support

**Risk:** Local and Linux decoders behave differently.

**Mitigation:** Keep decoding replaceable and validate against actual archive formats.

## Model incompatibility

**Risk:** New embeddings cannot be compared with old embeddings.

**Mitigation:** Version every embedding by model and preserve crops and labels.

## Feedback contamination

**Risk:** Incorrect automatic suggestions become future exemplars.

**Mitigation:** Use only human-confirmed labels as exemplars initially.

---

# 24. Architecture Decisions

Create these records:

```text
ADR-001  Use a modular monolith
ADR-002  Canonical labels are independent of models
ADR-003  Use ONNX as the primary model format
ADR-004  Use SQLite for the first version
ADR-005  Store reusable face crops
ADR-006  Use C# by default
ADR-007  Use YuNet and SFace as the baseline
ADR-008  Treat Azure as disposable compute
ADR-009  Use the local OneDrive sync client as the photo source
ADR-010  Do not require Microsoft Graph
ADR-011  Do not create Azure application identities
ADR-012  Use portable job and result bundles
ADR-013  Optimise for precision before recall
ADR-014  Do not use automatic labels as exemplars initially
ADR-015  Build the local vertical slice before Azure
ADR-016  Keep the canonical database outside Azure
```

---

# 25. LLM Context Management

Maintain `BUILD_CONTEXT.md` with only:

- Current milestone
- Current ticket
- Relevant modules
- Commands that work
- Current acceptance criterion
- Known failures
- Decisions made during the ticket
- Next concrete step

Module README files must describe:

- Purpose
- Public interfaces
- Dependencies
- Invariants
- Configuration
- Tests
- Known limitations

Use focused solution filters:

```text
PhotoIdentity.Core.slnf
PhotoIdentity.Recognition.slnf
PhotoIdentity.Source.slnf
PhotoIdentity.Bundles.slnf
PhotoIdentity.Web.slnf
PhotoIdentity.Full.slnf
```

Rules:

- One ticket normally changes one module and its tests.
- Contract changes are explicit separate work.
- Do not pass infrastructure types through Core.
- Do not duplicate orchestration between CLI and Worker.
- Keep model preprocessing inside its adapter.
- Keep Azure scripts outside recognition modules.
- Do not include model binaries, photos or large logs in LLM context.
- Update `BUILD_CONTEXT.md` after every completed ticket.

---

# 26. Recommended Starting Sequence

Begin with:

1. Repository skeleton
2. Core contracts
3. Model installation
4. Image decoding
5. YuNet detection
6. Face alignment
7. SFace embeddings
8. Single-image inspection
9. SQLite storage
10. Local folder processing
11. OneDrive-sync availability and staging
12. Review UI
13. Identity matching
14. Evaluation
15. Portable bundles
16. Azure VM pilot

Do not initially create:

- An Azure app registration
- A Microsoft Graph connector
- A service principal
- A managed identity
- A cloud database
- A public web deployment
- A GPU VM
- Unknown-person clustering
- Slideshow functionality

The first meaningful demonstration remains:

```text
1. Run `photoid inspect family-photo.jpg`.
2. Verify face boxes.
3. Inspect face crops.
4. Generate embeddings.
5. Compare same-person and different-person similarities.
```

The second meaningful demonstration is:

```text
1. Index a hydrated OneDrive test folder.
2. Name five people.
3. Confirm several examples.
4. Generate suggestions.
5. Review suggestions.
6. Measure precision.
```

The first Azure demonstration is:

```text
1. Create a crop-only job bundle locally.
2. Provision a temporary Azure VM interactively.
3. Copy the bundle using SCP.
4. Run the same worker used locally.
5. Copy the result bundle back.
6. Import it into SQLite.
7. Compare local and Azure results.
8. Deallocate the VM.
```

---

# 27. Expected Evolution

```text
Version 0.1
Local folder → YuNet/SFace → SQLite → browser review

Version 0.2
OneDrive-synchronised folder → hydration and staging

Version 0.3
Multiple models → repeatable evaluation

Version 0.4
Portable bundles → temporary Azure worker without identities

Version 0.5
Budget-controlled archive processing

Version 0.6
Ongoing local OneDrive synchronisation

Version 1.0
Stable people index → multi-person queries → collection API
```

The permanent centre of the system is:

```text
Local photo assets
    +
Canonical face occurrences
    +
Human-confirmed people labels
```

The following remain replaceable:

- OneDrive sync implementation
- Local staging strategy
- Azure
- CPU or GPU execution
- Detectors
- Embedders
- Vector indexes
- Clustering algorithms
- Review UI
- Future collection applications

```

```
