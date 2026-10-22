# WI-0188 — disposable Windows AppContainer file-boundary feasibility lab

**Status: experimental, opt-in and synthetic-only.** This command does not
enable an unattended video worker, trust FFmpeg media inputs, create jobs,
open private archives, hydrate OneDrive, use a database, or publish a video
derivative. It is a genuine Windows token/ACL *feasibility test*, not the
completed WI-0188 isolation boundary.

## Why this experiment exists

The existing native video launcher uses a one-process kill-on-close
Windows Job Object, CPU/memory limits, sampled read/write transfer ceilings,
a bounded working directory, sanitized environment and inherited-handle
allowlist. A Job Object does **not** restrict which other user files the
child can open or prevent network egress.

PR #648 tested optional process-creation mitigations and observed
`cmd.exe` DLL initialization failure `0xC0000142` under Windows CI.
Those mitigations remain default-off and are **not** enabled for this
AppContainer probe. The development goal here is to test an actual
AppContainer security **token** with only explicitly granted paths.

Microsoft's AppContainer model uses two principals: the caller's user SID
and the new Package SID must both have appropriate access. The Windows
`PROC_THREAD_ATTRIBUTE_SECURITY_CAPABILITIES` creation attribute
instructs CreateProcess to use an AppContainer token. No network
capabilities are granted in this lab.

## New command and exact scope

```powershell
# Existing dedicated $lab from the successful synthetic codec pilot may
# be reused ONLY if the ownership marker is its sole filesystem entry:
Get-ChildItem -LiteralPath $lab -Force | Select-Object Name
# Expected: .photoidentity-synthetic-video-lab

dotnet run --project src/PhotoIdentity.Cli -c Release -- `
    video sandbox appcontainer-lab --scratch-root $lab
# Expected: dryRun=true, status=not-run, no profile or file changes.
```

The `--apply` option **does not encode media**. It authorizes only this
bounded, generated-text-file Windows experiment:

```powershell
dotnet run --project src/PhotoIdentity.Cli -c Release -- `
    video sandbox appcontainer-lab --scratch-root $lab --apply
if ($LASTEXITCODE -ne 0) {
    throw 'Synthetic AppContainer feasibility remains unverified.'
}
```

Both invocations require the previously marked and otherwise empty
**OS-temporary-directory child**. The guard rejects unmarked, occupied,
linked, remote, source/archive or non-temporary roots. Do not create the
marker under the family photo archive, OneDrive sync tree, or any
folder containing media.

The explicit apply performs only these actions:

1. Creates three new, clearly synthetic directories inside the marked lab:
   working, allowed input, and ungranted input.
2. Creates a **unique disposable** user AppContainer profile and obtains its
   Package SID. It requests **zero** capability SIDs, including zero
   network capability SIDs.
3. Grants the Package SID noninheriting root traversal, read/execute access
   (inherited by one generated input file), and modify access only to the
   new working directory and descendants. The sibling ungranted input
   gets **no** AppContainer ACL grant.
4. Before any launch, independently audits the generated root, allowed-input,
   working and ungranted-input DACLs plus both generated files. It requires
   exactly the intended AppContainer SID rules, no inherited access to the
   blocked files, no write grant to the allowed read-only file, and no
   wildcard `ALL APPLICATION PACKAGES` / `ALL RESTRICTED APPLICATION PACKAGES`
   grants. Unexpected permissions produce a nonpassing fixed status without
   starting a subprocess. This audit covers only the disposable lab, **not**
   the actual archive, executable/DLL paths or private cache.
5. Uses the already-built suspended native launcher with one-process Job
   Object and fixed Windows system `cmd.exe`—**not caller-selected
   FFmpeg**—to copy only the generated *allowed* text input to its owned
   scratch folder. Before resuming, it opens the suspended child token and
   requires `TokenIsAppContainer = 1`. A missing token check **kills the
   suspended child**; no unrestricted fallback is permitted.
6. If the allowed copy succeeds, repeats the exact same process profile
   against the ungranted synthetic sibling. Unexpected access to the
   ungranted bytes is a hard **failure**.
7. Deletes all newly created scratch folders and the disposable Windows
   AppContainer profile. A cleanup failure is not accepted as PASS;
   it is reported as `sandbox-cleanup-unverified`. No arbitrary directory
   cleanup is performed.

The command uses the existing supervisor's timeout, scratch-count/size
limits, job-wide memory/CPU/process cap, native process join-before-resume,
and sampled read/write transfer ceilings. No archived or online-only
video source can be selected through the parser.

## How to interpret the sanitized JSON

Only fixed status codes and boolean safety fields are printed. No
profile name, SID, private paths, Windows exception messages, hashes,
generated text, credentials, subprocess stdout/stderr or process arguments
appear in the result.

- `file-boundary-observed` means: the launcher independently verified a
  live AppContainer token **before resume**, the generated allowed read
  and scratch write worked, a generated ungranted read failed, and the
  owned scratch and profile were cleaned. This is **not** whole-system
  sandbox acceptance.
- `profile-unavailable`, `sandbox-acl-unavailable`,
  `sandbox-launch-unavailable` or `allowed-read-write-unavailable`
  means no positive file-boundary evidence was established. Some Windows
  host configurations do not permit unpackaged Win32 AppContainer tools
  to initialize.
- `sandbox-acl-audit-failed` is a nonpassing ACL configuration result:
  do not launch any synthetic child until the expected AppContainer SID
  permissions and denied sibling have been verified.
- `ungranted-read-not-denied` is a security regression: never authorize
  production if an ungranted synthetic file can be read.
- `sandbox-cleanup-unverified` means the persistent Windows profile
  or owned lab cleanup could not be proven; inspect the host under
  controlled operator procedures rather than silently deleting unrelated
  profiles.

Every response includes `syntheticAclAuditVerified`, which can be true
only after a fresh on-disk DACL inspection of generated test paths. This
is **not** `independentAclAuditVerified` for real private archives/cache.

Every response states `networkCapabilitiesRequested=false`, but also
`networkEgressDenialVerified=false`, `independentAclAuditVerified=false`,
`hardDiskIoQuotaVerified=false`, `originalsOpened=false`,
`jobsEnqueued=false` and `realVideoExecutionAuthorized=false`.

**No networking is attempted** by this experiment. Zero requested
capabilities are not independent proof of egress denial; future work must
demonstrate a denied socket to a locally controlled network listener and
prove that no unintended exemptions apply.

## What must still be developed before real media

- AppContainer or LPAC compatibility with the **actual approved FFmpeg and
  FFprobe binaries**, loaded codecs, file-only argument policy and exact
  staged-input/output paths. The current test uses the Windows system
  command interpreter only.
- Independent ACL review of the actual launch identity, original archive
  nonaccess, staged-local input read and private output write rights;
  reject broad `ALL APPLICATION PACKAGES` grants and unintended token
  privileges. Keep the original video outside the sandbox in all cases.
- Verified network egress denial, including loopback and child-process
  attempts, without weakening AppContainer capabilities; bounded scratch
  volume/OS storage quota and observed parent input staging/output
  publication risks.
- One **separately authorized** already-local supported video trial,
  photo-only vs mixed workload p95/throughput acceptance, exclusion,
  cancellation/restart, lease-fenced publication and independently
  checked post-run original SHA-256/no-hydration.

WI-0188 remains `in_progress`, and both `video one-shot enqueue --apply`
and `video one-shot run --apply` remain **not authorized** solely
because a synthetic AppContainer test passes.

## Public Windows API references

- [Microsoft: Launch an AppContainer](https://learn.microsoft.com/en-us/windows/win32/secauthz/implementing-an-appcontainer)
- [Microsoft: CreateProcessW](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-createprocessw)
- [Microsoft: UpdateProcThreadAttribute](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-updateprocthreadattribute)
