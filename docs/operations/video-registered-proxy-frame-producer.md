# WI-0190 checkpoint 6 — current playable-proxy frame producer

**Status:** Developed but unregistered. No additional PostgreSQL schema
changes, no public API and no automatic video processing. The maintainer's
previous PostgreSQL v37→v38 migration verification remains accepted and
need not be repeated for this code-only checkpoint.

## Bounded execution design

1. A separately authorized, already-claimed `FrameEvidence` video job
   supplies a live revision/lease, exact pinned FFmpeg digest, policy,
   profile and an explicit **1–4 frame** reservation. No source path may
   be passed to this producer.
2. `PostgresVideoFrameProxyPilotSource.FindCurrentProxyJobAsync` resolves
   only an already-published current local SDR playable-proxy receipt
   matching the FrameEvidence lease's **exact revision**. Copying the
   proxy takes the existing source lock, verifies registered SHA-256 and
   never opens originals or hydrates cloud-only source copies.
3. A separate, operator-created and marked scratch root under OS temp
   must initially contain **only**
   `.photoidentity-frame-evidence-root`, exactly containing
   `wi0190-registered-proxy-frames-v1`. Reject source/cache overlap,
   network locations, links and unexpected files. The producer creates
   and removes only its unique per-attempt child workspace.
4. Each frame runs under the existing Windows FFmpeg supervisor:
   a 60-second wall clock, one-process Job Object, 768 MiB memory limit,
   15% CPU limit, 8 MiB tool workspace and 640-pixel SDR JPEG contract.
   The separate CLIP+Ollama inference limit is 180 seconds per frame;
   the caller's bounded job lifetime and lease heartbeat remain in force.
5. Model evidence includes exact local CLIP SHA-256 and pinned preprocessing
   version, current Ollama model digest and a **new video-specific prompt**
   version: `wi0190-visible-video-frame-v1`. The prompt explicitly avoids
   inventing motion between sampled frames or auditory information.
   The photo-caption prompt is unchanged.
6. Operator-managed Ollama is reached through
   `http://127.0.0.1:11434` only. An optional bridge may forward selected
   JPEG pixels to an operator-controlled Azure container as separately
   authorized under ADR-0013, but this type contains no cloud endpoint
   configuration, no automatic remote fallback and no third-party service.
7. Before each decoded frame, before disclosing its JPEG to Ollama and
   before returning the batch, source registration and live job permission
   are rechecked. If the model digest changes, the entire batch is rejected.
   The producer never publishes itself; only the checkpoint 5 adapter and
   v38 PostgreSQL repository can atomically acknowledge job success.

## CI test surface

```powershell
dotnet test tests/PhotoIdentity.Integration.Tests/PhotoIdentity.Integration.Tests.csproj -c Release --filter "FullyQualifiedName~VideoRegisteredProxyFrameEvidenceProducerTests|FullyQualifiedName~OllamaVisionCaptionClientTests"
dotnet test tests/PhotoIdentity.Integration.Tests/PhotoIdentity.Integration.Tests.csproj -c Release --filter "FullyQualifiedName~VideoFrameEvidenceWorkerAdapterTests"
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
```

Integration tests use temporary scratch markers, a fake loopback HTTP
handler and disposable PostgreSQL databases. For real database assertions,
`PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING` must point to the
**disposable CI/local test instance, never the maintained photoidentity_main
catalogue**. When absent, tests that require PostgreSQL skip their DB work.
No personal files, FFmpeg model inference or Azure service are used in CI.

## Maintainer verification

**No new maintained-host test is required for this checkpoint.** This
producer is not registered, so there is no supported operator command to
run it on the maintained media library. Do NOT try to directly arm
`video one-shot --apply`; its independent isolation gate intentionally
rejects real media execution.

The next local milestone is a separately reviewed *opt-in one-job* command
with the approved proxy-only producer wired to the v38 publisher under a
live heartbeat and selected-job scope. That must verify (1) current proxy
and local scratch isolation, (2) 1–4 timestamped captions/vectors,
(3) two-way pause/cancel/photo-priority fencing, (4) unsupported and
oversized input refusal, (5) no originals/OneDrive hydration, (6) exact
model/provenance and current-only persisted reads, (7) physical exclusion
purge, (8) complete temporary-file cleanup, and (9) sampled-scene,
low-light, long and rotated video coverage. Benchmarks must include
throughput, storage/clip, CPU/GPU/Azure cost and retrieval relevance.
