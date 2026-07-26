# Build context

## Current milestone

**M03 — OneDrive synchronised source**

## Current work item

**WI-0014 — Add OneDrive availability and staging**

Status: `in_progress`

## Branch and pull request

- Branch: `agent/WI-0014-onedrive-staging`
- Draft pull request: [#26 — Add OneDrive sync availability and verified staging](https://github.com/erikwasa/Photo-Identity-Indexer/pull/26)

## Objective

Treat Personal OneDrive as a local Windows sync-root source with explicit placeholder availability, user-managed hydration, verified staging fingerprints and cleanup that cannot remove unverified or source content.

## Current slice

Implement the complete source and staging boundary without OneDrive credentials or Microsoft Graph. The source classifies local, online-only, hydrating, unavailable and failed states from filesystem metadata. The stager independently verifies copied bytes and writes a cleanup sidecar only after verification succeeds.

## Relevant files

- `src/PhotoIdentity.Source.OneDriveSync/OneDriveSyncAssetSource.cs`
- `src/PhotoIdentity.Source.OneDriveSync/OneDriveSyncAssetStager.cs`
- `src/PhotoIdentity.Source.OneDriveSync/Properties/AssemblyInfo.cs`
- `tests/PhotoIdentity.Source.Tests/OneDriveSyncAssetSourceTests.cs`
- `tests/PhotoIdentity.Source.Tests/OneDriveFileAttributeStatusProviderTests.cs`
- `docs/delivery/work-items/WI-0014-onedrive-staging.md`
- `docs/delivery/status/work-items.yaml`
- `docs/delivery/status/milestones.yaml`

## Commands

```powershell
dotnet test tests/PhotoIdentity.Source.Tests/PhotoIdentity.Source.Tests.csproj
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
```

## Acceptance test for this slice

- Normal local files are reported as `Local`.
- Offline or recall-on-access placeholders are reported as `OnlineOnly`.
- Pinned placeholders whose content is still absent are reported as `Downloading`.
- Attribute I/O failures are reported as `Error` and retained in scan diagnostics.
- Opening or staging an online-only item returns a hydration-required error instead of intentionally triggering recall.
- Staging is refused unless SHA-256 verification is enabled and the target is outside the source root.
- Copied bytes are hashed during copy and independently re-hashed before becoming eligible for processing.
- Stable names include both content and source-item fingerprints, avoiding manifest collisions for duplicate content.
- Cleanup requires a matching sidecar and a fresh size/hash verification.
- Tampered, arbitrary, source-root and reparse-point paths are never removed by cleanup.
- No OneDrive credential, token or Graph dependency is introduced.

## Verification

WI-0013 completed through pull requests #24 and #25. Pull request #25 merged at `b7527275168ebc351ba4066e7c00a589ea0d03b6`; GitHub Actions run `30182282923` passed dependency audit, Release build, all tests, documentation checks and Windows mixed-media verification. The human maintainer then verified the private local 500-photo acceptance run.

The implementation-only head for draft pull request #26 passed GitHub Actions run `30184907471`, including all new OneDrive source and staging tests.

## Known issues

- Hydration remains user-managed through the OneDrive sync client; the adapter deliberately does not force recall.
- Availability is a point-in-time local filesystem observation and can change after it is reported.
- Placeholder availability is not yet persisted into SQLite catalogue rows.
- Cleanup removes only individually verified staged files and sidecars; directory lifecycle remains host-owned.

## Next action

Resolve final CI or review findings on pull request #26, then verify the availability mapping and staging flow against a real Personal OneDrive Files On-Demand folder before completing WI-0014.
