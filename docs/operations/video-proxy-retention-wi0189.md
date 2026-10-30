# WI-0189 — local playable proxy retention checkpoints

## Maintainer evidence — checkpoint 1 (2026-10-30)

PR #665 merged. The maintainer ran the focused Windows .NET 10 retention
policy tests using `-p:WasmEnableWebcil=false` because Webcil conversion
failed with access denied in the local NuGet WebAssembly runtime package.
**8 passed, 0 failed, 0 skipped; build succeeded.** The maintainer also
reported running `PhotoIdentity.Docs validate` and generating the delivery
documentation (no separate transcript or result counts were supplied).
This verifies the pure planner tests, **not** physical eviction or
real media quality.

The pure planner uses only opaque derivative IDs, sizes, ordering timestamps
and pinned flags. It selects oldest non-pinned entries deterministically,
protects pinned entries, rejects malformed/overflowing inventory, and
can now recommend reclamation if the *registered total already exceeds*
the quota. It never reads original files or deletes anything.

## Checkpoint 2 — read-only PostgreSQL retention preview (PR #666)

An operator-only `video derivatives retention-plan` command reads a bounded
PostgreSQL snapshot of **registered** playable derivatives, in a
cache owned by Photo Identity. It checks current video
source/revision/inspection and exclusions; canonical UUID-only derivative
paths, cache marker, local source-root separation, non-linked parents/files,
registered hashes' syntax and actual private file lengths. It reuses the
pure policy to recommend a registered-byte capacity plan.

Safety rules:

- Requires the video lane **disabled AND paused** and zero running jobs.
  The snapshot is read-only, and neither queue control nor any source file
  is modified. No schema migration, original reading or OneDrive recall.
- Every registered receipt must be current and its local cached bytes
  verifiable by length. Stale, missing, truncated, linked, or malformed
  receipts fail the entire plan closed, without suggesting partial eviction.
- More than `--max-records` registered receipts causes
  `inventory-limit-exceeded`, never a partial recommendation.
- Existing registered byte totals **can exceed** the proposed quota; the
  planner then suggests oldest eligible entries for reclamation. Young
  receipts are pinned using *creation time as a temporary retention-age
  proxy*. There is no last-played or last-access tracking yet.
- Output contains aggregate counts and reason codes, **no** source paths,
  derivative UUIDs, media metadata or SHA values.
- `admitted=true` means only that a **hypothetical registered-byte**
  reservation could fit after the suggested removals. It is not a promise
  of disk space, physical quota, safe deletion, or actual transcoding
  admission. Unknown/unregistered files and source disks are not included.
- **There is no `--apply` option.** No deletion, receipt revocation,
  encoding, playback endpoint or worker activation is implemented by this
  checkpoint. The older, separate operator-only
  [registered maintenance command](video-registered-derivative-lifecycle-v36.md)
  retains its independently reviewed stopped-lane DB-first deletion fences.

### Development tests

Run the focused, database-free tests on Windows:

```powershell
dotnet test tests/PhotoIdentity.Integration.Tests/PhotoIdentity.Integration.Tests.csproj `
  --filter "FullyQualifiedName~VideoProxyRetentionPolicyTests|FullyQualifiedName~VideoProxyRetentionPreviewCommandTests" `
  -p:WasmEnableWebcil=false
```

For actual PostgreSQL integration tests, configure the **existing isolated
test PostgreSQL admin connection** as in the project's
`verify-postgres.ps1` and execute:

```powershell
dotnet test tests/PhotoIdentity.Persistence.Tests/PhotoIdentity.Persistence.Tests.csproj `
  --filter "FullyQualifiedName~VideoRegisteredDerivativeMaintenanceTests" `
  -p:WasmEnableWebcil=false
```

**Important:** These fixture tests return without testing PostgreSQL when
`PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING` is unset. Do not
claim a real PostgreSQL pass from an unconfigured test run; ensure the
variable is set before running them.

Validate generated documentation before merge:

```powershell
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
```

### Optional read-only operator preview, not required to merge checkpoint 2

Only on an already current-schema local PostgreSQL catalogue, after
confirming the cache belongs to the application and the video worker is
fully stopped; replace the placeholders with **existing approved** local
values. No new private archive or cloud hydration is authorized.

```powershell
$db = 'YOUR_VERIFIED_LOCAL_DATABASE'
$envName = 'YOUR_EXISTING_POSTGRES_CONNECTION_ENV_NAME'
$privateCache = Join-Path $env:LOCALAPPDATA 'PhotoIdentity\video-worker-cache'

dotnet run --project src/PhotoIdentity.Cli -- video derivatives retention-plan `
  --postgres-connection-env $envName --database $db `
  --cache-root $privateCache --quota-mib 1024 `
  --max-proxy-mib 256 --incoming-mib 256 --max-records 1024
```

Expect `dryRun=true`. When `admitted=false`, inspect the path-free
`reason` code and run the pre-existing maintenance **dry run** if
receipts are stale/unverifiable. Do not turn this report into file
deletion instructions or run a whole-archive video job.

## Remaining WI-0189 implementation

Before enforcing a cache quota, connect catalogue-backed inventory to
writer/publication lease fencing; handle concurrent changes, unregistered
orphan bytes, actual physical capacity, safe privacy/eviction revocation and
crash recovery. Add persisted Settings quotas/profile controls and bounded
revision/preset validity, with end-to-end original immutability and
representative MOV/MP4 orientation, silence, audio, seek/quality and size
measurements. **WI-0189 acceptance remains open.**
