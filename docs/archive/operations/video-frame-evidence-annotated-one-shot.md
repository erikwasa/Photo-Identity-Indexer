# WI-0190 checkpoint 10 — opt-in annotated-scene durable FrameEvidence

The selected FrameEvidence one-shot now supports **real scene-balanced
caption and CLIP evidence** on one operator-selected, already-local,
registered current SDR proxy. Scene boundaries are **human-annotated**,
not detected. Interval sampling remains the default.

## Sampling identity, provenance and gates

Supply an optional --scene-boundaries-ms parameter with 1–6 canonical
positive, strictly increasing integer millisecond cut positions strictly
inside the inspected clip duration, e.g. 3000,6000,9000.

To make the result fully regenerable and protect the original interval
job from idempotency collisions, the private FrameEvidence job policy
encodes the exact annotated offsets and frame count, for example
video-frame-sampling-scene-v1-n2-3000_6000_9000. The producer
reconstructs that exact plan from the leased job policy; another set
of annotations creates a **different job**. The underlying video job identity permits at most
80 characters (six 7-digit cuts fit exactly). Selected frames store exact timestamps and
scene_index plus model, sampling, tool and revision provenance.

No new migration, worker, original or OneDrive hydration path is
introduced. The original checkpoint allowed **1–2 frames**; checkpoint 15
now supports **1–4 explicitly selected frames** (same grant/attempt caps),
15-minute grant, 13-minute attempt, supervised FFmpeg and separate
model timeouts, SHA-verified proxy and tools, and operator control,
photo-priority, cancellation, revision and exclusion fences.
The policy and annotation offsets are private PostgreSQL metadata,
not CLI output or source material to commit in a PR.

## Windows operator procedure after CI and merge

Start with the already documented [selected one-shot prerequisites](video-frame-evidence-selected-one-shot.md)
and its $frameArgs array (which contains the existing --max-frames 2).
Reuse the current published PlayableProxy UUID, pinned physical
FFmpeg executable, marked scratch root, approved local CLIP files,
and authorized loopback Ollama or operator-controlled Azure bridge.
Never substitute an online-only video.

**Illustrative** scene boundaries for a 12.3-second clip follow;
replace them with offsets privately determined from the actual video.

~~~powershell
$sceneCutsMs = "3000,6000,9000"

# Read-only preflight: no database write, FFmpeg, Ollama or image disclosure.
$preflight = dotnet @frameArgs --scene-boundaries-ms $sceneCutsMs
$preflightExit = $LASTEXITCODE
$preflight
"Exit code: $preflightExit"
if ($preflightExit -ne 0) { throw "Scene preflight refused." }
~~~

Expect samplingMode **annotated-scene**, reasonCode **preflight-only**,
dryRun **true**, admitted **true**. Unsupported or impossible scene
plans fail before any job or media work with **invalid-sampling-plan**.

Actual production is **optional and separately authorized**; this
may send two private JPEGs to the vetted loopback/controlled Azure
model and incur cost.

~~~powershell
$run = dotnet @frameArgs --scene-boundaries-ms $sceneCutsMs --apply --acknowledge-isolated-v38-rehearsal --acknowledge-frame-disclosure
$runExit = $LASTEXITCODE
$run
"Exit code: $runExit"
Get-ChildItem -LiteralPath $frameScratch -Force
~~~

Expect published **true**, reasonCode **published**, exit 0,
and only the owned scratch marker remaining. Repeating an identical
already-succeeded job is refused rather than silently rerun.

Find the new **FrameEvidence job UUID**, not the proxy UUID, via
numeric-only PostgreSQL:

~~~sql
SELECT job.id AS frame_evidence_job_uuid,
       job.status, batch.frame_count, batch.embedding_dimensions,
       count(frame.ordinal) AS durable_frames,
       count(DISTINCT frame.scene_index) AS sampled_scenes
FROM video_frame_evidence_batches batch
JOIN video_processing_jobs job ON job.id=batch.job_id
LEFT JOIN video_frame_evidence_frames frame ON frame.job_id=batch.job_id
WHERE job.derivative_kind='FrameEvidence'
GROUP BY job.id, job.status, batch.frame_count,
         batch.embedding_dimensions, batch.published_at_utc
ORDER BY batch.published_at_utc DESC
LIMIT 5;
~~~

Then run the existing read-only video frames audit and
video frames coverage for the new UUID, using the same private
manual cuts. No output contains captions, vectors or source paths.

## Automated verification

~~~powershell
dotnet test tests/PhotoIdentity.Core.Tests/PhotoIdentity.Core.Tests.csproj -c Release --filter "FullyQualifiedName~VideoFrameSceneSamplingIdentityTests"
dotnet test tests/PhotoIdentity.Integration.Tests/PhotoIdentity.Integration.Tests.csproj -c Release --filter "FullyQualifiedName~VideoFrameSelectedOneShotTests|FullyQualifiedName~VideoFrameEvidenceWorkerAdapterTests"
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
~~~

Pure tests prove policy round-trip, canonical annotation validation,
identity separation and bounded scene spacing. Disposable
PostgreSQL tests prove independent annotated-job idempotency and
complete durable scene-indexed evidence. Preflight tests prove
zero writes for valid/invalid cuts. Database fixture tests require
PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING; otherwise they
skip and do not count as database coverage.

This does not implement automatic scene detection, more than four
frames per real one-shot, private multi-scene/rotated/
low-light quality validation, GPU/CPU/Azure cost measurement,
retrieval relevance or real-process interruption testing. These
remain in the consolidated WI-0190 acceptance plan. No additional
maintainer media run is required merely to merge this checkpoint.
