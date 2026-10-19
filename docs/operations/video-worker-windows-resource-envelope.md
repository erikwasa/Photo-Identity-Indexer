# WI-0188 checkpoint 8 — Windows Job Object resource envelope

**Status:** explicit-call, opt-in infrastructure only. There is no automatic
video worker, FFmpeg adapter, source file read, derivative publisher, queue
activation, or application setting that enables this execution path.

This checkpoint adds a Windows native Job Object controller to the existing
`VideoToolProcessSupervisor`, via the optional `WindowsJobLimits` member
of `VideoToolExecutionBudget`. Existing calls without that opt-in field
keep their former behavior. A production video worker **must not rely on
the optional defaults**: it requires a reviewed, mandatory resource profile.

## Native limits

An explicitly selected bounded profile requires:

| Limit | Allowed values | Enforcement mechanism |
|---|---:|---|
| Total job memory | 128–4096 MiB | `JOB_OBJECT_LIMIT_JOB_MEMORY` |
| Active processes | 1–8 | `JOB_OBJECT_LIMIT_ACTIVE_PROCESS` |
| CPU rate | 1–50% | `JOB_OBJECT_CPU_RATE_CONTROL_HARD_CAP` |
| Process tree lifecycle | mandatory | `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE` |

The supervisor sets all required Windows limits before launching its
subprocess. For capped profiles it now uses native `CreateProcessW` with
`CREATE_SUSPENDED`, assigns the suspended process to the job, opens a durable
wait handle, and only then calls `ResumeThread`. Standard handles are
inheritable Windows NUL devices: child output is never collected or logged.
If the platform cannot create/configure or assign the job, execution fails
closed; it does not silently continue without limits. The job handle is
closed during all exit paths, terminating surviving members. This is
additional to the existing wall-clock timeout, bounded scratch polling,
binary SHA-256 verification, sanitized output discard and process cancellation.
The limits apply to the Job Object and its members, not arbitrary other
programs or photo processes on the machine.

## Production-activation blockers

- **Startup containment:** the capped execution path now uses native
  suspended launch and no process instruction runs before job assignment.
  The older uncapped supervisor path remains for historical explicit callers;
  it is **not** acceptable for an enabled production worker.
  Native launch, handle inheritance and multi-process tests require Windows CI
  and maintained-machine inspection before activation.
- **Disk and I/O:** scratch size/entry polling is not a kernel-enforced
  filesystem quota; a process could temporarily allocate over budget or
  create files outside scratch. Provide a real contained scratch volume
  and read/write I/O limits before activation.
- **GPU and isolation:** Windows Job Objects do not independently enforce
  GPU time, network access, full filesystem access, process mitigations,
  user tokens or codec sandboxing. Design audited media-only arguments,
  low-privilege launch, allowed file access, and a no-hydration source-open
  authorization path.
- **CPU accounting:** a CPU-rate hard cap is a ceiling, not scheduling
  priority or a guarantee that photo searches remain responsive. Measure
  mixed workloads and retain the photo-priority polling/preemption gate.
- **Platform:** no non-Windows fallback is provided for a Windows-limited
  profile. A future Linux backend would need separate cgroup controls.

Tests validate bounds, native job-close process termination and supervised
opt-in execution/cancellation using already installed Windows system tools.
There are no private video files, OneDrive reads or new schema migrations.

## Maintained-host rollout

No local work is required solely for this library-only checkpoint. The
earlier **schema v35** migration remains mandatory before starting any v35
app on the maintained v34 catalogue: first a new authoritative backup and
[verified isolated v34 → v35 rehearsal](video-processing-control-v35.md).
Keep the existing maintained v34 application in service until that gate
passes. Full WI-0188 Windows video/photo mixed-load testing remains deferred
until the actual video worker, media adapter and safe publisher exist.
