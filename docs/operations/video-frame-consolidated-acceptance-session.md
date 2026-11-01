# WI-0190 checkpoint 16 — measured frame runs and controlled interruption

These commands are Windows-only, operator-invoked tooling for one
consolidated maintained-host acceptance campaign. Nothing is scheduled
automatically. Selected processing still requires a current, registered
local SDR playable proxy and independently approved pinned FFmpeg, CLIP
and Ollama model digests. Azure is an optional operator-controlled
bridge; unknown third-party endpoints are not accepted.

## What is measured

video frames acceptance-harness --mode measure starts ONLY the same
PhotoIdentity CLI's selected-one-shot subprocess (not an arbitrary
executable or PID). Its JSON reports wallTimeMs for the complete
child process, cliHostCpuTimeMs from OS cumulative CPU time of that
child .NET process, and cliHostPeakWorkingSetBytes. It NEVER captures
or prints stdout/stderr from the child: even unexpected private
model paths and output are discarded.

**Important resource attribution:** CLI host CPU excludes the separate
FFmpeg child, locally running Ollama and any remote Azure inference
CPU/GPU. The harness cannot see Azure billing. It deliberately
reports cpuTimeMs=null, gpuTimeMs=null and azureCostMinorUnits=null,
rather than inventing complete numbers. The acceptance-report manifest
can use a successful exact job's wallTimeMs as executionWallMs, noting
that it includes process startup. It MUST NOT use cliHostCpuTimeMs as
full cpuTimeMs. Measure cumulative system/FFmpeg/local model CPU and
GPU on the relevant hosts, and allocate independently verified Azure
billing in the chosen currency's minor units; otherwise leave those
manifest fields null. GPU utilization percent is not GPU processor
time, and Azure instance list price is not measured clip cost.
This separation is essential for credible WI-0190 results.

A child exit code of 0 is not proof of a current v38 batch. Confirm
separately using the existing read-only video frames audit, coverage,
evaluate-live and acceptance-report commands. All private media,
paths, job IDs, captions and judgments remain outside GitHub.

## Instrumented selected-one-shot — Windows PowerShell

First follow the existing
[selected one-shot runbook](video-frame-evidence-selected-one-shot.md)
to construct a valid $frameArgs array, its previously approved
model/proxy/cache inputs and a NEW owned empty scratch root under
OS temp. For a four-scene job follow
[four-frame sampling](video-frame-four-scene-selected-one-shot.md).
Confirm the video/photo queues are idle and the selected scope is
disabled, paused and unselected. Start with a normal selected-one-shot
dry-run (do not add --apply to that dry-run).

The following uses the full selection flags from that earlier
runbook. The harness has a separate --execute gate even when the
selected command carries its own --apply and two acknowledgments.

~~~powershell
git switch main
git pull --ff-only origin main

$selectedPosition = [Array]::IndexOf($frameArgs, "selected-one-shot")
if ($selectedPosition -lt 0) { throw "Missing frame selection." }
$selectedArgs = @($frameArgs[($selectedPosition + 1)..($frameArgs.Length - 1)])

$approvedSelectedArgs = @(
    $selectedArgs
    "--apply"
    "--acknowledge-isolated-v38-rehearsal"
    "--acknowledge-frame-disclosure"
)

$harness = @(
    "run", "--project", "src/PhotoIdentity.Cli", "-c", "Release", "--",
    "video", "frames", "acceptance-harness", "--mode", "measure"
)

# Independent preview: NO process spawned and NO database/media/model work.
dotnet @harness -- @approvedSelectedArgs

# Only with separate approval to actually process:
# $measurement = dotnet @harness --execute -- @approvedSelectedArgs
# $measurementExit = $LASTEXITCODE
# $measurement
# "Exit code: $measurementExit"
~~~

The selected child independently performs all existing 13-minute
attempt, 15-minute scoped authorization, local-source/exclusion,
photo-priority, FFmpeg Job Object, digest, scratch and all-or-nothing
v38 publication checks. Reusing an already-succeeded FrameEvidence
identity is not a fresh inference run: select distinct policy/job
identities and approved clips as needed. Do not repeat the already
accepted two-frame 12.3-second audit simply for this checkpoint.

## Hard-interruption study — disposable isolated PostgreSQL only

This is intentionally destructive to ONE newly spawned private
FrameEvidence child. The harness uses Windows
Process.Kill(entireProcessTree:true) on the child it started; it
does NOT accept a PID, arbitrary executable, process name or path.
A remote Azure request previously issued might continue/bill after
local termination; remote cancellation is not proven.

Use a separate rehearsed **disposable PostgreSQL database** with an
approved disposable registered proxy and a NEW marker-owned empty
scratch root. Do not use a main shared photo catalogue, a selected
succeeded job or a running production video job. Choose a delay
5–600 seconds that reaches actual processing after the child's
preflight. With private $interruptSelectedArgs containing the same
exact selected-one-shot flags and both acknowledgments:

~~~powershell
$interruption = @(
    "run", "--project", "src/PhotoIdentity.Cli", "-c", "Release", "--",
    "video", "frames", "acceptance-harness", "--mode", "interrupt",
    "--terminate-after-seconds", "30"
)

# Preview cannot start or terminate any process.
dotnet @interruption -- @interruptSelectedArgs

# Only after explicit operator approval:
# dotnet @interruption --execute --acknowledge-owned-child-termination -- @interruptSelectedArgs
~~~

The child may finish first; that is reported as
child-exited-before-interruption, not successful termination.
A positive owned-child-terminated report proves only termination
of the locally owned process tree, not PostgreSQL job recovery,
Azure GPU shutdown or scratch reclamation.

## Read-only independent interruption audit

Find the new disposable FrameEvidence job UUID locally, without
posting IDs, sources or media to PR logs:

~~~sql
SELECT id, status, requested_frames, created_at_utc
FROM video_processing_jobs
WHERE derivative_kind='FrameEvidence'
ORDER BY created_at_utc DESC LIMIT 10;
~~~

~~~powershell
$frameEvidenceJob = "PASTE-DISPOSABLE-FRAME-EVIDENCE-JOB-UUID"
$interruptionAudit = @(
    "run", "--project", "src/PhotoIdentity.Cli", "-c", "Release", "--",
    "video", "frames", "interruption-audit",
    "--postgres-connection-env", "PHOTOIDENTITY_WI0190_CONN",
    "--database", "photoidentity_disposable",
    "--frame-job-id", $frameEvidenceJob,
    "--scratch-root", $frameScratch
)
dotnet @interruptionAudit
"Audit exit code: $LASTEXITCODE"
~~~

The audit uses a repeatable-read **READ ONLY** local PostgreSQL
transaction and a non-deleting scratch inspection. It reports
job status, selected-scope authorization, active lease, batch and
durable row counts, and owned/young/recoverable/unknown scratch
counts. It never fetches original media, captions, vectors or
model content. It cannot migrate the schema, disarm a selected
scope, recover leases or remove any scratch.

Immediately after killing a child, an active lease or enabled
selected control is EXPECTED and causes exit 1, even if the
absolute selected authorization has expired. A partial batch or
unexpected published rows must also cause exit 1. A safe
interruption acceptance requires a failed/cancelled terminal job,
no published batch or frames, fenced lease/grant, disabled/paused/
unselected control and zero owned/unrecognized workspace children.

To finish recovery in the deliberately isolated disposable
database, the existing video jobs stop --apply disables the
control and cancels all outstanding queued jobs, and video jobs
recover --apply reclaims expired leases after their deadline.
**Both operate on OTHER video jobs in that database as well.**
That is why they must NEVER be used on the maintained shared
catalogue as part of this interruption campaign. On any shared
catalogue, stop and request a separately reviewed targeted plan
instead of performing these global actions.

The owned scratch recovery gate is **intentionally age-gated**:
a hard-terminated workspace younger than six hours is NOT
eligible for deletion, even with --apply. The audit may report
scratch-not-clean until this cooldown has passed. Review and
recover it later under the existing
[marker and lock recovery runbook](video-frame-scratch-crash-recovery.md).
Do not backdate marker files or weaken the age threshold just to
finish in one sitting. A later audit exits 0 only after every
state and scratch gate is satisfied.

## Tests, acceptance and limitations

CI uses only synthetic local Windows cmd children for output
redaction and real Process.Kill(entireProcessTree:true) testing.
Pure tests verify explicit consent, no-apply preview, invalid
target commands, full/partial evidence and lease/grant/refusal
reasons. A disposable PostgreSQL integration test verifies the
read-only audit and active-grant refusal. No tests send media
to Azure, OneDrive or external AI applications.

For formal WI-0190 closeout the maintainer must still execute
the representative private video set, judge captions and
retrieval quality, annotate missed scenes, allocate actual
system/GPU/Azure cost, and observe the real interruption and
deferred recovery steps. Successful CI proves the tooling's
synthetic boundaries, not those real-world measurements.
