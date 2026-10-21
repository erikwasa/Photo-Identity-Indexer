# WI-0188 — native Windows creation-mitigation compatibility pilot

**Development experiment only.** A passing Windows Job Object CPU/memory,
process-count, wall-time and transfer-counter test does **not** make
FFmpeg a low-privilege or network-denied process. The remaining
filesystem, network, hard disk/I/O and measured mixed-photo-load gates
are separate requirements for authorizing the first original-video trial.

## Proposed additional process creation boundary

The existing `WindowsVideoSuspendedLauncher` already creates approved
processes suspended, assigns them to the native Windows Job Object
**before** resuming any instruction, passes only Windows system
environment variables, a private `TEMP` directory and an allowlisted
inherited NUL standard handle. It still runs FFmpeg under the same
Windows security identity as its calling application.

This optional checkpoint adds
`WindowsVideoJobLimits.RequireChildProcessRestriction` (default
**false**). For opted-in processes only, the native launcher
installs three `STARTUPINFOEX` attributes **before** `CreateProcessW`:

| Attribute | Requested Windows policy |
| --- | --- |
| `PROC_THREAD_ATTRIBUTE_HANDLE_LIST` | Existing single NUL handle only |
| `PROC_THREAD_ATTRIBUTE_CHILD_PROCESS_POLICY` | `PROCESS_CREATION_CHILD_PROCESS_RESTRICTED` |
| `PROC_THREAD_ATTRIBUTE_MITIGATION_POLICY` | Disable legacy extension points, reject remotely loaded images and low-integrity-label images |

The two added mitigation attributes use native fixed-size DWORD / ULONG64
buffers that remain allocated until the process attribute list is
destroyed. If initialization or either policy installation fails,
the native launch fails **closed**. The existing kill-on-job-close,
SHA-256 tool validation, bounded scratch, source/derivative guards,
sanitized errors and CPU/memory/I/O caps remain unchanged.

## Deliberate scope

For this checkpoint, **only the synthetic fixture FFmpeg generator and
the synthetic candidate encoder** opt into these policies. The existing
real-video selected one-shot FFmpeg, independent FFprobe, all photo
processing and the general uncapped supervisor **do not** opt in.
This is intended to verify compatibility and the concrete native
child-process denial first, without touching maintained originals.

The new Windows unit test compares the same harmless
system `cmd.exe` nested launch both *without* and *with*
`RequireChildProcessRestriction`, under a Job Object allowing
two processes: the unrestricted nested call must succeed; the restricted
one must be rejected. It also proves a single first-party process still
runs normally and scratch teardown succeeds. This is a compatibility
and basic native process-policy assertion, not a hostile-code bypass test.

The optional real Windows FFmpeg test remains the prior strictly marked
synthetic-only command. **After a fully green CI and PR merge**, the
maintainer can reuse the already approved physical/unlinked FFmpeg and
FFprobe binaries, unchanged approved hashes and the existing owned
synthetic scratch directory:

~~~powershell
git switch main
git pull --ff-only origin main
# Confirm the dedicated $lab still contains only its ownership marker:
Get-ChildItem -LiteralPath $lab -Force | Select-Object Name

# First perform the no-side-effect preview.
dotnet run --project src/PhotoIdentity.Cli -c Release -- @argsPilot
if ($LASTEXITCODE -ne 0) { throw 'Synthetic mitigation preview failed.' }

# This --apply writes/deletes synthetic footage ONLY, not real videos.
dotnet run --project src/PhotoIdentity.Cli -c Release -- @argsPilot --apply
if ($LASTEXITCODE -ne 0) { throw 'Synthetic mitigation pilot failed.' }
~~~

Reusing `$argsPilot` requires the unchanged approved **physical** binary
paths and hashes from the prior successful Windows synthetic roundtrip.
Never reselect a Winget symlink, launch a real archive source, or discard
an unknown scratch directory. Submit only the sanitized JSON outcome,
not source/executable paths, private log text, or media details.

## Security limitations and remaining WI-0188 acceptance

Microsoft explicitly warns that
`PROCESS_CREATION_CHILD_PROCESS_RESTRICTED` can be bypassed by code
with access to powerful process handles; it is *effective only in
combination with a genuine sandbox boundary* such as AppContainer.
The existing restricted inherited handle list helps but does **not**
replace an AppContainer token, filesystem ACL, low-privilege identity
or independent privilege audit.

These attributes do **not** prohibit arbitrary outbound sockets,
filesystem access under the caller's permissions, GPU usage or
unbounded writes outside the observed scratch directory. They do
not enforce hard storage capacity or I/O-rate quotas. Windows Job Object
observed I/O transfer counters remain sampled fail-closed diagnostics,
not OS-enforced disk limits.

**Do not authorize real video `one-shot enqueue --apply` or
`one-shot run --apply` on this evidence.** The next planned
checkpoint is an explicit AppContainer/low-privilege launcher
feasibility and ACL/capability audit on owned **synthetic** files,
followed by a separately reviewed dedicated bounded storage location,
low-privilege network-denial test, and finally a one-video mixed-photo
measurement / cancellation / exclusion / restart / post-source-integrity
trial.

References:

- [Microsoft: UpdateProcThreadAttribute](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-updateprocthreadattribute)
- [Microsoft: Launch an AppContainer](https://learn.microsoft.com/en-us/windows/win32/secauthz/implementing-an-appcontainer)
- [Existing Windows Job Object resource envelope](video-worker-windows-resource-envelope.md)
