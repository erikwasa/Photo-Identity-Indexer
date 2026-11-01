# WI-0190 checkpoint 14 — explicit crashed-frame scratch recovery

The selected one-shot frame producer normally cleans its own temporary
workspace in a `finally` block. An abrupt PowerShell/process termination,
power loss or OS crash does **not** execute `finally`; a leftover child
directory could then block the next run's required **empty scratch root**.
Checkpoint 14 adds an explicitly invoked **local-only** recovery procedure,
without scheduling a janitor or authorizing processing any new media.

## Boundaries

- The root must be the pre-existing, physically local,
  `photoidentity-frame-evidence-<random>` direct child of OS temp,
  containing the **exact** trusted
  `.photoidentity-frame-evidence-root` ownership marker. Never point
  at the media archive, original photos, OneDrive folder or private proxy
  cache. Unknown roots cannot be recovered.
- New producer workspaces carry a private, exact
  `.photoidentity-frame-evidence-workspace` marker and an OS-exclusive
  `.photoidentity-frame-evidence-active.lock` held for the entire job.
  Normal completion closes the lock and removes the workspace.
  An abnormal process exit closes the lock at the OS level, but leaves
  its marker and scratch payload behind.
- Recovery only recognizes `wi0190-evidence-<32 lowercase GUID hex>`
  workspaces with **both** expected markers, regular local files,
  no symlinks/junctions/reparse descendants, at most 256 entries and
  at most 320 MiB of contents. Older legacy/unmarked folders from
  previous builds are **not** authorized for automatic deletion.
- An untouched workspace marker must be at least **24 hours old**
  by default; the operator can specify **6–720 hours**. The hard
  6-hour lower bound is well beyond the currently authorized
  13-minute frame job cap. Future timestamps and actively locked
  workspaces are never recovery candidates.
- By default the command **only inspects**. Any unrecognized
  child entry makes recovery report `blocked=true`, and an apply
  request will not remove **any** candidate in that root.
- Applying also requires both `--apply` and
  `--acknowledge-private-scratch-deletion`. Just before each
  removal, ownership, age, link safety and inactive lock are
  checked again. No frame captions, vectors, private file names,
  directory names or original paths are returned in JSON.
- The cleanup does **not** open PostgreSQL, delete published frame
  evidence, change leases, open sources, perform OneDrive hydration,
  call Azure/Ollama, run FFmpeg or enqueue video work.
  The separate v38 lease/recovery checks remain authoritative
  for database job state.

## Windows procedure

Use the **same owned scratch root** that was passed to a previous
`video frames selected-one-shot` command. It is the OS-temp folder
you explicitly created, **not** the permanent PhotoIdentity cache.
Never paste personal paths or file names into CI/PR logs.

~~~powershell
git switch main
git pull --ff-only origin main

# Replace this example with the actual *previously approved*
# PhotoIdentity frame scratch root under [IO.Path]::GetTempPath().
$scratchRoot = Join-Path ([IO.Path]::GetTempPath()) "photoidentity-frame-evidence-PASTE-ROOT-ID"

$command = @(
    "run", "--project", "src/PhotoIdentity.Cli", "-c", "Release", "--",
    "video", "frames", "scratch-recover",
    "--scratch-root", $scratchRoot,
    "--minimum-age-hours", "24"
)

# Read-only preview. Check owned, recoverable, active, young and refused counts.
dotnet @command
"Preview exit code: $LASTEXITCODE"

# Optional and explicitly destructive: only after confirming:
#   1. No selected frame one-shot is running;
#   2. The exact root is correct, marker-owned and no unknown entries appear;
#   3. The old workspaces are disposable temporary copies, not originals.
# dotnet @command --apply --acknowledge-private-scratch-deletion
# "Apply exit code: $LASTEXITCODE"

# Verify the result by running the preview again; it should report
# zero recoverable owned orphan workspaces.
~~~

On the maintained host, if the **existing scratch root is empty
except for its root marker**, no action is necessary. If it contains
unmarked workspaces from an older build, the recovery command
intentionally refuses deletion. Inspect those locally and use a
separately reviewed manual cleanup plan; do not bypass marker
checks by renaming folders or fabricating markers.

## Testing and acceptance

Synthetic filesystem tests exercise live locks, age refusal, normal
root ownership, distinct orphan cleanup, unknown/unmarked child
blocking, symlink descendants, explicit apply acknowledgments
and redacted numeric output. The tests do not run FFmpeg, models
or PostgreSQL and never use personal media.

This is **one** of the abnormal termination safeguards, not a
complete real-world interruption acceptance. A consolidated
Windows test still needs an *authorized disposable registered proxy*
with a real running frame job, explicit cancellation or process kill
during model inference, independent verification that no partial
batch is published, that the selected-job authorization cannot
linger past its expiry, and that the scratch workspace is either
normally cleaned or recovered according to this runbook.

Do not repeat the already accepted 12.3-second successful
two-frame frame audit solely for PR review. WI-0190 remains
`in_progress` with **0/4 full formal acceptance** until real
representative media and interruption/recovery have been validated.
