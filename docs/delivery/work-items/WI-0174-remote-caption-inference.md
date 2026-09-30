---
id: WI-0174
title: Support operator-authorized remote caption inference for backlog bursts
milestone: M35
status_source: ../status/work-items/active/WI-0174.yaml
depends_on: [WI-0128]
---

# WI-0174: Support operator-authorized remote caption inference for backlog bursts

## Objective and scope

Implement [issue #472](https://github.com/erikwasa/Photo-Identity-Indexer/issues/472) within Photo Identity only. [ADR-0011](../../decisions/ADR-0011-operator-authorized-remote-caption-inference.md) accepts the narrow exception to [ADR-0010](../../decisions/ADR-0010-local-production-execution.md). No Azure infrastructure, remote catalogue access or general remote processing is authorized.

## Contract

- Local remains default and loopback HTTP(S) only. Explicit Remote requires an explicit HTTPS URL; invalid modes, plaintext remote transport, URL secrets and conflicting legacy/current URL configuration fail fast.
- The worker sends only locally rendered 480x320 JPEG bytes, neutral caption prompt, model and Ollama options. No original bytes, paths, identities, crops, embeddings, Places/GPS, dates, collection membership, connection information or EXIF is serialized.
- Exact `/api/tags` digest, generation/prompt versions, candidate selection, language, normalization, guards, persistence, timing, cancellation and retry/backoff remain intact. No transport field is added to persisted caption identity.
- Status displays mode, host, configured model, resolved digest and worker message; Remote carries an explicit notice. Browser settings cannot select an endpoint.
- `narration evaluate --inference-mode Remote` supports HTTPS and requires thumbnails; aggregate reports record mode and host without URL secrets.
- [Operator procedure](../../operations/local-caption-narration-evaluation.md) covers endpoint access controls, digest verification, benchmark, temporary backlog operation and return to Local.

## Acceptance and evidence

Automated checks must establish configuration validation, exact payload keys and bounded JPEG/no metadata, status privacy, remote failure/cancellation without fallback and equal-digest candidate identity across transports. Relevant builds/tests, Docs validation and current generated views are required. Evidence is recorded here after implementation.

Maintained Windows acceptance remains required: benchmark a small representative sample, generate a small remote batch, confirm stored model/digest/policy/timing and guard behavior, then restart in Local with the same digest/policy and verify no transport-only regeneration. Record worker failure/backoff visibility and absence of sensitive logs. Do not claim this verification without an operator-controlled endpoint and catalogue run.
