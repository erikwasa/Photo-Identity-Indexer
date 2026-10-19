# WI-0188 checkpoint 7 — supervised video execution preemption

**Implementation-only checkpoint; no active video worker.** This update introduces
an explicit-call `VideoGuardedToolExecutor` bridge and a read-only PostgreSQL
lease safety probe. It does not enqueue videos, open original media, hydrate
OneDrive, publish derivative files, register a hosted worker or expose a CLI
command to run FFmpeg. The video lane remains disabled and paused by default
under schema v35.

## Execution guard

Only a future explicitly authorized worker may call this component, passing:
- A verified leased job and its PostgreSQL `PostgresVideoLeaseSafetyProbe`
- An independently authorized absolute tool path and digest
- A locally isolated scratch root and audited argument builder
- The existing process supervisor's bounded execution budget

The bridge checks the safety probe **before starting** a child process,
periodically every 100 ms to 1 second while it runs, and again after the
child exits. A negative decision cancels the linked tool token, asking the
existing supervisor to kill the process tree and clean its private scratch
directory. An unexpected database/probe exception also stops execution
rather than assuming permission. The result contains only an outcome code
and exit status; no private paths, tool logs or metadata.

The PostgreSQL safety probe reads a *single snapshot query* of the
authoritative video control generation, disabled/paused state, photo
advancement and processing activity, current visible supported revision,
source-copy exclusion, cancellation intent and exact unexpired job lease.
It never initializes/migrates a database, touches photo jobs or files, or
uses client-supplied claims of photo idleness.

## Important safety limitations

- Periodic checks have a bounded latency; they **cannot atomically prevent**
  a photo job starting between polling intervals. No video subprocess is
  actually started by the application in this checkpoint.
- This is not a CPU/GPU/memory/IO quota, OS sandbox or hard disk quota.
  A future worker must provide real operating-system controls and measure
  photo processing latency under simultaneous loads.
- It does not make a video derivative publishable. A future publishing
  transaction must recheck the latest revision, exclusions and lease,
  guarantee atomic file/database consistency, and safely delete crash
  remnants. A final positive probe is not a source-open lease.
- An external process that cannot be verified terminated raises a failure
  and preserves scratch for controlled investigation; it must **not** be
  treated as a successful or safe job completion.

## Tests

- Windows Core tests use `cmd.exe` with a verified SHA-256 to show a
  synthetic photo-priority signal terminates a deliberately long-lived
  subprocess and cleans its scratch; a failed probe also terminates it
  without exposing the probe's private error message.
- Core tests also cover denied startup, invalid intervals and successful
  completion requiring a final observation.
- Disposable PostgreSQL tests cover a claimed job becoming invalid after
  pause, a changed control generation, a photo job arriving mid-lease,
  bogus lease tokens and durable stop/cancellation.

## Operator notes

**No new migration beyond v35** is introduced by this checkpoint. Since the
maintained environment was last verified at v34, the previously documented
[required v34 → v35 backup and isolated migration rehearsal](video-processing-control-v35.md)
must occur before starting a v35-based application. Continue using the old
v34 application until that gate passes; CI tests do not authorize starting
v35 code against the maintained catalogue.

There is no new local command or test required solely for checkpoint 7.
Combine full photo/video mixed-load acceptance testing with the later
runtime worker milestones.
