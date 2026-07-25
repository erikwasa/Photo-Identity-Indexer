---
id: WI-0012
title: Add local folder scanning
milestone: M02
status_source: ../status/work-items.yaml
depends_on: [WI-0011]
affected_modules: [PhotoIdentity.Source.Local, PhotoIdentity.Persistence.Sqlite, PhotoIdentity.Source.Tests, PhotoIdentity.Integration.Tests]
---

# WI-0012: Add local folder scanning

## Objective

Recursively catalogue supported files, record stable source metadata, detect changes and mark deletions.

## Acceptance criteria

- [x] Repeated scans do not duplicate unchanged assets.
- [x] Changed files create new revisions.
- [x] Deleted files are marked without deleting labels.
- [x] Unsupported formats are reported.

## Local filesystem adapter

`LocalFolderAssetSource` implements the Core `IAssetSource` contract without depending on SQLite:

- root-relative source keys use `/` separators and remain stable across scan calls;
- JPEG and PNG extensions are matched case-insensitively;
- recursive and non-recursive enumeration are supported;
- unsupported files are returned as ordered diagnostics rather than silently ignored;
- reparse points are skipped and resolved paths must remain below the configured root;
- content availability and stream access validate the owning source identifier.

## Catalogue scan boundary

`SqliteSourceCatalogueScanner` consumes any `IAssetSource` and owns catalogue persistence:

- every supported file is SHA-256 hashed before a short repository transaction begins;
- sources and source-owned asset keys resolve stable asset identities;
- a previously unseen asset/content hash creates an immutable revision;
- a repeated content hash refreshes presence without creating another revision;
- a successful scan marks assets not observed with that scan timestamp as deleted;
- observing a previously deleted path clears its deletion marker;
- deletion marking retains revisions, face occurrences, crops, embeddings and human labels.

The scanner deliberately hashes every supported file on every scan. Metadata-only hash avoidance is deferred until a correctness-preserving cache policy is defined.

## Schema version 2

Schema version 2 adds `last_seen_at_utc` and `deleted_at_utc` to assets plus a source-presence index. Version-one databases upgrade transactionally; existing assets receive their creation timestamp as the initial last-seen value.

The migration follows the forward-only policy established by WI-0011 and leaves the released version-one migration unchanged.

## Validation

```powershell
dotnet test tests/PhotoIdentity.Source.Tests/PhotoIdentity.Source.Tests.csproj
dotnet test tests/PhotoIdentity.Integration.Tests/PhotoIdentity.Integration.Tests.csproj
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
```

## Review focus

- stable and safe path normalisation;
- the supported-format boundary;
- content hashing before database transactions;
- scan-timestamp presence and deletion semantics;
- preservation of historical identity data;
- version-one to version-two migration behaviour.
