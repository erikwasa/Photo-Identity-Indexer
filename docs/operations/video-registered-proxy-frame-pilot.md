# WI-0190 checkpoint 3 — read-only registered-proxy frame analysis

This is an **explicit operator-only, experimental, read-only** development
pilot. It uses an already-generated WI-0189 playable proxy, NOT a personal
original. There is no background worker, Archive registration, OneDrive
hydration, PostgreSQL migration, job claim or persisted/searchable evidence.

## End-to-end boundaries

1. `video frames registered-proxy-pilot` requires one opaque `job_id`
   selected from already-succeeded `PlayableProxy` publications. There is
   **no** source path, original path or online-only override option.
2. Validate the existing private cache ownership marker and source-root
   non-overlap. A source-locking PostgreSQL read rechecks source, latest video
   revision, supported inspection, SDR BT.709, exclusions, finished job,
   registered canonical derivative path and file byte budget.
3. Copy only the local registered MP4 (max 256 MiB) into a unique, owned
   marked-temp workspace, while the source lock is held; verify exact
   registered SHA-256 and size before decoding. Never open a OneDrive item.
   Recheck currentness before each frame and after model inference.
4. Cap work to **1–4 frames** and at most 10 minutes for the entire command,
   with independently bounded, hash-pinned FFmpeg processes under Windows
   Job Objects (**60-second limit per extraction**). A separately bounded
   **180-second limit per frame** governs CLIP/caption inference *after* the
   supervised process has exited and the JPEG is validated; Azure cold-start
   time cannot consume the FFmpeg deadline. The Ollama HTTP timeout is 200
   seconds, with the shorter inference and overall limits taking precedence.
   Source is never passed to FFmpeg; the input is the disposable
   *proxy snapshot*. Auto-rotation and JPEG geometry/sizes use the fixed
   `wi0190-sdr-frame-jpeg-v1` extraction contract.
5. Feed each BGR24 image into the **same ONNX CLIP image encoder and image
   preprocessing as still-photo evaluations**, and the JPEG into the
   **existing loopback-only Ollama vision caption client**. Provenance
   includes CLIP ONNX model digest, CLIP preprocessing version, caption
   model's installed digest and caption prompt version.
6. Reject missing frames, stale/excluded revisions, partial results,
   malformed captions or vectors, mismatched positions and model identities.
   No batch is saved to PostgreSQL. A user-supplied optional `--query`
   also runs the existing CLIP text encoder and reports *numbers only* for
   max, top-3 mean and deduplicated-pooled cosine. This is **diagnostic,
   uncalibrated, and NOT video search ranking**.
7. Return **aggregate-only JSON**: frame count, JPEG bytes, decode/model
   timings, optional cosine metrics, and fixed failure stages. Do not
   print the source path, revision/job IDs, frames, captions, embeddings,
   hashes, private query text, native stderr or OneDrive metadata.

### Local prerequisite / Windows pilot

This is the **first WI-0190 checkpoint that can exercise actual local
photo models on an already-created video proxy**. Use it only after PR CI
passes and the PR is merged, with **one explicitly selected registered
already-local proxy**. Do not use an online-only source, and do not enable
video Archive processing just for this test.

Use the local PostgreSQL connection that already worked during WI-0189.
Select the opaque `job_id` from existing private catalogue records (for
example using your local psql session):

```sql
SELECT job.id AS job_id, round((proxy.encoded_bytes/1048576.0)::numeric, 2) AS proxy_mib
FROM video_published_derivatives proxy
JOIN video_processing_jobs job ON job.id = proxy.job_id
WHERE job.status='succeeded' AND job.derivative_kind='PlayableProxy'
ORDER BY proxy.created_at_utc DESC LIMIT 5;
```

The actual pilot will reject any selected stale, excluded, unsupported,
wrong-profile, missing, unregistered or corrupt proxy. Use the *same*
private registered cache root as WI-0189. No cache path, GUID, model hash
or source identifier should be committed to git or copied into chat.

On Windows PowerShell, from the repository root:

```powershell
$jobId = "<selected-existing-successful-playable-proxy-job-id>"
$cacheRoot = "<existing-WI0189-private-video-cache-root>"
$clipModel = "<path-to-existing-CLIP-ONNX-model>"
$clipVocab = "<path-to-existing-CLIP-vocabulary-json>"
$clipMerges = "<path-to-existing-CLIP-merges-txt>"
$ffmpeg = (Get-Command ffmpeg.exe).Source
$ffmpegSha = (Get-FileHash -LiteralPath $ffmpeg -Algorithm SHA256).Hash.ToLowerInvariant()
$root = Join-Path $env:TEMP ("photoidentity-wi0190-registered-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $root | Out-Null
Set-Content -LiteralPath (Join-Path $root ".photoidentity-synthetic-video-lab") -Value "wi0188-synthetic-video-lab-v1" -NoNewline -Encoding ascii

$baseArgs = @(
  "run", "--project", "src/PhotoIdentity.Cli", "-c", "Release", "--",
  "video", "frames", "registered-proxy-pilot",
  "--postgres-connection-env", "PhotoIdentity__Postgres__ConnectionString",
  "--database", "photoidentity_catalogue",
  "--job-id", $jobId, "--cache-root", $cacheRoot,
  "--scratch-root", $root, "--ffmpeg", $ffmpeg,
  "--ffmpeg-sha256", $ffmpegSha,
  "--clip-model", $clipModel, "--clip-vocab", $clipVocab,
  "--clip-merges", $clipMerges, "--max-frames", "2"
)
# The preview DOES NOT open PostgreSQL/media/models.
dotnet @baseArgs
# After explicit authorization, this copies only a verified *proxy*,
# runs local Ollama and ONNX, and leaves NO evidence in the catalogue.
dotnet @baseArgs --apply
# Optional only if you wish to compare retrieval: append
# --query "a neutral query" before --apply.
Get-ChildItem -LiteralPath $root -Force
# Only the original ownership marker should remain.
```

Verify that the environment variable named above is set; adjust the
environment-variable name and database if your maintained configuration
differs. The installed local Ollama model defaults to `qwen2.5vl:3b`;
optional `--ollama-model` selects another installed model. Model weights
must be locally available (nothing is downloaded automatically). The
Ollama endpoint is fixed to loopback.

**Operator evidence to share:** `status`, `frames`, `jpegBytes`,
`decodingMs`, `modelMs`, optional `retrievalScores`, final temp
directory listing, and any fixed `errorCode`. Do not share private media,
prompts, paths, hashes, GUIDs, or generated captions. A successful pilot
says `validated-not-published`, NOT accepted searchable evidence.

### Azure bridge cold start and model timeout (2026-10-31)

Maintainer Windows verification: the synthetic frame pilot passed 4/4
landscape (640×360) and 4/4 portrait (360×640) frames with complete
cleanup. The registered-proxy pilot successfully decoded the first JPEG
(`jpegBytes=18065`, `decodingMs=333`) but reported
`local-caption-and-clip:timeout` at `frames=0`; the operator-controlled
Azure bridge subsequently logged `POST /api/chat -> 200 (121.9s)`.

**Root cause:** checkpoint 3 mistakenly invoked CLIP and Ollama inside the
FFmpeg supervisor's post-exit callback, causing the **60-second FFmpeg clock
to cancel model inference**. The corrected command retains the decoder
budget, performs inference separately under a **180-second per-frame**
clock (still within the 10-minute aggregate cap), and records `modelMs`
even when inference fails. The 180-second cap is a bound, not a guarantee
that a cold Azure model will finish. Recheck the registered current revision
immediately before forwarding extracted JPEG pixels as well as afterward.

After merging the correction, update `main`, re-create `$pilotArgs` as in
this runbook, and rerun the one-proxy test. Use an operator-controlled
loopback bridge if Azure is intentionally selected; the pilot itself has
no automatic Azure fallback. **The bridge forwards the selected frame JPEGs
to Azure,** even though the CLI reports `originalsOpened=false`. Do not
send originals or video proxies to Azure; the maintainer must explicitly
authorize frame disclosure under ADR-0013. Record both the bridge's
`POST /api/chat -> 200` evidence and the pilot aggregate JSON
(`status`, `frames`, `jpegBytes`, `decodingMs`, `modelMs`,
`errorCode`) and check that only the scratch ownership marker remains.
No failed or partial model result is published or persisted.

### Maintained-host Azure/CLIP result: warm-model PASS (2026-10-31)

The operator retried the read-only, two-frame registered proxy on the
**pre-PR #677 checkout after the Azure Ollama model had warmed up**:

| Aggregate field | Observed |
| --- | --- |
| `status` | `validated-not-published` |
| `frames` | 2 |
| `jpegBytes` | 36213 |
| `decodingMs` | 128 |
| `modelMs` | 2551 |
| `errorCode` | `null` |
| Process exit code | 0 |
| Scratch after execution | Only the original ownership marker |

The output flags were all `false` for original access, OneDrive
hydration, video jobs, schema change, evidence persistence and returned
caption content. Neither private media nor identifiers were retained in
the repository. This confirms that an already-registered local video
proxy, existing CLIP model and operator-managed Azure Ollama can
complete the read-only frame model pilot without publishing evidence.

**Scope of proof:** This successful invocation preceded pulling merged
PR #677, so it **does not establish** that the corrected separate 180s
inference deadline survives an Azure cold start. Windows CI for the merged
PR #677 passed. A post-merge `main` rerun is the direct regression check,
preferably after allowing Azure to become cold again if the goal is to
observe a slow first caption. Do not force a cold restart solely to meet
this nonproduction pilot. For any Azure-backed test, verify the bridge
received actual `POST /api/chat` responses, and scale Azure GPU back to
zero when finished.

### Safe default/no-database tests

```powershell
dotnet test tests/PhotoIdentity.Core.Tests/PhotoIdentity.Core.Tests.csproj -c Release --filter "FullyQualifiedName~VideoFrame"
dotnet test tests/PhotoIdentity.Integration.Tests/PhotoIdentity.Integration.Tests.csproj -c Release --filter "FullyQualifiedName~VideoRegisteredFramePilotTests|FullyQualifiedName~VideoFrameExtractionProfileTests|FullyQualifiedName~VideoSyntheticFramePilotTests"
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
```

## Explicitly outstanding WI-0190 work

All four WI-0190 acceptance boxes remain unchecked. The pilot supports
only *already-created local SDR proxies*; it is not a registered frame
worker, does not produce any current durable frame evidence, has no
scene-change detector, and cannot prove HLG/HDR/unsupported-original
coverage. Current-revision revalidation before/after extraction is
not the atomic database publication/invalidation WI-0190 requires.
Preprocessing compatibility is exercised by reusing existing encoder
implementations; model **quality and retrieval relevance still require
maintainer evaluation on representative private videos**. Timings here
exclude durable storage, and diagnostic cosine scores are not calibrated
for multi-video search (WI-0191).

The next integrated development slice should add a distinct
`FrameEvidence` job execution/publication path, a versioned PostgreSQL
frame-evidence schema, atomic source/exclusion/lease/control fences,
partial-result invisibility and safe invalidation, then bounded
representative-video acceptance. Any schema migration requires the
usual maintained-host PostgreSQL backup/isolated restore rehearsal.
