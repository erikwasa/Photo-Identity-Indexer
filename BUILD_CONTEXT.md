# Build context

## Current milestone

**M02 — Local catalogue and jobs**

## Current work item

**WI-0012 — Add local folder scanning**

Status: `in_review`

## Branch and pull request

- Branch: `agent/WI-0012-local-folder-scanning`
- Draft pull request: [#23 — Add local folder catalogue scanning](https://github.com/erikwasa/Photo-Identity-Indexer/pull/23)

## Objective

Recursively catalogue supported local files, preserve stable source-owned identities, create immutable content revisions for changes, mark missing files without removing derived identity data, and report unsupported formats.

## Current slice

Implement the complete local-folder scan boundary using the existing `IAssetSource` contract. `Source.Local` owns filesystem enumeration and diagnostics; the SQLite adapter owns hashing, stable assets, revisions and deletion markers.

## Relevant files

- `src/PhotoIdentity.Source.Local/LocalFolderAssetSource.cs`
- `src/PhotoIdentity.Persistence.Sqlite/SqliteSourceCatalogueScanner.cs`
- `src/PhotoIdentity.Persistence.Sqlite/SqliteCatalogueDatabase.cs`
- `src/PhotoIdentity.Persistence.Sqlite/CatalogueRecords.cs`
- `tests/PhotoIdentity.Source.Tests/LocalFolderAssetSourceTests.cs`
- `tests/PhotoIdentity.Integration.Tests/LocalFolderCatalogueScannerTests.cs`
- `docs/delivery/work-items/WI-0012-local-scanner.md`
- `docs/delivery/status/work-items.yaml`

## Commands

```powershell
dotnet test tests/PhotoIdentity.Source.Tests/PhotoIdentity.Source.Tests.csproj
dotnet test tests/PhotoIdentity.Integration.Tests/PhotoIdentity.Integration.Tests.csproj
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
```

## Acceptance test for this slice

- Recursive and non-recursive scans use stable root-relative source keys.
- JPEG and PNG files are catalogued; unsupported extensions are returned as diagnostics.
- Repeated scans retain one asset and one revision for unchanged content.
- Changed content creates another immutable revision while retaining the asset ID.
- Missing files receive a deletion timestamp instead of being removed.
- Face occurrences and human labels attached to historical revisions survive deletion marking.
- Reappearing paths can clear the deletion marker through a later successful observation.
- Source IDs and path traversal are validated before content is opened.
- Existing schema-version-one databases upgrade transactionally to schema version two.

## Verification

WI-0011 completed through pull requests #17–#22. The final operational-policy pull request #22 merged at `35814a403d7d53d38105daa0cc4c1a2c616fbacf`; GitHub Actions run `30178418550` passed restore and vulnerability audit, Release build, all tests, living-document checks and Windows mixed-media verification.

Draft pull request #23 relies on GitHub Actions for executable validation because this agent environment does not contain the .NET SDK.

## Known issues

- The first scanner version hashes every supported file on every scan; metadata-based hash avoidance is deferred until correctness is established.
- Unsupported files are reported by the local-source scan report but are not persisted.
- The scan timestamp is the presence token, so overlapping scans of the same source are outside the supported single-writer policy.
- Image dimensions are not decoded during catalogue scanning; decoding remains a later processing stage.

## Next action

Resolve CI or review findings on pull request #23, then complete WI-0012 after merge and human verification.
