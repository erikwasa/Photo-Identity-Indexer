# WI-0188 — locally fenced video staging and scratch recovery

These are **opt-in library primitives**, not an activated background
video worker. No photo ingestion, video auto-queue, FFmpeg profile or
original-file mutation is enabled by this checkpoint.

## Source access contract

`PostgresVideoLocalSourceLocator` returns the source root, relative key,
expected source size and SHA-256 **only for an exact unexpired video lease**,
a supported current video revision, non-excluded source copy, and active
non-paused v35 operator generation. The locator is private worker data:
never include paths, keys or SHA values in CLI status or logs.

`VideoLeasedInputPreparer` checks the live lease/priority probe, resolves
the maintained source from PostgreSQL, and asks
`OneDriveVideoLocalStager` to make an immutable, private staging copy:

- Only MOV/MP4, inside the registered root, with no source/scratch path
  overlap, directory junction or symbolic-link alias.
- A locally-available source before opening. Windows uses
  `FILE_FLAG_OPEN_NO_RECALL` to request no cloud recall for the source
  handle; providers may still have implementation-specific behavior and
  **this must be checked on the maintained machine**.
- The source is opened read-only. Bytes copied to private scratch are capped
  at 1 GiB, SHA-256 is streamed, and actual size/hash must exactly match
  the catalogued revision. The copy is *not* trusted merely because its
  timestamp or length matches.
- Lease permission and source availability are checked before copying,
  during multi-megabyte copies, and after copying. Revocations, changed
  hash or invalid local status cause cleanup and rejection.
- Source originals are never passed to FFmpeg; a future audited argument
  profile receives only `VideoStagedLocalCopy.FilePath` and must close its
  child process before disposing the stage.

`VideoUnpublishedToolAttempt` demonstrates the integration of staging,
15-second lease heartbeats, Windows Job Object limits and live photo-priority
preemption. It **does not mark jobs successful** or publish anything:
no derivative artifact schema/transaction or approved FFmpeg profile
exists in WI-0188 yet. A future output adapter must atomically publish a
revision-bound artifact with PostgreSQL source/exclusion/lease fencing
before success can be acknowledged.

## Scratch-volume admission (checkpoint 11)

The explicit-call staging path now first requires enough available free space
on a **fixed, local, non-linked volume** for its bounded input copy plus
512 MiB of headroom. The unregistered single-attempt bridge separately
checks the combined leased input + tool scratch allowance, and an OS-capped
tool launch rechecks the output budget. Nonexistent, network and linked scratch
roots fail closed. Checks use filesystem free-space observations and produce
no private source paths in the result.

**This is not hard disk/I/O isolation.** Another process can consume the
space immediately after the check, tool writes can spike between periodic
scratch inspections, and the volume may also serve non-video software.
Before production registration, provide a dedicated hard-quota/enforced
workspace or an explicitly accepted equivalent with measured worst-case
disk pressure. CPU-only / no GPU, low privilege, network restrictions and
Windows mixed photo/video acceptance are still separate gates.

## Guarded publication bridge (unregistered, v36)

An optional guarded callback runs *after* a supervised child exits cleanly
but *before* the private tool workspace is deleted. It rechecks current
lease/photo priority and can call the v36 private artifact publisher.
The unregistered `VideoPublishedToolAdapter` uses it to atomically rename
a bounded verified `output.mp4` in app-owned scratch, then commits the
receipt and durable success under PostgreSQL source/control/lease fencing.
A stale lease cannot publish, and an unexpected DB failure can leave a
private, **unreferenced orphan** that a future DB-aware janitor must reclaim.
This does not validate real MP4 semantics or authorize FFmpeg/worker use.
See [v36 integration and upgrade gate](video-publication-and-v36-rehearsal.md).

## Operator-only abandoned scratch cleanup

New owned temporary workspaces contain an exact
`.photoidentity-video-owned` marker. A crash can leave these behind.
The operator may inspect and, after a long minimum retention, delete only
strictly named, marked, linked-free scratch directories:

~~~powershell
dotnet run --project src/PhotoIdentity.Cli -c Release -- video scratch-cleanup --scratch-root "C:\Path\To\DedicatedVideoScratch"
~~~

This command is **dry-run by default**. It emits only aggregate counts,
never private paths. A maximum 64-directory bounded scan and 48-hour
minimum-age default protect active work; you can choose
`--min-age-hours 24..720` and `--max-directories 1..256`.

After independently verifying the named directory is the dedicated
temporary video scratch root and reviewing the dry-run counts, repeat
with `--apply`. The sweeper refuses unfamiliar directories, recent
workspaces, reparse points/junctions and unmarked data; it never scans
or cleans source archives or photo derivatives intentionally.

## Remaining WI-0188 blockers

- No registered production worker or operator enable action.
- No audited, media-only FFmpeg argument/output profile. The unregistered
  proof-of-execution adapter MUST NOT be used to process private media.
- No fully contained quota for scratch filesystem capacity, disk I/O, GPU,
  or file/network access beyond the existing Windows CPU/memory/process job
  and staged byte bounds. A future adapter must avoid hardware acceleration
  unless separately approved.
- No revision-fenced atomic database + derivative publication/cleanup.
- No maintained-machine proof of no OneDrive hydration during source copy;
  no real mixed video/photo search/slideshow load or performance baseline.
- Schema v35 is already merged but still needs the separate
  [v34→v35 backup and isolated restore rehearsal](video-processing-control-v35.md)
  before starting new application code on the maintained v34 database.

These checkpoints can be validated in CI with synthetic files and Windows
commands. Real-media acceptance belongs to the consolidated local WI-0188
verification, not an automatic deployment.
