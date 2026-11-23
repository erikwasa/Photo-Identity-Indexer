# WI-0188 — private video derivative recovery (v36)

**Development-only, default off:** this command does NOT activate a worker,
process a video, hydrate OneDrive, expose any generated video or mutate photo
jobs, archives or original files. It operates only on the application-managed
private v36 derivative cache, using the current PostgreSQL catalogue.

## Why there is a recovery step

The v36 publisher renames output into a private canonical
`video-derivatives/<job-guid>/<lease-guid>.mp4` location **before** its
PostgreSQL receipt/success transaction. Database rejection or a crash after
rename can leave a real file with no receipt. This is an orphan, not a
successful published derivative; it must remain invisible to readers.

The recovery tool compares these files with the authoritative database.
Only **old unregistered files whose job still exists and is terminal**
are eligible for deletion. A registered derivative is not an orphan,
even if its source is currently excluded or superseded. Such registered
but now invalid evidence is counted separately as
`invalidatedRegistered` and is protected from this command. Full privacy
purging and safe regeneration/eviction require WI-0189 work; deleting a
registered artifact during reversible exclusion would otherwise leave a
`succeeded` job with missing bytes after a restore.

## Safety requirements

- The maintained database must have finished the **fresh v34→v36 isolated
  rehearsal** and appropriate upgrade, as documented in
  [the consolidated v36 upgrade runbook](video-publication-and-v36-rehearsal.md).
  Do not run this command against the maintained v34 database; it never
  initializes or migrates a catalogue.
- Use an explicit verified local PostgreSQL connection variable and actual
  application database name (never assume a previous rehearsal database).
- The **independent video lane must be fully stopped**: database
  `enabled=false`, `paused=true`, and **zero running video jobs**.
  If interrupted leases remain, wait until they expire, use the existing
  dry-run `video jobs recover` command, and only then consider its
  separately explicit `--apply`. Do not touch unexpired leases.
- The private root must exist, be absolute, locally accessible, have the
  exact `.photoidentity-video-derivative-root` marker, contain no junction
  ancestors, and not overlap any registered local source archive.
- Only direct job-ID directories and lowercase lease-ID MP4 filenames are
  visited, without recursive descent. Linked or unfamiliar files are
  preserved. Aged files use the last-write time, at least 48 hours old.
  Each run examines at most 256 entries and at most 64 candidate files.
- The tool locks the PostgreSQL video control row while inspecting/deleting;
  publishers/claims cannot pass through while it holds the lock. An orphan
  must belong to a known `succeeded`/`failed`/`cancelled` job but
  have **no** receipt for that exact canonical path.
- Default mode is **dry run**. `--apply` deletes only eligible individual
  files; it never recursively deletes directories or removes receipts.
  Failed deletion fails the command. Report output contains aggregate counts
  only—no file paths, job IDs, SHA values or media descriptions.

## Operator flow — only after consolidated local verification is requested

Run this in PowerShell from the repository root, first confirming the current
v36 database and private cache root independently:

~~~powershell
$databaseName = 'REPLACE_WITH_CONFIRMED_APP_DATABASE'
$privateRoot = 'C:\REPLACE_WITH_DEDICATED_PRIVATE_VIDEO_DERIVATIVE_ROOT'
# Use the existing launcher connection variable name in the current PowerShell
# process; do NOT print or paste its value.
$connectionVariable = 'REPLACE_WITH_EXISTING_POSTGRES_ENV_VARIABLE'
dotnet run --project src/PhotoIdentity.Cli -c Release -- video jobs status `
  --postgres-connection-env $connectionVariable --database $databaseName
dotnet run --project src/PhotoIdentity.Cli -c Release -- video derivatives recover `
  --postgres-connection-env $connectionVariable --database $databaseName `
  --cache-root $privateRoot --min-age-hours 72 --max-files 32
~~~

Inspect `eligible`, `protected`, `refused`,
`invalidatedRegistered` and `dryRun=true`. The command will fail
closed if the video lane is not entirely stopped or any running lease exists.
Only after confirming the root is an app-owned private cache and that the
numbers match expectations, an operator can repeat the **identical** command
with `--apply` appended.

Expected outcomes:

- `eligible=0`: nothing to delete; no reason to apply.
- `protected>0`: current or invalidated receipts, live/recent files,
  unknown jobs and unsupported entries are not deleted.
- `invalidatedRegistered>0`: a **separate** WI-0189 privacy and
  retention pass is needed, not a reason to override the safety gate.
- `deleted=N` in apply mode: exactly N unregistered, bounded, old
  private files were removed. No other files or database rows were changed.

## Registered receipts now have a separate operator lifecycle

The orphan recovery command **still protects every registered receipt**.
A complementary dry-run-first maintenance command can explicitly revoke
invalidated/missing/corrupt registered files and quota-selected old valid
files under the same stopped-lane control fence. It first invalidates
the DB receipt/job, then unlinks known private outputs; failures to
unlink become future orphan-recovery work. It also supports an explicit
separate UUID-only regeneration request when the video source is again
eligible. See [registered receipt maintenance](video-registered-derivative-lifecycle-v36.md).
Neither command creates a production video worker or bypasses v34→v36
local rehearsal.

## Limits / remaining WI-0188 and WI-0189 work

This operator tool does not solve a malicious local filesystem race or
provide OS disk quotas, low-privilege isolation, FFmpeg output validation,
a real production worker, enablement, retention/preset-based eviction,
source-copy privacy purge or automatic scheduled recovery. The v36
publisher's file rename and database commit remain separate operations.
There is no media-serving endpoint connected to these receipts yet.

CI uses synthetic old/recent files, durable registered and excluded receipts,
stopped/enabled/running lease gates, archive overlap and parser checks.
Operator file cleanup should be grouped with the eventual one-round
maintained Windows/OneDrive mixed-photo/video acceptance, not requested
for this intermediate development batch.
