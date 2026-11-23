# WI-0188 — photo-priority gate for queued video derivatives

**Checkpoint:** read-only, PostgreSQL-backed admission evidence. This
implementation does not start any video worker, transcode or hydrate media,
change a PostgreSQL schema, alter photo job logic or introduce new settings.

## What blocks a video lease

Every attempt to claim a queued video derivative now reads the following
existing authoritative PostgreSQL states within the same serialized video
claim transaction:

- Archive advancement requested/running (`archive_advancement_control`
  desired running or synchronization required)
- Photo processing run pending or running (`processing_runs`)
- Photo processing job queued or running under a pending/running run
  (`processing_jobs`)

Any of these conditions blocks **new** video leases, even when a caller
passes `photoWorkActive: false`. A PostgreSQL error aborts the attempt;
it does not silently count as photo idle.

The application remains **default off** and no hosted worker is connected.

## Observe locally (optional after merge)

The existing read-only, operator-only queue status command now includes
three photo-priority flags and a derived `photoWorkActive` boolean:

~~~powershell
dotnet run --project src/PhotoIdentity.Cli -c Release -- video jobs status --postgres-connection-env YOUR_LAUNCHER_DB_ENV_VARIABLE --database YOUR_CONFIRMED_APP_CONNECTED_DATABASE
~~~

Use the already-verified launcher variable name and maintained database name
from your local setup. Do not paste credentials, database IDs, job UUIDs,
source paths, private metadata, or unredacted output in a public review.

With no archive advancement or photo processing running, the priority
flags should normally be false. With an actual photo processing run or
archive advancement requested, `photoWorkActive` is true. This is a
**status-only query** with no schema migration and no source media access.

## What remains before real runtime video activation

This gate is an admission snapshot, not an enforceable CPU/GPU/IO quota
and not preemption of a child process once it has started. A photo job
starting immediately after the snapshot can still run concurrently with
an existing video lease. Interactive photo search, browsing and slideshow
activity are not all represented by photo background-work tables.

Later worker checkpoints must provide live rechecks of photo activity,
preemption/pause, stable work cancellation, per-job OS resources and
source availability/exclusion fencing before any FFmpeg adapter is enabled.
The video queue is not yet allowed to run even when the status says photos
are idle.

**No local migration rehearsal or comprehensive regression re-run is
required solely for this checkpoint.** The maintained v34 acceptance
remains the baseline; the full mixed-load measurements come later.
