# WI-0190 checkpoint 8 — read-only FrameEvidence audit and cost baseline

This command audits **one opaque FrameEvidence job UUID** using PostgreSQL
metadata only. It does not read photos, original video bytes, cached proxy
files, frame captions, embedding vectors, filesystem paths, model identities
or prompts. No worker, FFmpeg, Ollama, Azure bridge or OneDrive hydration is
started. It uses a **repeatable-read, read-only transaction**, requires an
existing current schema and cannot migrate or repair data.

## Windows operator procedure

Use the latest merged `main`, and keep the PostgreSQL connection string in
the already approved environment variable, never in arguments, screenshots
or documentation. The maintained catalogue already passed its isolated
v38 migration rehearsal; do not repeat it for this read-only audit.

~~~powershell
git switch main
git pull --ff-only origin main

# Supply a FrameEvidence job UUID from your private catalogue / sanitized
# video job status. It is NOT the earlier PlayableProxy job UUID.
$frameJobId = "REPLACE_WITH_FRAME_EVIDENCE_JOB_UUID"

$auditArgs = @(
    "run", "--project", "src/PhotoIdentity.Cli", "-c", "Release", "--",
    "video", "frames", "audit",
    "--postgres-connection-env", "PHOTOIDENTITY_WI0190_CONN",
    "--database", "photoidentity_main",
    "--job-id", $frameJobId
)

$report = dotnet @auditArgs
$auditExit = $LASTEXITCODE
$report
"Exit code: $auditExit"
~~~

For an unknown job UUID, select one **locally** without printing or sending
captions or source identifiers:

~~~sql
SELECT id, status, requested_frames
FROM video_processing_jobs
WHERE derivative_kind='FrameEvidence'
ORDER BY updated_at_utc DESC
LIMIT 10;
~~~

The returned JSON has a fixed `wi0190-frame-evidence-audit-v1` protocol,
`readOnly:true`, `current:true` and `reasonCode:"current-complete"`
for a succeeded current, unexcluded, supported SDR batch with complete
ordered frames, valid vector lengths and timestamps. Exit **0** means this
**specific** currentness and completeness check passed, not that caption
quality or scene recall is good.

The report exposes only aggregate frame count, vector dimensions, caption
UTF-8 byte count, embedding byte count, combined **payload** bytes and
first/last frame milliseconds within the inspected video duration. Payload
bytes exclude PostgreSQL row/index overhead, WAL, replicas and retained video
proxy bytes; do not mistake them for physical database storage or cost.
The older selected one-shot now additionally returns `executionWallMs`
for its attempted worker cycle plus disarm (not model GPU utilization,
total end-to-end application time or actual Azure billed cost). This metric
is absent/null for dry-run or a rejected preflight.

A nonzero audit exit with `current:false` means the requested job is not
currently publishable as complete current evidence:

- `job-not-found`, `wrong-job-kind`, `batch-not-published` or
  `job-not-succeeded`: not a completed batch.
- `stale-revision`, `source-excluded` or `source-unavailable`: source
  revision or privacy/currentness invalidation. Excluded batches may already
  have been **physically purged** by PostgreSQL v38.
- `unsupported-media`: current inspection is not a supported SDR video.
- `incomplete-or-invalid`: stored count, ordinal order, timestamps,
  payload lengths or source/revision identities fail the audit contract.

The audit **does not make or revoke** the publication. It is not a substitute
for the publisher's transactional lease/source locks or the independent
`GetCurrentAsync` read gate. It also cannot detect a plausible but
semantically inaccurate generated caption, measure unseen scenes, detect
motion/audio or demonstrate rotated/HLG/low-light understanding. Do not
treat this as evidence of model or retrieval quality.

## Private acceptance recording

For each already-local representative test clip, keep a **private**
operator worksheet with: clip type (short, multi-scene, long, rotated,
low-light), sampled frame timestamps and visibly missed scenes after human
review, `executionWallMs`, frame payload bytes, proxy bytes, approximate
memory/CPU/GPU monitoring and approximate operator-managed Azure costs
when relevant. Record quality/retrieval judgments **privately**, then
transfer only coarse anonymized aggregate counts and timings to the
work item. The current automatic one-shot is restricted to at most two
frames; this is useful for continuity and a baseline but insufficient to
accept multi-scene coverage or ranking strategy.

## CI and local synthetic verification

~~~powershell
dotnet test tests/PhotoIdentity.Integration.Tests/PhotoIdentity.Integration.Tests.csproj -c Release --filter "FullyQualifiedName~VideoFrameEvidenceAuditTests"
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
~~~

Tests validate the strict parser, fail-closed audit classification and,
when `PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING` exists, create
and drop a **disposable isolated** PostgreSQL database. Synthetic rows
exercise a current two-frame batch, an observed newer video revision and
an actual source-copy exclusion that removes the private batch. The existing
selected-one-shot preflight test also checks unsupported, oversized and
excluded proxy refusal without temporary media or queue mutation. No personal
media, model server or FFmpeg is used. Missing test-admin connection means
the DB portion is skipped and **does not** certify a live PostgreSQL test.

## Status and remaining gates

Checkpoint 7's maintained-host durable two-frame publication remains
accepted. Checkpoint 8 adds an audit/testing instrument and one-shot wall
time only; the maintainer **successfully ran the read-only audit** on the
published two-frame batch on 2026-10-31. It returned
`current-complete`, 2/2 durable frames, 512 dimensions, 151 caption UTF-8
bytes and 4,096 vector bytes (4,247 combined payload bytes), with sampled
timestamps 3,075 and 9,225 milliseconds within 12,300 milliseconds.
No originals, OneDrive hydration, caption/vector return, queue write or schema
change was reported. This is currentness/storage verification only; it is
not CPU/GPU accounting, retrieval quality or representative clip coverage.
See the [separate scene coverage command](video-frame-scene-coverage.md). It does
not grant permission to enable automatic video work, resume OneDrive
online-only hydration or dispatch to Azure. Remaining WI-0190 acceptance:
disturbance/cancellation/preemption tests, explicit unsupported/oversized
rejection, representative short/multi-scene/long/rotated/low-light sampling,
frame retrieval relevance and measured CPU/GPU/throughput/storage cost.
No formal acceptance checkbox changes from this development-only step.
