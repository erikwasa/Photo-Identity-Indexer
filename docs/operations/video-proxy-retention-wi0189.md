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

## Checkpoint 2 — read-only PostgreSQL retention preview (PR #666, merged)

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

## Checkpoint 3 — opt-in registered-byte publication fence (PR #667)

This checkpoint adds **publication admission**, not automatic eviction.
The shared Archive video lane retains its WI-0188 behavior unless BOTH
of the following explicit configuration values are supplied:

| Configuration key | Allowed MiB | Meaning |
|---|---:|---|
| `PhotoIdentity:VideoProcessing:MaximumCacheMiB` | 64..16384 | Maximum **registered** video derivative bytes after this publication |
| `PhotoIdentity:VideoProcessing:MaximumProxyMiB` | 1..256 | Maximum encoded bytes for one newly published proxy |

The corresponding Windows environment or approved launcher settings keys
are `PhotoIdentity__VideoProcessing__MaximumCacheMiB` and
`PhotoIdentity__VideoProcessing__MaximumProxyMiB`. They are validated
as decimal integers, require **both** settings, and reject invalid/partial
configurations **before enabling video processing**. No settings are
implicitly written to existing launcher configurations. This PR does
not expose these options in the in-app Settings page.

The published tool's existing source/exclusion/lease validation, output
validation, scratch limits, SHA-256 evidence, revision binding and original
immutability remain mandatory. The per-proxy cap is verified before
renaming any output. Within the PostgreSQL success transaction, a
single exclusive lock on the video operator-control row serializes
registered-byte accounting and commits; the aggregate includes **all**
registered derivative receipts, including superseded/excluded ones whose
private bytes could still exist.

If the new proxy would exceed either configured cap, publication is
rejected, no receipt is committed and the existing proxy files stay
unchanged. A known, transactionally rejected output is removed from
**its own app-owned private destination only**, never from the original
source or other registered proxies. The failure is surfaced as
`proxy-quota-exceeded` at the `publication` stage and is
**non-retryable**, to avoid uncontrolled transcode loops; any later retry
needs the application's existing explicitly guarded retry semantics.
Ambiguous database commit errors still preserve moved private output
for the existing recovery janitor. Pausing and source exclusion still
override quota checks.

**Limits of this checkpoint:** this registered-byte cap is **not** a
filesystem-level quota, does not include temporary workspaces or
unregistered crash orphans, does not evict existing proxies, does not
reserve space before transcoding, and applies only to publisher calls
given these opt-in limits (currently the shared Archive video worker).
The separate operator `video derivatives retention-plan` remains
advisory; it is not made executable by PR #667.

### Automated verification before PR #667 merge

Run on Windows; the first test project may require the known Webcil
workaround for the current SDK installation:

```powershell
dotnet test tests/PhotoIdentity.Integration.Tests/PhotoIdentity.Integration.Tests.csproj `
  --filter "FullyQualifiedName~VideoProxyRetentionPolicyTests|FullyQualifiedName~VideoProxyRetentionPreviewCommandTests|FullyQualifiedName~VideoProxyPublicationQuotaConfigurationTests" `
  -p:WasmEnableWebcil=false

# Separate isolated test databases: this is NOT a production DB operation.
# A real test admin connection is essential (do not count fixture self-skips).
[bool]$env:PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING

dotnet test tests/PhotoIdentity.Persistence.Tests/PhotoIdentity.Persistence.Tests.csproj `
  --filter "FullyQualifiedName~VideoDerivativePublicationTests|FullyQualifiedName~VideoRegisteredDerivativeMaintenanceTests" `
  -p:WasmEnableWebcil=false

dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
```

**Do not enable a quota or run a bulk video workload merely to verify
this PR.** The synthetic database-backed publication test proves
successful/inadequate-capacity transactions and known-denied byte cleanup.
A later maintained-host WI-0189 acceptance session must separately
measure actual already-local MOV/MP4 size, seekability, orientation, audio
and perceived quality, and observe real cache usage, restart, exclusions,
pause and immutable original hashes.

## Acceptance tracking

- **Actual smaller, seekable portrait/landscape proxies and original hash
  preservation:** existing WI-0188 one-video materialization accepted,
  but representative WI-0189 quality/codec/size assessment still pending.
- **Revision/preset reuse and invalidation:** versioned receipts and
  latest-revision visibility exist; full reuse/invalidation/rebuild tests
  and profile configuration still pending.
- **Quota, eviction and failure cleanup:** pure planner accepted (8/8 local
  tests); registered-byte advisory preview merged; opt-in atomic
  publication cap introduced in this PR. Automatic safe eviction, hard
  disk-space accounting and end-to-end edge-case verification pending.
- **Encode time, size, compatibility and quality report:** not yet measured
  on representative private videos; maintain original WI-0189 acceptance
  checkboxes as open until verified.

## Checkpoint 4 — persisted Video Settings quota override (PR #668)

The **Video processing → Local video proxy storage** panel now has
an optional **Override launcher proxy limits** checkbox and two numeric
limits. Enabling the override and saving writes only non-sensitive,
bounded preferences to the existing application-local
`video-processing-preferences.json` file:

- Total registered proxies: **64..16384 MiB**.
- Maximum one newly published proxy: **1..256 MiB**, not exceeding the total.

Both are required when the override is enabled. They apply to new
publication attempts via the existing transactionally checked WI-0189
fence. No persisted override means the existing launcher keys (if any)
continue to apply; no launcher limits means the prior WI-0188
unrestricted-*registered-byte* behavior, still subject to pre-existing
scratch/file/OS limits. The toggle **never** starts video processing,
automatically deletes older copies, or changes source files.

A legacy preferences file with only `Enabled` and
`IncludeOnlineOnly` automatically inherits the launcher settings,
without migration. Invalid/partial persisted limits are treated as
invalid preferences: **video processing and online-only consent are
disabled**, rather than guessing a budget or silently enabling work.
On API updates, invalid limits are rejected before any video processing
control changes. If a video attempt is currently active, Settings refuses
a quota change; use the existing **Pause archive** control first. New
settings are used for subsequent attempts without restarting the host.
The system does not retroactively revoke existing derivatives if the
new total is lower than the already registered total; future
publication is refused until there is sufficient registered capacity.

### Checkpoint 4 CI verification

```powershell
dotnet test tests/PhotoIdentity.Integration.Tests/PhotoIdentity.Integration.Tests.csproj `
  --filter "FullyQualifiedName~VideoProcessingQuotaPreferencesTests|FullyQualifiedName~VideoProxyPublicationQuotaConfigurationTests" `
  -p:WasmEnableWebcil=false

dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
```

The repository's Windows build workflow now includes the new focused
preferences tests, covering legacy preference JSON, restart, saved
overrides vs launcher limits, clearing the override, invalid input,
orphan temp-file absence and fail-closed corrupt persisted data.
These are development tests, **not** proof that the browser controls
were visually accepted or that real video proxy capacity is managed.

### Optional UI-only maintained-host smoke test

This can be deferred and combined with the remaining WI-0189 acceptance
tests after profile selection and retention work is finished. If tested
separately, **disable Process videos and Pause archive first**. Do not
enable any new processing, OneDrive hydration or apply a deletion.

1. Note existing video settings and any configured launcher quota.
2. Open Video processing Settings, select **Override launcher proxy
   limits**, enter e.g. `1024` and `256`, and Save.
3. Reload Settings and restart the app: the same override and both
   numbers should persist, with Process videos still disabled.
4. Uncheck **Override launcher proxy limits** and Save; the panel
   should return to launcher-inherited behavior. Restart to verify.
5. Optionally, while the app is running on its usual loopback port,
   inspect only the non-sensitive Settings response:

   ```powershell
   Invoke-RestMethod 'http://127.0.0.1:5080/api/video-processing/settings' |
     Select-Object enabled, includeOnlineOnly, maximumCacheMiB, maximumProxyMiB
   ```

   The two quota fields should be null when not overridden. The existing
   CLI/launcher values, if any, remain outside this local preferences file.

No private-video output, original hashes, cloud recall, file deletion
or mass archive run is necessary for this **Settings-only** smoke test.

## Updated WI-0189 acceptance inventory (checkpoint 4)

- Representative smaller/seekable MOV/MP4, orientation/audio/original
  hashes: **not yet accepted** (one WI-0188 real-video publication already
  accepted, but not WI-0189 quality matrix).
- Revision/preset reuse, invalidation and cache persistence lifecycle:
  **partially implemented**, end-to-end verification pending.
- Quota/eviction: pure planner, read-only registered inventory, atomic
  optional quota refusal and persisted UI overrides implemented.
  Operator-only stopped-lane, DB-first maintenance exists; unattended
  safe eviction and **hard physical** cache quota not implemented.
- Report encode time, output size, browser compatibility and perceived
  quality: **not yet completed**; later already-local MOV/MP4 checks.
- Online-only OneDrive-only hydration and release: **WI-0207**, not
  exercised or newly authorized by this checkpoint.

**WI-0189 stays `in_progress`; no acceptance checkbox has been signed off.**
