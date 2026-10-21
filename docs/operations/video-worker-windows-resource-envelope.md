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
the inherited Windows NUL device only, through a
`PROC_THREAD_ATTRIBUTE_HANDLE_LIST` allowlist: child output is never
collected or logged, and unrelated parent handles are not inherited.
If the platform cannot create/configure or assign the job, execution fails
closed; it does not silently continue without limits. The job handle is
closed during all exit paths, terminating surviving members. This is
additional to the existing wall-clock timeout, bounded scratch polling,
binary SHA-256 verification, sanitized output discard and process cancellation.
The limits apply to the Job Object and its members, not arbitrary other
programs or photo processes on the machine.

## Optional observed per-job read/write transfer budget — 2026-10-21

The opt-in Windows Job Object now supports paired
`MaximumObservedReadBytes` and `MaximumObservedWriteBytes` counters
(1 MiB–2 GiB per direction). If configured, the supervisor calls
`QueryInformationJobObject(JobObjectExtendedLimitInformation)`
during process execution and **after process exit, before any
post-exit validation or publication callback**.

The kernel-reported `ReadTransferCount` / `WriteTransferCount`
values cover the supervised Job Object, not the host's whole
filesystem. When a count exceeds its configured maximum, the tool
is stopped with the allowlisted `io-budget-exceeded` checkpoint.
If the OS query is unavailable or returns incomplete data, the
process fails closed with `io-accounting-unavailable`, and the
existing kill-on-close Job Object cleanup is preserved. Exposed
successful tool evidence may contain only these numeric counter
values, never archive paths, child output or private filenames.
When no limits are set, historical synthetic/test callers retain
their prior behavior.

The explicitly selected one-shot's FFmpeg now uses **512 MiB
observed read and 384 MiB observed write** limits. Independent
FFprobe uses **512 MiB observed read and 32 MiB observed write**
limits. The one-process memory (768 MiB/512 MiB), CPU (15%/25%),
9-minute/15-second deadlines and source/temporary volume
budgets stay unchanged. The original's verified private staging
occurs outside the child Job Object, but remains separately
size-bounded and no-recall.

**Important: This is a sampled, cumulative diagnostic and abort
condition, NOT a kernel-enforced peak allocation, instantaneous
bandwidth throttle, per-volume quota, firewall or sandbox.**
An untrusted process can exceed the count between polls (at
most the configured 1-second interval), mmap I/O may not be
fully represented by transfer counters, and a child could
write outside the scratch folder without a low-privilege
filesystem boundary. The host process's input staging and
private publication I/O are not counted in the child job.
A hard independent disk allocation/I/O and network containment
review remains a separate blocker for production activation.

**OS design restriction:** Microsoft's
`SetIoRateControlInformationJobObject` function is explicitly
unsupported on Windows 10 version 1607 and newer, so this
work does not attempt to use that deprecated rate-control API.
Reference: https://learn.microsoft.com/en-us/windows/win32/api/jobapi2/nf-jobapi2-setioratecontrolinformationjobobject

## Synthetic-only native process-creation mitigations (2026-10-22)

The supervised suspended launcher now supports an optional native
`RequireChildProcessRestriction` flag. It installs an explicit Windows
child-process denial attribute, **before** `CreateProcessW`
and before resuming the restricted tool. If the attribute is
unsupported or rejected, the launch fails closed. The initial
combined remote-image/low-label/extension-point mitigations caused
`cmd.exe` DLL initialization failure (`0xC0000142`) in Windows
CI, so they have been removed from the experimental profile rather
than being incorrectly treated as compatible.

Initially **only the synthetic encoder/generator** enables this flag.
The selected one-shot FFmpeg/FFprobe and ordinary production paths retain
their existing settings until Windows CI and the separately approved
FFmpeg synthetic host trial demonstrate compatibility. This is defense
in depth; Microsoft's documented child-process restriction is not
considered complete isolation without a restricted token and inaccessible
privileged process handles. It does not confer network denial, file ACL
restriction, hard storage quota or GPU isolation. See the
[mitigation pilot and AppContainer follow-up](video-windows-native-mitigation-pilot.md).

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
