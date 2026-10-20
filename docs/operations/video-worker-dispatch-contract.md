# WI-0188 checkpoint 10 — unregistered bounded worker-dispatch contract

The application still has **no registered video-processing worker**, no
operator enable command and no derivative-format publisher. This checkpoint
adds one deliberate, explicit-call coordinator for future adapters without
starting media work on ordinary scans, startup, photo searches or slideshows.

## One-job-per-cycle admission and outcomes

`VideoWorkerAttemptCoordinator.RunOneAsync` requires both:

1. An explicitly enabled, non-paused caller budget with concurrency **exactly
   one** until a maintained mixed-load benchmark proves larger limits safe.
2. An independent, durable PostgreSQL v35 video-control row allowing claims
   and an admissible queued video job. The queue already rechecks authoritative
   photo work, cancellation, current revision, source exclusion and reserved
   scratch quota inside the serialized transaction.

The coordinator checks the newly claimed lease again before invoking the
`IVideoPublishedDerivativeAdapter` (an interface with **no production
implementation registered**). Attempt cancellation uses the configured
maximum job lifetime. An adapter must await its subprocess termination and
private staging cleanup before returning or acknowledging cancellation.

The adapter must own its own revised source authorization, OS Job Object,
photo-priority preemption, and file/database atomic publisher. WI-0188's
existing `VideoUnpublishedToolAttempt` supplies staging and execution but
deliberately **does not** implement this publishing interface.

The coordinator NEVER calls success acknowledgement. If an adapter reports
`Published=true`, the coordinator independently reads the durable job status
and refuses to report success unless the persisted job is in succeeded state,
100% progress and not cancelled. This is a *status-level* gate only: it does
not inspect derivative bytes or prove an atomic file/database commit. A
publisher must provide those stronger guarantees **before** the interface
is implemented in production.

If the adapter returns an unpublished result, throws, or hits its lifetime
deadline, the coordinator attempts the existing lease-fenced PostgreSQL
failure/retry transition. An invalidated token, control generation, cancelled
job, excluded video or unavailable PostgreSQL refuses completion. Exceptions
are sanitized to codes; filenames, media paths, stack traces, source SHA
and subprocess output are not surfaced through coordinator results.

## Operator recovery while disabled

The video lane may remain paused or stopped while an interrupted lease ages
past its 60-second expiry. With no hosted worker, automatic claim recovery
is intentionally not running. To recover only expired video leases, use the
existing PostgreSQL operator command on a **verified local schema-v35 database**:

~~~powershell
dotnet run --project src/PhotoIdentity.Cli -c Release -- video jobs recover --postgres-connection-env YOUR_VERIFIED_CONNECTION_ENV --database YOUR_CONFIRMED_APP_DATABASE
~~~

This is **read-only dry run by default** and reports an aggregate count of
visible expired leases. Review it, then explicitly repeat with `--apply`
to recover expired queue records (including excluded/deleted video jobs which
are intentionally hidden from the operator preview). Recovery does not open,
hydrate, modify, or delete source files, nor does it change photo jobs.
Running jobs with unexpired leases cannot be claimed or recovered by this
command. Stop/pause intent remains unchanged.

**The operator must not run any v35 CLI against the maintained v34 database
before completing the fresh backup and isolated v34→v35 rehearsal.**

## Tests

Disposable PostgreSQL tests use one synthetic video revision to assert
default-off/persisted-off admission, false in-memory publication claims,
retryable exceptions without private diagnostic leakage, pause refusal and
no adapter calls outside the gated path. No original video is opened.

## Scratch capacity preflight (checkpoint 11)

The unregistered staging and tool attempt now preflight an available fixed
local scratch volume. Input copying requires its full expected byte count,
the attempt requires the **combined** lease temp reservation, and an OS-capped
child launch checks the tool-output budget again. Each leaves 512 MiB of
free-space headroom. This is not an atomic reservation or disk-I/O hard quota;
volume contention, hard filesystem confinement and production media-output
publication remain unapproved. No worker is registered or enabled.

## Before local acceptance

The coordinator is a *pre-activation foundation*. The remaining work is:

- A reviewed derivative-type adapter and **real atomic publication** with
  persisted revision/exclusion/lease ownership. Actual video proxy encoding
  belongs to WI-0189; the general adapter/result protocol belongs to WI-0188.
- A production registration and explicit enable/selection UX/API that cannot
  start video work until a publisher, hard source containment and quota
  enforcement have been approved.
- Adequate scratch disk/I/O/network/GPU policy, or measured CPU-only limits,
  validated no-recall behavior of the installed OneDrive provider.
- A real Windows photo-only versus photo+video mixed-load benchmark, search
  and slideshow latency measures, restart/cancel/cleanup verification.

No schema change here. The previously merged v35 schema still requires the
[current backup and isolated v34→v35 rehearsal](video-processing-control-v35.md)
BEFORE starting any new v35-based app against the maintained v34 catalogue.
