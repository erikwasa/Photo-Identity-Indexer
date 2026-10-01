---
id: ADR-0011
title: Allow operator-authorized remote caption bursts through a localhost bridge
status: accepted
date: 2026-10-01
supersedes: []
superseded_by: []
---

# ADR-0011: Allow operator-authorized remote caption bursts through a localhost bridge

## Context

[ADR-0010](ADR-0010-local-production-execution.md) keeps production model execution and archive processing on maintainer-controlled local hardware and requires a new architecture decision before any later remote-compute path is used.

The WI-0128 caption worker is deliberately narrower than the rest of Photo Identity processing. It renders a temporary 480x320 JPEG from the configured durable review proxy, sends that image with a neutral caption prompt, model identifier and bounded Ollama generation options, then normalizes, guards and persists the returned text locally. PostgreSQL, archive access, identities, face data, Places, dates and collection state are not required by the inference endpoint.

On 2026-10-01 a maintained-machine backlog catch-up demonstrated that the existing loopback-only production code can use temporary external GPU capacity without adding a Remote mode to Photo Identity. A localhost reverse proxy on the Windows host forwarded the existing Ollama requests to an operator-controlled Azure Container Apps HTTPS endpoint. The local and Azure Ollama endpoints exposed the same `qwen2.5vl:3b` model digest, and the normal production worker completed the caption backlog successfully.

## Decision

Photo Identity itself remains loopback-only and unchanged. The default and supported application configuration continues to point at a loopback Ollama-compatible URL.

For a deliberate temporary caption-backlog burst, the operator may run a local reverse proxy on the maintainer-controlled Windows host and point `PhotoIdentity:CaptionEnrichment:OllamaBaseUrl` at that loopback proxy. The bridge may forward only the existing caption-inference traffic to an operator-controlled HTTPS Ollama-compatible endpoint.

The maintainer-controlled Windows host remains the trusted application/control plane. PostgreSQL remains the sole writable production catalogue and is never exposed remotely. Source originals, review-proxy paths, identity assignments, face crops/embeddings, Places/GPS, capture dates, collection membership and unrelated catalogue metadata remain local.

The remote endpoint receives only what the current caption generator already places in its Ollama request:

- the locally rendered temporary 480x320 JPEG;
- the neutral caption prompt;
- the model identifier; and
- bounded Ollama generation/protocol options.

Visible private content in the thumbnail is therefore disclosed to the remote inference endpoint. This is an explicit operator-authorized privacy exception and must never be mistaken for local-only processing merely because Photo Identity itself connects to a loopback address.

The bridge is an operator tool, not an automatic product fallback. It must be started and stopped deliberately. The upstream endpoint must use HTTPS and should have independent access restrictions such as source-IP allow-listing. No credentials, tokens or signed query parameters are stored in Photo Identity configuration or repository documentation.

Transport location is not part of caption-policy identity. The existing generation version, language, prompt version, image mode, requested context, model name and exact model digest remain authoritative. If the remote and local endpoints expose the same model digest and the other policy inputs match, returning to normal local Ollama must not cause transport-only regeneration. A different digest remains different provenance and normal candidate selection applies.

This exception applies only to temporary generated-photo-caption inference. It does not authorize remote face detection, recognition, embeddings, metadata extraction, archive processing, PostgreSQL access or original-file processing.

## Consequences

- No Photo Identity runtime, schema or API change is required for the proven burst workflow.
- The existing loopback restriction remains a useful application boundary; the operator-owned bridge makes the external disclosure explicit and temporary.
- Operational documentation must contain ON, monitoring, OFF/scale-to-zero and later reactivation procedures.
- The historical narration-evaluator field `external-photo-uploads=false` is not semantically accurate for a bridged run: the evaluator sees a loopback endpoint while the bridge forwards the thumbnail externally. Bridged benchmark records must call this out explicitly.
- Exact model digest verification is required before a burst intended to continue the same caption policy.
- Remote endpoint failure remains visible as ordinary Ollama transport failure; there is no automatic fallback.
- Azure or another provider may supply the temporary Ollama-compatible GPU endpoint, but provider infrastructure is operator-managed and does not become application architecture.
