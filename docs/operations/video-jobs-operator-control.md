# WI-0188 checkpoint 3 — inspect and cancel video queue jobs

The maintained Windows catalogue has been verified at **PostgreSQL schema v34**
(accepted 2026-10-19). This checkpoint changes **no schema** and introduces
**no scheduled video processing**, no cloud hydration, and no FFmpeg worker.
Do not repeat the v33→v34 backup/restore rehearsal merely for this checkpoint.

The local operator CLI can read the isolated queue and request cancellation of
a deliberately identified job. It cannot enqueue, claim or execute anything.

## Read-only status

In PowerShell from the repo root after updating to the merged checkpoint:

~~~powershell
$databaseName = 'REPLACE_WITH_VERIFIED_APP_CONNECTED_DATABASE'
$launcherCandidates = @(
    $env:PHOTOIDENTITY_LAUNCHER_CONFIG
    (Join-Path (Get-Location) 'PhotoIdentity.launcher.json')
    (Join-Path $env:LOCALAPPDATA 'PhotoIdentity\launcher.json')
)
$launcherPath = $launcherCandidates | Where-Object {
    $_ -and (Test-Path -LiteralPath $_ -PathType Leaf)
} | Select-Object -First 1
if (-not $launcherPath) { throw 'Launcher configuration not found.' }

$launcher = Get-Content -LiteralPath $launcherPath -Raw | ConvertFrom-Json
$variableName = [string]$launcher.postgresConnectionEnvironmentVariable
if ($variableName -notmatch '^[A-Za-z_][A-Za-z0-9_]*$') {
    throw 'Invalid PostgreSQL credentials reference.'
}
$secret = @(
    [Environment]::GetEnvironmentVariable($variableName,'Process')
    [Environment]::GetEnvironmentVariable($variableName,'User')
    [Environment]::GetEnvironmentVariable($variableName,'Machine')
) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
    Select-Object -First 1
if (-not $secret) { throw 'Launcher-selected PostgreSQL credentials unavailable.' }
[Environment]::SetEnvironmentVariable($variableName, $secret, 'Process')
Remove-Variable secret

dotnet run --project src/PhotoIdentity.Cli -c Release -- video jobs status --postgres-connection-env $variableName --database $databaseName
if ($LASTEXITCODE -ne 0) { throw 'Video job queue inspection failed.' }
~~~

The JSON shows aggregate queued/running/succeeded/failed/cancelled counts,
active cancellation requests, expired leases, reserved temporary-byte
estimates and at most 20 recent **opaque job IDs** with status/progress.
It does not report source paths, face/person identities, tags or metadata.
Deleted or excluded video records are hidden.

**Expected now:** all queue counts zero and recent empty, because checkpoint
2 created an empty queue and no worker or enqueue command exists. The status
message explicitly says executionEnabled is false. If you see unexpected jobs,
do not enable processing; report only sanitized counts.

The command **does not** invoke PostgreSQL migration initialization. It
requires a localhost connection and an existing current v34 catalogue.

## Controlled cancellation (only when needed)

Copy a job UUID from your own local status output. Run a **dry run** first:

~~~powershell
$jobId = 'REPLACE_WITH_LOCAL_JOB_UUID'
dotnet run --project src/PhotoIdentity.Cli -c Release -- video jobs cancel --postgres-connection-env $variableName --database $databaseName --job-id $jobId
~~~

The dry run reads the job state but does not write anything. To request a
cancellation of a specific queued or running job, repeat with the extra
`--apply` argument. Queued jobs become cancelled immediately; a running
job records a cancellation request and must have its worker acknowledge the
request or have its lease expire. **There is no worker yet.**

Do not paste private database details, job UUIDs, credentials, launcher JSON
or media paths into GitHub issues or reviews.

## Not yet implemented

This command is not a durable pause/stop control, real CPU/GPU/memory or
disk quota, worker process kill switch, secure source-open lease or atomic
artifact cleanup. WI-0188 stays open until these safeguards and a maintained
Windows mixed-load performance test are accepted.
