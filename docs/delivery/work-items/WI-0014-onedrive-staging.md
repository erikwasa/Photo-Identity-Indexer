---
id: WI-0014
title: Add OneDrive availability and staging
milestone: M03
status_source: ../status/work-items.yaml
depends_on: [WI-0012, WI-0013]
affected_modules: [PhotoIdentity.Source.OneDriveSync, PhotoIdentity.Source.Tests]
---

# WI-0014: Add OneDrive availability and staging

## Objective

Add a filesystem source for the local OneDrive directory with placeholder detection, user-managed hydration, verified staging copies and content fingerprints.

## Acceptance criteria

- [x] Online-only, local and failed availability states are distinct.
- [x] Staged files are verified before processing.
- [x] No OneDrive credentials or Graph permissions are requested.
- [x] Staging cleanup cannot remove unverified source content.

## Local sync-root boundary

`OneDriveSyncAssetSource` treats Personal OneDrive as a Windows-synchronised filesystem root. It does not call Microsoft Graph, authenticate to OneDrive or request application permissions. Stable source-owned relative keys remain the public asset identity.

The source scans JPEG and PNG files and reports unsupported extensions separately. Reparse-point directories are not traversed. Cross-source references and paths escaping the configured root are rejected before any content is opened.

## Availability policy

Availability is derived from the local filesystem view:

- a present file with no offline or recall attributes is `Local`;
- an offline or recall-on-access placeholder is `OnlineOnly`;
- a placeholder that is also pinned is `Downloading`, because the sync client has been asked to keep it locally but the bytes are not fully present yet;
- a missing path is `Unavailable`;
- an I/O or access failure while reading attributes is `Error` and is included in the scan diagnostics.

`OpenContentAsync` and staging both refuse `OnlineOnly` and `Downloading` items with `OneDriveHydrationRequiredException`. The operator hydrates the item through the OneDrive sync client and retries. The adapter does not intentionally open a placeholder to trigger download.

## Verified staging

`OneDriveSyncAssetStager` requires hash verification and a destination outside the OneDrive source root. It:

1. rechecks that the source item is locally available;
2. copies to a unique partial file while computing SHA-256 and byte count;
3. independently re-reads and hashes the partial copy;
4. moves the verified bytes to a deterministic name containing both the content fingerprint and a stable source-item fingerprint;
5. writes a verification sidecar only after the staged data is verified.

Repeated staging of the same unchanged source item reuses the same verified path. Different source items with identical content receive different source fingerprints, so their cleanup manifests cannot overwrite one another.

## Cleanup policy

Cleanup removes one staged file and its sidecar only when all of these checks pass:

- the path is inside the supplied staging directory and outside the OneDrive source root;
- the staging path does not traverse a reparse-point directory and the data file is not a reparse point;
- a verification sidecar exists and matches the source ID, source key, staged filename, size and SHA-256;
- a fresh hash of the current staged bytes still matches the verified fingerprint.

Missing manifests, tampered bytes, arbitrary files and source paths are retained and reported through `StagingVerificationException`. Cleanup never recursively deletes a staging directory.

## Validation

```powershell
dotnet test tests/PhotoIdentity.Source.Tests/PhotoIdentity.Source.Tests.csproj
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
```

Draft pull request [#26](https://github.com/erikwasa/Photo-Identity-Indexer/pull/26) adds availability classification, placeholder-safe content access, verified staging, guarded cleanup and automated source tests.

## Remaining work

- Resolve CI or review findings on pull request #26.
- Verify availability against a real Personal OneDrive Files On-Demand folder before marking WI-0014 complete.
