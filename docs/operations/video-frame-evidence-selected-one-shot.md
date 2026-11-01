# WI-0190 checkpoint 7 — selected, opt-in FrameEvidence one-shot

**Checkpoint 10 update (2026-11-01):** The existing interval default is unchanged. A separately documented optional manual scene-cut mode now persists scene-balanced evidence with a distinct regenerable sampling job identity. See the [annotated-scene one-shot](video-frame-evidence-annotated-one-shot.md). The existing verification of the interval happy path remains accepted; new mode requires its own end-to-end acceptance before WI-0190 closure.


**Maintainer happy-path verification accepted (2026-10-31).**
After the stopped maintained catalogue was migrated to PostgreSQL v38,
the operator completed an explicit two-frame one-shot using a physical
approved FFmpeg executable, local CLIP and a controlled Azure Ollama bridge.
It returned published=true, exit 0. PostgreSQL showed two durable
FrameEvidence rows with 512-dimensional embeddings. Only the original
scratch marker remained; video controls returned to disabled, paused and
unselected; photo/video work stayed idle. The operator reported bridge
shutdown and Azure scale-down. This verifies the checkpoint 7 happy path,
**not** the four full WI-0190 acceptance criteria. See the
[work-item verification record](../delivery/work-items/WI-0190-video-frame-caption-embedding.md).

**State: Maintained-host happy path accepted; broader WI-0190 acceptance remains open.** A single operator-selected
**already-local, already-published SDR playable proxy** can now be sampled,
captioned, embedded and published into the private PostgreSQL v38 frame
evidence tables. The command never opens an original, hydrates OneDrive,
selects an unrelated queued video job, enables automatic processing,
exposes captions or frame pixels in CLI output, or uses an unvetted
third-party model endpoint. A registered proxy JPEG **is private data**;
an operator-managed Azure Ollama bridge deliberately forwards it only
when separately authorized.

Unlike the older `video one-shot run` path, which remains hard-disabled
for original-media execution, this command uses **only the already
published SHA-verified local proxy**. The separate source-media isolation
gate is NOT changed.

## Prerequisites

- The maintained **v37→v38 isolated PostgreSQL backup/restore rehearsal
  previously passed** and the active catalogue is now at schema v38.
- Photo Identity is not performing photo jobs; video controls are
  **disabled, paused and unselected**, and no other video lease runs.
  Run only one instance of this operator command.
- Existing current Rec.709 playable-proxy receipt UUID (NOT an original
  source file path). The earlier read-only registered-proxy pilot may be
  used to identify a previously verified candidate.
- Physically resolved, nonlinked FFmpeg, CLIP model/tokenizer files and
  verified expected hashes. Local Ollama or an explicitly controlled
  Azure bridge on `http://127.0.0.1:11434`.
- Sufficient local fixed-disk scratch space, with 512 MiB unrelated
  free-space headroom. A separate operator-created **empty**
  `photoidentity-frame-evidence-*` scratch root immediately beneath
  OS temp, containing only the exact marker shown below.
- **Checkpoint 15 supports 1–4 frames** per explicitly selected attempt. The original two-frame happy path remains accepted and unchanged. Four frames may time out under the existing 13-minute attempt and 15-minute absolute grant; see [four-scene selected one-shot](video-frame-four-scene-selected-one-shot.md).
  The existing job lease is renewed every 10s, with at most 13 minutes
  per attempt, a 15-minute absolute selected scope, independent 60-second
  FFmpeg budgets and 180 seconds per model inference call.

## Windows PowerShell procedure

Start at the repository root on current main. Reuse the existing private
proxy cache root and pinned CLIP files from the previous successful pilot.
**Load FFmpeg and FFprobe paths/hashes from the persistent launcher
approval**, not Get-Command or a WinGet alias. See the
[approved pinned video tools guide](ffmpeg-approved-video-tools.md).
Keep database credentials exclusively in the process environment;
never paste connection strings, original paths or captions into a PR.

```powershell
git switch main
git pull --ff-only origin main

$approved = .\Get-PhotoIdentityApprovedVideoTools.ps1
$ffmpeg = $approved.FfmpegPath
$ffmpegSha = $approved.FfmpegSha256
$ffprobe = $approved.FfprobePath
$ffprobeSha = $approved.FfprobeSha256

# A new, dedicated root. Do not reuse the synthetic pilot scratch.
$frameScratch = Join-Path ([IO.Path]::GetTempPath()) (
    "photoidentity-frame-evidence-" + [guid]::NewGuid().ToString("N")
)
New-Item -ItemType Directory -Path $frameScratch | Out-Null
[IO.File]::WriteAllText(
    (Join-Path $frameScratch ".photoidentity-frame-evidence-root"),
    "wi0190-registered-proxy-frames-v1"
)

$clipSha = (Get-FileHash -LiteralPath $clip.ModelPath -Algorithm SHA256).
    Hash.ToLowerInvariant()

# Known-good existing current local proxy UUID from prior read-only pilot.
$proxyJob = "9d20dafa-282e-4240-914c-4cc5328185a4"

# Confirm the operator-approved Ollama model digest independently.
# For local Ollama or an authorized Azure bridge, the inventory is loopback.
$models = Invoke-RestMethod "http://127.0.0.1:11434/api/tags"
$model = $models.models | Where-Object {
    $_.name -eq "qwen2.5vl:3b" -or $_.model -eq "qwen2.5vl:3b"
} | Select-Object -First 1
if (-not $model) { throw "Approved Ollama model is unavailable." }
$ollamaSha = ([string]$model.digest -replace '^sha256:', '').ToLowerInvariant()
if ($ollamaSha -ne
    "fb90415cde1ef08aa669ae74b082d49b158729b6db1ab183c941417d507e71a1") {
    throw "Ollama digest changed; review model before disclosure."
}

$frameArgs = @(
  "run", "--project", "src/PhotoIdentity.Cli", "-c", "Release", "--",
  "video", "frames", "selected-one-shot",
  "--postgres-connection-env", "PHOTOIDENTITY_WI0190_CONN",
  "--database", "photoidentity_main",
  "--proxy-job-id", $proxyJob,
  "--cache-root", $cacheRoot,
  "--scratch-root", $frameScratch,
  "--ffmpeg", $ffmpeg,
  "--ffmpeg-sha256", $ffmpegSha,
  "--clip-model", $clip.ModelPath,
  "--clip-model-sha256", $clipSha,
  "--clip-vocab", $clip.TokenizerVocabularyPath,
  "--clip-merges", $clip.TokenizerMergesPath,
  "--ollama-model", "qwen2.5vl:3b",
  "--ollama-sha256", $ollamaSha,
  "--max-frames", "2"
)

# Read-only preflight — MUST NOT create a job or send images.
$preflight = dotnet @frameArgs
$preflightExit = $LASTEXITCODE
$preflight
"Exit code: $preflightExit"
if ($preflightExit -ne 0) { throw "Frame one-shot preflight denied." }
```

Expected JSON: `dryRun:true`, `admitted:true`,
`published:false`, `reasonCode:"preflight-only"`,
`originalsOpened:false`, `oneDriveHydrationRequested:false`.
No `POST /api/chat` should occur. Review video control and photo
activity if preflight says `operator-or-photo-busy`.
Preflight verifies the current proxy receipt but does not open the media.

Only after expressly approving disclosure of **two sampled JPEGs** to
the loopback model (possibly via your controlled Azure bridge) AND
confirming the independent isolated v38 rehearsal, run:

```powershell
$run = dotnet @frameArgs --apply `
    --acknowledge-isolated-v38-rehearsal `
    --acknowledge-frame-disclosure
$exitCode = $LASTEXITCODE
$run
"Exit code: $exitCode"
Get-ChildItem -LiteralPath $frameScratch -Force
```

Expected: `published:true`, `reasonCode:"published"`, exit code 0,
**only the original marker** in the scratch root, and exactly
two successful bridge `POST /api/chat` calls. `captionsReturned:false`
is intentional: the captions are private PostgreSQL evidence, not
printed. The command always disarms the selected scope in a bounded
`finally` block; on a crash, the PostgreSQL grant expires by itself.

### Verify persisted evidence without exposing private captions

With PostgreSQL tools already configured for the maintained catalogue,
the following read-only SQL displays aggregate numeric results only.
It does NOT enumerate captions or embeddings:

```sql
SELECT job.derivative_kind, job.status,
       batch.frame_count,
       batch.embedding_dimensions,
       COUNT(frame.ordinal) AS durable_frames
FROM video_frame_evidence_batches batch
JOIN video_processing_jobs job ON job.id=batch.job_id
LEFT JOIN video_frame_evidence_frames frame ON frame.job_id=batch.job_id
GROUP BY batch.job_id,job.derivative_kind,job.status,
         batch.frame_count,batch.embedding_dimensions
ORDER BY MAX(batch.published_at_utc) DESC
LIMIT 5;
```

Expect one new `FrameEvidence` / `succeeded` batch with
`frame_count=2`, `durable_frames=2`, and a positive,
consistent `embedding_dimensions`. Do not print frame captions,
vector bytes, source paths, connection strings or model prompts.

Re-running `--apply` with the same exact revision/model/tool identity is
**idempotent**: it cannot republish or silently create a second batch.
If an attempt fails, the job remains under normal bounded
failure/recovery semantics. Inspect with the existing sanitized
`video jobs` commands and explicitly retry only after reviewing the
failure. Do not manually alter database job state.

### Failure, stop and cleanup

Stop the bridge with Ctrl+C. The operator can use the reviewed
`video jobs stop --apply` command to revoke an active video job.
During processing, photo priority, pause, cancellation, source
exclusion and grant expiration must prevent further publication.
If an abnormal stop leaves a scratch child, DO NOT recursively delete
unknown content; inspect the marker/owned-workspace type and use
the conservative age-gated video scratch cleanup workflow.

A first-pass local verification may demonstrate success and normal
cleanup. Separate maintained-host tests must still exercise photo
preemption, pause/cancel, excluded/revised proxy, unsupported/oversized
clips and actual resource measurements. They should use controlled
candidate media, not random originals, and preserve archive features.

No new schema migration, hosted worker, background retry loop or
automatic Azure fallback was added in this checkpoint.

## CI verification

```powershell
dotnet test tests/PhotoIdentity.Persistence.Tests/PhotoIdentity.Persistence.Tests.csproj -c Release --filter "FullyQualifiedName~VideoScopedOneShotControlTests"
dotnet test tests/PhotoIdentity.Integration.Tests/PhotoIdentity.Integration.Tests.csproj -c Release --filter "FullyQualifiedName~VideoFrameSelectedOneShotTests|FullyQualifiedName~VideoFrameEvidenceWorkerAdapterTests"
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
```

PostgreSQL integration tests only exercise the **disposable CI database**.
No CI test sends private images or uses real Ollama/Azure credentials.
