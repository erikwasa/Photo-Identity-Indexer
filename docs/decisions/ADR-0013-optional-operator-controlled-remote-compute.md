---
id: ADR-0013
title: Permit optional operator-controlled remote inference without unvetted third-party processing
status: accepted
date: 2026-10-31
supersedes: [ADR-0010]
superseded_by: []
---

# ADR-0013: Permit optional operator-controlled remote inference without unvetted third-party processing

## Context

[ADR-0010](ADR-0010-local-production-execution.md) established local production processing and retired the original Azure VM/checkpointing roadmap. [ADR-0011](ADR-0011-operator-authorized-caption-burst-bridge.md) subsequently demonstrated an explicitly activated photo-caption backlog burst through a localhost bridge to a maintainer-deployed Ollama container on Azure. [ADR-0012](ADR-0012-isolated-video-media-architecture.md) originally assumed video processing would always be local. With bounded video-frame captions and embeddings in development, offloading expensive inference may be useful.

The maintainer permits transfer of potentially private media to **an application and model they control on Azure infrastructure**. The concern is **unvetted, opaque third-party application processing**, not Azure as a compute location. The original local-only wording conflated those different trust boundaries.

## Decision

### Local-first default; optional operator-controlled compute

- Local execution remains the standard workflow. Azure is **optional, plugged in deliberately when needed**, and never required for ordinary operation, CI, offline use or startup. No automatic cloud fallback, bulk upload, remote job scheduling or mandatory always-on service is authorized.
- For a **specific, explicitly operator-approved task**, Photo Identity may send bounded photo, video-frame, or other needed inputs to a **maintainer-deployed and configured runtime/model** on Azure infrastructure, such as the existing Azure Container App running Ollama. The application, container image, model and processing configuration must be known, reviewable and operator-controlled. Resources can be scaled to zero or deleted when not needed.
- The local Windows application remains the trusted control plane. The sole writable PostgreSQL catalogue, OneDrive authorization/hydration, canonical people and assignments, review history, exclusion rules, job control, validation and durable result publication **stay local**. Remote compute is a disposable inference executor, not a second source of truth.
- This policy does not reinstate the retired M09–M11 Azure VM pilot/checkpointing/cloud-dependent model-selection plan. ADR-0011 remains the accepted concrete photo-caption bridge. Any *new* remote worker or video-frame route requires separately scoped implementation and acceptance; this ADR does not claim it already exists.

### Processing application trust: what is and is not allowed

**Permitted:** Using Azure for infrastructure and hosting an operator-managed container running reviewed processing code and a pinned model. The operator selects the endpoint, inference application, model weights, access configuration, logs and temporary-data lifecycle. Dependencies and model licenses still require vetting.

**Not permitted by default:** Sending personal photos, frames, videos, embeddings, identities, metadata or location information to an unvetted hosted AI service, opaque Microsoft/Azure vision application, Azure AI Vision/Cognitive Services inference API, external model-as-a-service, or another third-party application's black-box processing endpoint. A service does not become approved merely because it is marketed as Azure, is in the same subscription, or supports a private network connection. Separate affirmative evaluation/authorization would be required for its processing behavior, storage, retention, telemetry, training/reuse, subprocessors and contractual guarantees.

Azure remains an external infrastructure provider with its own access, regional, storage, network and platform telemetry risks. Running our own container **does not** imply air-gap equivalence, zero platform access or automatic privacy compliance. Configure and review the chosen infrastructure accordingly.

### Explicit transfer, privacy and operational controls

- Every offload must be opt-in, purpose-scoped, size/time/concurrency/cost bounded, cancellable, observable and fail visibly; no silent fallback to another remote endpoint.
- Transfer only **necessary** inputs, with explicit disclosure: a temporary photo-caption thumbnail and neutral prompt, or selected bounded video frames and necessary per-frame parameters. Do not implicitly upload entire videos, archives, filenames, personal paths, GPS, face identities or unrelated catalogue records. When an algorithm truly requires original bytes or additional sensitive information, explicitly authorize and document that task's data minimization and retention policy rather than imposing an arbitrary categorical ban.
- Never export OneDrive tokens/credentials, PostgreSQL access, canonical update authority or unrestricted source-root access. Check source-copy exclusion, source revision and hydration policy before creating remote inputs; recheck revision, exclusion, job lease and authorization before local result publication. Newly excluded/stale results must not be published.
- Authenticate and encrypt transport (HTTPS/TLS), restrict ingress/egress, protect short-lived secrets outside logs/repository, keep any remote scratch private and temporary, minimize logs/telemetry, enforce retention/deletion, expose cost/time ceilings and provide operator ON/OFF and scale-to-zero steps. Fail closed when these safeguards cannot be met.
- Never commit sensitive input/output media, captions, embeddings, identities, credentials or private run evidence. Capture non-sensitive operational summaries and full model/policy provenance without leaking source paths or private text. A localhost bridge that forwards remotely is an **external disclosure** even though the app only calls loopback.

### Task-specific photo and video model settings

ADR-0011's proven **still-photo** caption path renders a **480x320 JPEG**, uses a neutral caption prompt and bounded Ollama options; **1024 context tokens** is a documented photo-burst configuration, **not a mandated video profile**.

Video frames have different visual-detail, aspect/rotation, timeline and scene-coverage demands. WI-0190's current local registered-proxy pilot uses a constrained SDR JPEG extraction profile with a **640 px maximum long edge**, selected timestamps, the still-photo CLIP preprocessor and Ollama vision model. This is an experimental, **local** path, not proof that the still-photo settings are optimal or that a video Azure path has been implemented.

For each processing role, explicitly version and evaluate: frame-sampling policy (interval/scene, timestamps, per-scene and total caps), source/proxy and extraction profile, pixel size/resolution, orientation/color/HDR behavior, encoding quality, prompts/language, requested context/output limits, batch/concurrency, model ID/exact digest, preprocessing, timeout and cost ceilings. Reuse photo settings **only if tests establish compatibility and quality**. Video-frame prompts and inference budgets may be changed when representative private-video benchmarks justify it; there is no universal prescribed frame size or token setting in this ADR.

Bind output to the immutable source revision and behavior-affecting model, prompt, sampling, preprocessing and inference profile versions. Record local versus Azure transport for audit, but do not invalidate otherwise equivalent results merely because transport changed; material model/preprocessing/setting changes must produce distinct provenance. Result import, publication and canonical effects remain governed locally.

### Implementation and verification boundary

The pre-existing ADR-0011 temporary photo-caption bridge remains permitted with its manual activation, exact model digest, HTTPS, guard and shutdown procedures. For **new** offloaded tasks (including video-frame captions/embeddings), first document the approved payload contract, endpoint/runtime supply chain, authentication, exclusion/revision/hydration fences, local validation, retention/purge, concurrency/cost limits, failure/retry behavior, versioned model and input profiles, quality/performance parity evidence and ON/OFF runbook. Track the implementation and evidence in repository-native work items. No remote video-frame worker, full-original transfer, remote transcoding or cloud access to the catalogue is approved merely by this ADR.

## Consequences

- Azure is a **permitted optional accelerator**, not the standard workflow or a forbidden processing location.
- Photos/video frames may be sent to a **known, operator-managed** Azure application when the operator explicitly enables a controlled task; **unvetted black-box third-party inference remains prohibited**.
- The standard Photo Identity architecture remains local-first, with local authority and verified derivative provenance.
- Photo and video sampling, preprocessing and model inference settings may differ and evolve with explicit versioning and quality evidence.
- ADR-0010 is superseded. ADR-0011 and ADR-0012 remain accepted for their respective caption bridge and video architecture; their old broad local-only assumptions must be read under this later policy.
