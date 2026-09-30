---
id: ADR-0011
title: Allow operator-authorized remote caption inference
status: accepted
date: 2026-09-30
supersedes: []
superseded_by: []
---

# ADR-0011: Allow operator-authorized remote caption inference

## Context

[WI-0174](../delivery/work-items/WI-0174-remote-caption-inference.md) authorizes a narrow caption-inference exception to [ADR-0010](ADR-0010-local-production-execution.md) for temporary backlog bursts. Local generation is useful but slow. This decision does not revive ADR-0004, Azure provisioning, portable cloud workers or general remote archive processing.

## Decision

The maintainer-controlled Windows Photo Identity host remains the trusted application and control plane. PostgreSQL remains the sole writable production catalogue, local and never exposed to the inference endpoint. Originals, source access, review-proxy paths, identities, biometric data, Places/GPS, dates and collection state remain local.

Local is the default caption inference mode and accepts only absolute loopback HTTP(S) endpoints. Remote requires explicit operator/deployment configuration and an explicit absolute HTTPS endpoint. Browser clients cannot select the transport. There is no automatic fallback in either direction. Remote receives only a freshly rendered bounded 480x320 JPEG caption thumbnail, the neutral prompt, model identifier and Ollama generation/protocol options. The evaluator must also use thumbnails in Remote mode.

Endpoints cannot contain user information, query strings or fragments. Redirects are disabled. The operator must provide independent endpoint access controls, such as platform authentication or network allow-listing. This change does not introduce credential management; an endpoint requiring application-supplied authentication headers is outside this contract. Transport logging is disabled for the caption HTTP client; exceptions and worker logs must not include request/response bodies, URL secrets or private paths. Status and aggregate benchmarks expose the mode and endpoint host only.

Exact model digest continues to be resolved from `/api/tags`. Transport location is operational status, not generation-policy identity. With matching model name/digest, prompt version, language, image mode, context size and generation version, remote captions satisfy the same candidate policy as local captions. A different digest remains distinct evidence. Returning to Local requires configuration only, with no migration or caption rewrite.

This accepted decision modifies only ADR-0010's caption execution location restriction. All other model execution and archive processing retain ADR-0010's local boundary. No Azure infrastructure is in scope.

## Consequences

- Caption thumbnails depict private photos; remote opt-in explicitly authorizes that disclosure, even though catalogue metadata and originals are excluded.
- Endpoint trust, access controls, model availability and temporary capacity are operator responsibilities.
- Remote outages remain visible under the existing worker retry/backoff and cancellation behavior.
- The worker remains serial and preserves normalization, guards, provenance and generation timing.
- Completion requires maintained-machine evidence of a small remote batch and return to Local with matching digest and no transport-only regeneration.
