# WI-0189 — local playable proxy retention checkpoints

## Final accepted scope and maintainer results (2026-10-30)

**WI-0189 completed.** PRs #665–#671 are merged; the maintainer
reported successful local end-to-end checks and PostgreSQL safety tests.
All four scoped acceptance criteria are checked in the canonical
[work item](../delivery/work-items/WI-0189-video-local-low-resolution-proxies.md).
This runbook's earlier checkpoint text, including statements that
acceptance is pending, is retained as **history** and superseded here.

| Evidence | Accepted outcome |
| --- | --- |
| Focused .NET tests; docs validate/generate | PASS |
| Synthetic standard 720p | PASS landscape/audio and portrait/silent |
| Synthetic compact 540p | PASS landscape/audio and portrait/silent |
| Scratch cleanup | PASS |
| Video Settings restart; bounded one-video Archive step | PASS |
| Actual already-local MOV/MP4 proxies | 3 successful |
| Compact 540p | 14.36 MiB source → 0.07 MiB copy; 99.5% smaller |
| Standard 720p, case 1 | 2.36 MiB source → 1.53 MiB copy; 35.3% smaller |
| Standard 720p, case 2 | 20.68 MiB source → 0.74 MiB copy; 96.4% smaller |
| Seeking, orientation, audio | PASS |
| Processing duration | Maintainer deemed acceptable; no numeric real timing |
| Visual quality | Maintainer judged Compact 540p sufficient |
| Revision/preset reuse and source hashes | PASS; originals unchanged |
| PostgreSQL quota and manually confirmed retention safety tests | PASS; real cache was **not** deleted |
| Online-only video hydration | 0 |

**Explicit limitations.** These are three **different real sources**:
size reductions cannot compare 720p and 540p on the same revision.
Real encode time was reported qualitatively, not recorded numerically.
The existing Standard 720p remains the default; Compact 540p is an
approved **opt-in** option. Synthetic validation establishes the
H.264/AAC fast-start MP4 browser-oriented format and safety,
not in-app browser playback; WI-0152 owns video serving.
No destructive confirmation was performed on the personal cache;
isolated PostgreSQL tests cover old-plan refusal, quota denial,
private revocation/cleanup, source exclusions and corruption.
The publication budget covers **registered proxy bytes only**, not
a hard disk limit for scratch/orphan files. The maintainer's explicit
choice is manual preview-and-confirm retention, never unattended
eviction. Actual OneDrive-only hydration/release remains WI-0207,
and no video was hydrated during WI-0189 acceptance.

**Operator posture after sign-off:** keep cloud-only inclusion off
until WI-0207 is explicitly verified. Leave `standard-720p` as
default; use `compact-540p` where lower resolution is acceptable.
Return one-step Archive mode to the operator's preferred setting.
Only perform retained-proxy deletion via the existing localhost
Preview → Confirm with a stopped/paused video lane, not by directly
unlinking cache files. No further WI-0189 test run is required
solely for this accepted scope.

---

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
without migration.
Legacy API clients that PUT only `Enabled` and `IncludeOnlineOnly`
also **preserve** any existing saved quota override. New clients must
explicitly provide `overrideProxyLimits=true` with both numeric
limits to create/change an override, or `overrideProxyLimits=false`
with no limit values to clear it. Incomplete/ambiguous requests return
a non-mutating bad-request response. Invalid/partial persisted limits are treated as
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

## Checkpoint 5 — manually confirmed bounded proxy cleanup (PR #669)

The existing **Video processing → Review local proxy cleanup** panel
provides an explicit two-stage operator action:

1. **Disable Process videos** in Settings, save, use the existing
   **Pause archive** control, and verify no active video attempt or
   running lease remains. The new workflow **never** disables or pauses
   a worker automatically.
2. With a configured publication quota (saved override or inherited
   launcher pair), click **Preview proxy cleanup**. This is a bounded,
   non-mutating inspection of **at most 8** registered receipts. The
   maintenance service checks the application-owned private cache root,
   canonical UUID paths, exclusion and current-revision status,
   PostgreSQL stopped-control/zero-running-lease fence and actual
   cached file SHA-256 for every inspected existing file.
3. Review the **aggregate-only** counts of invalid, missing, corrupt,
   and old over-quota receipts, plus estimated remaining registered
   bytes. Newly registered valid copies are protected for at least
   **48 hours**; invalid/excluded/corrupt records may be cleaned
   earlier for privacy/integrity.
4. To remove generated proxies, explicitly tick **I understand** and
   click **Confirm removal**. No background job or implicit deletion
   follows a preview. The server recomputes the same bounded plan
   under PostgreSQL locks before any DB mutation, and rejects a
   changed/expired plan.
5. The existing maintenance logic **commits receipt revocation and
   marks affected jobs failed before unlinking private files**.
   A failed unlink leaves an invisible orphan for the pre-existing
   stopped-lane recovery janitor. A recovery report never authorizes
   deleting arbitrary/unregistered files. Restart or another review
   requires deliberate operator action.

### Confirmation safety

- The dry run returns a SHA-256 **fingerprint**, not a list of source
  paths or public media links. It covers cache root, quota/age/count/hash
  settings, the registered-byte snapshot, exact selected receipt IDs
  and hashes, and a five-minute UTC time bucket.
- A changed file (including same-size corruption), receipt, quota,
  selection, or expired window makes a previous confirmation invalid.
  A second use after removal has changed the receipts and is refused.
  The fingerprint is **not an authentication credential**.
- The API accepts **no caller-specified cache root, media path, job ID,
  file list or quota size**. It uses only the current persisted/launcher
  configuration. It never creates the cache or hydrates OneDrive.
- The API limits each confirmed foreground sweep to **8 receipts** and
  fixes the minimum age at **48 hours** with SHA-256 verification. Any
  remaining old files require another separately reviewed preview.
- Invalidated, missing and corrupt registered receipts may also be
  selected by the pre-existing maintenance policy, even when under the
  configured quota; the UI shows their separate counts.
- **No unattended eviction** or separate delete implementation is
  registered. The independent older CLI `video derivatives maintain
  --apply` is unchanged and remains an expert operator path that does
  not require a UI confirmation token.

### Checkpoint 5 automated verification

GitHub's existing Windows isolated PostgreSQL test step includes
`VideoRegisteredDerivativeMaintenanceTests`. PR #669 extends that
class to cover expired/stale guesses, same-size content corruption,
replayed confirmation rejection, DB-first receipt revocation,
preservation of healthy private copies and unknown cache files, and
no deletions when there are no eligible receipts. These synthetic tests
do not read any family video or require a running app.

```powershell
# Set PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING to your
# pre-existing approved isolated test PostgreSQL admin connection first.
[bool]$env:PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING

dotnet test tests/PhotoIdentity.Persistence.Tests/PhotoIdentity.Persistence.Tests.csproj `
  --filter "FullyQualifiedName~VideoRegisteredDerivativeMaintenanceTests" `
  -p:WasmEnableWebcil=false

dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
```

Do not count tests that return early due to an unset PostgreSQL admin
connection. **Do not run a confirmed deletion on the actual family
archive merely to verify this development checkpoint.** If desired,
the Settings-only **Preview** is non-mutating after verifying the lane
is disabled and stopped; apply/delete verification can be deferred
until the final combined maintained-host WI-0189 acceptance session
with an approved test cache and backup. No source file or online-only
hydration should be involved.

### Remaining WI-0189 acceptance

Automatic unattended retention/eviction with robust physical/orphan
capacity accounting is **still not enabled**. The encode profile is
still the fixed validated SDR 720p candidate; user-configurable
presets need a separate reviewed implementation and revision/preset
rebuild tests. The representative local MOV/MP4, rotated, silent,
long, seekable, codec/size/quality and original-hash acceptance
matrix is also outstanding. All four WI-0189 acceptance checkboxes
remain open.

## Checkpoint 6 — versioned selectable software encoding profile (PR #670)

Video Processing Settings now offers an approved **Output quality and size**
choice, independent of the quota override and of the Process videos switch:

| Choice | Durable preset version | Encoder limits | Intended evaluation |
|---|---|---|---|
| **Standard 720p** (default) | `wi0188-sdr-h264-aac-720p-pilot-v1` | Existing exact 720x720 max-axis, H.264 CRF 24, 2500k maxrate, 96k AAC | Unchanged WI-0188 baseline |
| **Compact 540p** | `wi0189-sdr-h264-aac-540p-compact-v1` | 540x540 max-axis, H.264 CRF 26, 1400k maxrate, 80k AAC | Candidate to compare output size, encode time and visible detail |

Both presets remain software-only, bounded SDR bt709 H.264/AAC,
`yuv420p`, <=30 fps, fast-start MP4, with optional single AAC audio
track and FFmpeg autorotation. Both retain the existing restricted
FFmpeg protocol, source/lease/size/job-object and original-file guards.
HDR, unsupported codecs/rotation, arbitrary filter strings and other
user-entered FFmpeg arguments remain rejected. The independent
FFprobe validator enforces the chosen output dimension and container
properties. The **256 MiB** absolute encoded-output safety cap and
separately configurable per-file publication cap still apply.

**Compatibility and persistence:** Older preference JSON files (without
an `EncodingProfile` property) use the exact historical 720p preset.
The Settings API returns `encodingProfile` and accepts one of
`standard-720p` or `compact-540p`. Older boolean-only Settings PUT
requests preserve the prior choice. Unknown strings or malformed saved
values fail closed. Changing profiles while a processing attempt is
active is rejected; Save by itself never starts a video worker or
requests cloud hydration.

**Revision/preset reuse:** Selected pending and queued jobs are
filtered by exact approved preset *and* FFmpeg digest; job identity
includes source revision, policy, preset and digest. A matching
completed job is reused rather than re-encoded. A changed revision,
selected profile or approved executable digest creates an independent
identity. Other profile jobs and private files are retained as
separate records; they are **not** silently re-encoded, marked current
for the new profile, or deleted. Switch back to the original preset
to reuse its existing current-revision identity, subject to the
normal exclusion, revision and maintenance fences. This checkpoint
does not alter the independent operator one-shot pilot CLI, which
continues using its own historical fixed 720p path.

### Automated pre-merge checks

The Windows build workflow's existing focused test step includes
`VideoProxyEncodingProfilesTests`. It verifies default argument
compatibility, strict allowed profile choice and no injected FFmpeg
options, output dimension verification, distinct and repeatable
revision/preset idempotency, legacy Settings migration, profile
persistence across restart, and fail-closed malformed values. This
is a **no-media** test layer and does not measure comparative speed
or visual quality.

```powershell
dotnet test tests/PhotoIdentity.Integration.Tests/PhotoIdentity.Integration.Tests.csproj `
  --filter "FullyQualifiedName~VideoProxyEncodingProfilesTests|FullyQualifiedName~VideoProcessingQuotaPreferencesTests|FullyQualifiedName~VideoProxyCandidateProfileTests" `
  -p:WasmEnableWebcil=false

dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
```

Run these only on current `main` after PR #670 merges. Isolated
PostgreSQL fixtures remain in the existing required Windows CI
suite. Do not claim a private-media acceptance from passing
synthetic tests.

### Later combined Windows profile and cache acceptance (not yet run)

The final WI-0189 verification must compare **the same already-local
MOV/MP4 clips** under both approved presets, without online-only
downloads or processing the entire archive. Include at least one
portrait/rotated file, a silent clip, an audio clip, a long but supported
clip and a deliberately unsupported/oversized control. Preserve
original SHA-256 hashes before and after; use the existing bounded
Archive advance/pause flow, not direct freeform FFmpeg on an archive
original. Record the following non-private aggregate observations:

- Input type, dimensions, orientation and whether an audio track exists
  (do not publish names or footage).
- Per-preset elapsed encoding time, output bytes, percentage reduction,
  max output dimensions, playback on the intended browser/TV,
  seeking, displayed orientation/aspect and audio correctness.
- Human visual comparison of details, including portrait footage;
  note any visible blur, banding or compression artifacts.
- Repeat with the same preset without new encoding; switch presets,
  confirm distinct jobs; switch back, confirm the prior valid identity
  is reused. Verify a revision change/exclusion is not exposed.
- Confirm configured registered-byte quotas, non-destructive
  retention previews, stopped-lane recovery, temporary-file and
  unknown-file safety. Any actual cleanup must use a separately
  approved **test** private cache with backup and explicit confirmation.

The default remains 720p until that quality/size/time decision is
recorded. **This checkpoint neither establishes that compact encoding
is smaller for every source nor authorizes online-only OneDrive
hydration/release (separate WI-0207).**

## WI-0189 remaining scope after checkpoint 6

The work item still lacks accepted representative on-device video
metrics and full revision/preset/retention recovery demonstration.
Physical cache/orphan accounting and any **unattended** retention
policy require a separate explicit decision and safeguards; the
current user-confirmed DB-first cleanup is safe but not a hard
filesystem quota or background storage regulator. Keep all four
formal WI-0189 acceptance boxes open until maintainer evidence.

### Manually confirmed minimum retention age (PR #670)

Video Settings → **Manual proxy retention** now offers exactly three
approved, persisted age choices:

| Minimum age | Intended behavior |
|---|---|
| **48 hours** (default) | Legacy WI-0189 behavior; consider oldest valid current registered copies only after two days |
| **7 days** | Protect valid current copies for a week |
| **30 days** | Protect valid current copies for a month |

These are **minimum protection ages**, not a command to delete a file
when it reaches the specified age. The same user-reviewed
**Preview → Confirm** workflow still requires disabled+paused processing,
zero running leases, an app-owned cache, SHA-256 verification, and a
fresh fingerprint. Changing the retention age is a policy change and
therefore invalidates the previous fingerprint. **Invalid, excluded,
missing or corrupt registered copies can still be selected before
that minimum age** for source privacy and integrity, as before.

Legacy preference JSON (without `RetentionAgeHours`) defaults to
48 hours. Older Settings API clients preserve the current retention
choice; unsupported numeric values fail closed, without modifying
saved settings. The standalone expert maintenance CLI retains its
own independently supplied age option and safety fences.

**No automatic eviction runs at 48 hours, 7 days or 30 days.**
The user must perform a new preview and explicit confirmation.
The registered-byte publication quota can refuse new copies while
old protected copies are retained. If capacity is insufficient, the
application does not bypass protection or delete source material.

### Local-only confirmation boundary (PR #670)

For safety, the existing browser **retention preview and apply**
endpoints now require the actual local and remote HTTP socket addresses
to both be loopback (IPv4, IPv6 or IPv4-mapped IPv6). An optionally
enabled trusted-LAN/phone listener cannot directly invoke these
maintenance actions; it receives HTTP 403. No
`X-Forwarded-For` or other request header authorizes deletion.
The operator must use the application through localhost on the
maintained Windows machine for any confirmed cache cleanup. This
does not change the normal video library or non-destructive viewing
from the optional trusted LAN. It is a defense-in-depth restriction,
not a substitute for local device access controls.

## WI-0189 final development handoff — safe Windows verification (PR #671)

**Maintainer choice (2026-10-30):** the existing explicit
Preview → Confirm manual eviction is sufficient. There is no
automatic eviction schedule to implement or test. The registered-byte
publication quota and guarded private cache recovery remain; **a hard
physical filesystem quota is not implemented** and should not be
claimed as verified. Actual online-only OneDrive hydration and
release are isolated in WI-0207; keep **Include online-only videos**
off throughout this WI-0189 review.

### 1. Run focused local tests after merging the PR

In an up-to-date Windows PowerShell checkout:

```powershell
Set-Location 'C:\Kod\codex\Photo Identity Indexer'
git pull --ff-only

dotnet test tests/PhotoIdentity.Integration.Tests/PhotoIdentity.Integration.Tests.csproj `
  --filter "FullyQualifiedName~VideoSyntheticProxyPilotTests|FullyQualifiedName~VideoProxyEncodingProfilesTests|FullyQualifiedName~VideoProcessingQuotaPreferencesTests" `
  -p:WasmEnableWebcil=false

dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
```

Expected: build succeeds, zero failed tests; documentation validation
and generated-file checks pass. If any step fails, **stop and report
the exact failure**, without proceeding to personal video processing.

### 2. Verify both real FFmpeg preset argument paths on GENERATED data

This step uses only two synthetic 3-second sources per profile and a
dedicated marked directory below Windows TEMP. It does not open
archive originals, connect to PostgreSQL, activate the app video
worker, or recall cloud placeholders. Obtain the **already approved**
tool paths and digests from the app's persistent packaged-launcher
configuration (or populate the four variables using the equivalent
approved local configuration if using a developer build).

```powershell
$launcherFile = Join-Path $env:LOCALAPPDATA 'PhotoIdentity\launcher.json'
if (-not (Test-Path -LiteralPath $launcherFile)) {
    throw 'No launcher file: use your existing approved local FFmpeg/FFprobe tool configuration.'
}
$approved = (Get-Content -LiteralPath $launcherFile -Raw |
    ConvertFrom-Json).settings
$ffmpeg = [string]$approved.PhotoIdentity__VideoProcessing__FfmpegPath
$ffprobe = [string]$approved.PhotoIdentity__VideoProcessing__FfprobePath
$ffmpegHash = [string]$approved.PhotoIdentity__VideoProcessing__FfmpegSha256
$ffprobeHash = [string]$approved.PhotoIdentity__VideoProcessing__FfprobeSha256

foreach ($pair in @(@($ffmpeg,$ffmpegHash), @($ffprobe,$ffprobeHash))) {
    if (-not $pair[0] -or -not (Test-Path -LiteralPath $pair[0] -PathType Leaf) -or
        ((Get-FileHash -LiteralPath $pair[0] -Algorithm SHA256).Hash.ToLowerInvariant() -cne $pair[1])) {
        throw 'An approved tool path or pinned SHA-256 is missing or mismatched.'
    }
}

$lab = Join-Path $env:TEMP ("photoidentity-wi0189-lab-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $lab -ErrorAction Stop | Out-Null
[System.IO.File]::WriteAllText(
    (Join-Path $lab '.photoidentity-synthetic-video-lab'),
    'wi0188-synthetic-video-lab-v1'
)

# Compile once so stdout from each invocation is only the JSON report.
dotnet build src/PhotoIdentity.Cli/PhotoIdentity.Cli.csproj -c Release
if ($LASTEXITCODE -ne 0) { throw 'Video CLI build failed.' }

$results = foreach ($profile in @('standard-720p','compact-540p')) {
    $json = & dotnet run --project src/PhotoIdentity.Cli -c Release --no-build -- `
        video synthetic-proxy-pilot --scratch-root $lab `
        --ffmpeg $ffmpeg --ffmpeg-sha256 $ffmpegHash `
        --ffprobe $ffprobe --ffprobe-sha256 $ffprobeHash `
        --profile $profile --apply
    if ($LASTEXITCODE -ne 0) {
        throw "Synthetic $profile pilot failed. Keep the lab for inspection."
    }
    $report = $json | ConvertFrom-Json
    if ($report.status -ne 'validated' -or $report.results.Count -ne 2 -or
        $report.profile -cne $profile -or $report.originalsOpened -or
        $report.schemaUsed) {
        throw "Synthetic $profile pilot did not satisfy the source-free contract."
    }
    $report.results | Select-Object encodingProfile, orientation, width,
        height, hasAudio, durationSeconds, encodedBytes, encodeMilliseconds
}

$results | Format-Table -AutoSize
Get-ChildItem -LiteralPath $lab -Force | Select-Object Name, Length
```

Expect four non-private synthetic rows (landscape/audio and
portrait/silent for both profiles), each with positive output bytes
and encoding duration, correct orientation and at most 720p/540p.
The scratch root should contain **only**
`.photoidentity-synthetic-video-lab` afterward. If unexpected files
remain, **do not recursively delete them**; report the failure for
investigation. The elapsed times are from synthetic input, **not**
representative real-video encoding measurements.

### 3. Prepare one bounded real-media acceptance session

Before an actual archive run:

1. Keep **Include online-only videos = off** throughout. Select only
   clips that are **already locally available**, with no need to
   change OneDrive pinning or fetch placeholders. Do not hash or open
   online-only originals.
2. Use **Video Settings → Stop each Archive advance after one video
   step**. Save and reload to confirm the setting persisted. This is
   intentionally **opt-in**; disabling it returns to the prior normal
   multi-video processing behavior.
3. Check that **photos and archive sync work are drained** and the
   application's existing approved tool/cache settings are valid.
   An Archive run remains allowed to advance photo work before reaching
   its one-video-step limit; the limit is **not** a photo-work limit.
   A single video step can be metadata discovery *or* one queued
   derivative attempt. If it only inspects a video, a later, separately
   authorized Advance may be needed to encode it.
4. Confirm the included archive coverage has a **known and acceptable
   already-local candidate set**. The ordinary Advance workflow
   selects the next eligible video, not an arbitrary filename given
   by the operator. If you cannot identify a suitably bounded set,
   **do not start an uncontrolled real-media test**; first report that
   limitation for a scoped selection safeguard.
5. Take a **current PostgreSQL backup** as required by existing
   review procedures. Record private baseline hashes of the selected
   already-local originals on your own machine. Do not post paths
   or hashes in the PR or chat.
6. Test **Standard 720p** first. Advance archive once and let the
   bounded session stop. Then review video Settings status on localhost:

   ```powershell
   $status = Invoke-RestMethod 'http://127.0.0.1:5080/api/video-processing/settings'
   $status | Select-Object enabled, includeOnlineOnly, encodingProfile,
       stopAfterOneVideoStepPerAdvance, queued, running, succeeded, failed,
       lastResult, lastAttemptFailure | Format-List
   $status.recent |
       Select-Object jobId, status, attempts, progressPercent, failureCode |
       Format-Table -AutoSize
   ```

7. With no active Archive attempt, switch to **Compact 540p**,
   Save, and perform **one more separately authorized Advance** on
   an eligible, already-local clip. Compare outputs using a locally
   approved MP4-capable player. Video streaming/range playback through
   the web app is a **separate WI-0152** and cannot be inferred here.
8. Confirm changing back to the same revision+preset+FFmpeg digest
   doesn't create another successful duplicate; changed revisions
   and exclusions must not remain visible as current copies.
   The queue uses exact preset identity; switching profile retains
   (does not silently delete) prior receipts.
9. Stop video processing and pause Archive. Test a **dry-run Preview**
   of manual retention, only from localhost, with a configured
   registered-byte quota. Actual **Confirm removal** deletes
   generated private copies and should be tested **only on an
   explicitly approved, backed-up test cache**, not automatically
   against the personal archive. Use the isolated PostgreSQL
   maintenance tests for cancellation/replay/exclusion/corruption.
10. Check original hashes still equal the baselines, source placeholders
    were not recalled, existing photo features still work, no
    processing lease is running and no unexpected scratch files persist.

The application does **not** expose exact media input/output paths
or original hashes in its status response. Keep detailed pairing,
hashes and visual judgments private. Share only aggregated results:

| Check | Record |
|---|---|
| Cases | count of local MOV/MP4, portrait/rotated, silent, audio, long/unsupported |
| Each profile | successful proxies, output size vs input, elapsed time, max dimension, fast-start/seek |
| Quality | browser/TV or local-player compatibility; visible detail, banding, audio and orientation |
| Reuse | same revision/preset no new encode; different version new identity; exclusions safe |
| Safety | originals unchanged; online-only recall zero; quota refusals; private recovery/cleanup |
| Recommendation | standard 720p or compact 540p, with observed reasons |

**Acceptance must not be claimed from synthetic data alone.** Existing
WI-0188 17/17 isolated safety tests and first real 720p publication
are prior evidence, not proof of a representative WI-0189 sample.
All four WI-0189 acceptance criteria stay open until the maintainer
records actual outcomes.
