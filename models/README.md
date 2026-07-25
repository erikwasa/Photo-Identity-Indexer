# Models

Tracked content in this directory is limited to manifests, licence information and installation scripts.

Model binaries are installed into `models/files/` and are ignored by Git.

## Install the baseline models

```powershell
./models/install-models.ps1
```

Install one model:

```powershell
./models/install-models.ps1 -Id yunet-2023mar-fp32
```

Verify installed files without downloading:

```powershell
dotnet run --project tools/PhotoIdentity.Models -- verify
```

List manifest metadata:

```powershell
dotnet run --project tools/PhotoIdentity.Models -- list
```

Downloads are pinned to an upstream repository revision. Installation succeeds only when both the expected file size and SHA-256 digest match the manifest. A partially downloaded or mismatched file is deleted and never promoted to the final path.

## Licence records

Each manifest records separately:

- the licence for code shipped beside the model
- the licence asserted for the weight file
- training-data provenance and any unresolved licence considerations

A permissive weight licence does not by itself establish that every possible use of the training data is unrestricted.
