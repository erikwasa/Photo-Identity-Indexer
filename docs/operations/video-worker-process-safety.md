# WI-0188 checkpoint 4 — local subprocess safety foundation

**Status: implementation only.** The VideoToolProcessSupervisor is a callable
component in Core and is **not registered** with a hosted service, API, CLI,
scheduler, FFmpeg adapter, or active video queue consumer. Installing this
checkpoint does not process any videos.

## Changes

- A future caller must explicitly supply an absolute executable path,
  approved executable SHA-256, existing local scratch directory, a bounded
  wall time, temporary byte estimate and scratch entry cap.
- Each invocation gets a unique temporary working subdirectory. Parent
  directories and scratch entries cannot be symbolic links or junctions.
  Windows UNC executable and scratch paths are rejected.
- The subprocess environment excludes launcher credentials, user secrets
  and unrelated application variables. Standard output and standard error
  are discarded via bounded-memory streaming; no private content is logged.
- Timeouts, cancellation or temporary-space violations terminate the child
  process tree on a best-effort basis, and scratch cleanup runs after the
  child exits. Cleanup never targets source media or the scratch parent.
- The repository additionally serializes video-job completion under the
  source lock used by registration and source-copy exclusions, and heartbeats
  now refuse unsupported, superseded, excluded or deleted source revisions.

## Not yet sufficient for real video processing

This is NOT an OS-level process sandbox. It cannot enforce CPU/GPU usage,
memory limits, network confinement or file access outside the working
directory. Polling scratch usage is NOT a hard disk quota: a child may
allocate above the limit between checks. Process-tree termination is a
best-effort operation, not a Windows Job Object containment guarantee.

Before any video worker is enabled, implement all of the following:
- Current locally available source authorization under revision/exclusion
  fencing, without automatic OneDrive placeholder hydration
- Fixed audited and protocol-restricted FFmpeg arguments, with output paths
  confined to the per-run scratch directory
- Windows process and disk resource controls, photo-priority admission,
  operator stop/pause, controlled progress and diagnostics
- Atomic, revision- and lease-fenced publication or safe deletion of
  unfinished derivatives, including cleanup on restart
- Maintained-host mixed photo/video performance benchmark

## Verification scope

GitHub Windows CI covers binary hash mismatch, subprocess success/nonzero
exit, scratch overage, cancellation and cleanup. PostgreSQL tests cover
exclusion invalidation of running leases. No database migration, repeated
archive ingestion, or further local backup is required solely for this PR;
the accepted schema v34 baseline remains unchanged.

The operator must not enable workers or invoke the component against
private source files. No new operator-side action is exposed.
