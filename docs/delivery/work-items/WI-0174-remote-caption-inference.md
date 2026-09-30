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


## Implementation review

[PR #473](https://github.com/erikwasa/Photo-Identity-Indexer/pull/473) implements the explicit `PhotoIdentity:CaptionEnrichment:InferenceMode` and `--inference-mode` contracts. Both use the shared neutral endpoint validator. Remote requires an explicit HTTPS endpoint; Local remains loopback HTTP(S). URL credentials/query/fragment and redirects are excluded. The production thumbnail renderer, prompt/generation versions and repository candidate identity are unchanged. The status contract adds mode, host and last resolved model digest, while browser writes remain limited to enable/language.

Transport/JSON errors are sanitized before reaching CLI output, and worker retry logs retain only the failure type. The existing five-minute backoff is exercised through the same method used by the background loop. Local and Remote use the same generation evidence identity and no database migration is needed.

The workspace had no .NET SDK, and a workspace-local SDK could not initialize CoreCLR (`0x8007000E`), so repository commands were executed on the existing Windows CI runner. [Lifecycle/validation run](https://github.com/erikwasa/Photo-Identity-Indexer/actions/runs/36779726525) successfully ran `PhotoIdentity.Docs show WI-0174`, `review WI-0174`, `validate` and `generate --check`; generated delivery files and the canonical in-review shard were retrieved from that runner. The temporary artifact/lifecycle step is removed from the final change; the required CI gate is unchanged.

Maintained-machine acceptance remains pending. This item is in review, not completed; no live remote batch, backlog disclosure or production configuration change was performed by this implementation session.


Windows CI run 36779726525 passed the solution build, fast assemblies (including Core endpoint tests), documentation validation/generation checks, published review smoke, launcher and package verification, and both integration shards: 285 + 352 = 637 passing tests, zero failures/skips. The direct remote worker regression passed in 4.466 seconds; it establishes bounded JPEG/metadata stripping, caption persistence and exact digest, equal-digest Local idempotency, changed-digest regeneration and five-minute remote failure/backoff. Configuration, status privacy, remote evaluator parsing/report fields, transport/JSON sanitization and cancellation tests also passed. Automated requests use recording handlers, so no private photo was sent to a live external endpoint during CI.

The cleanup commit retains the generated delivery outputs, corrects the Settings privacy notice for Remote mode and removes the temporary CI bridge. Its ordinary required-gate run verifies the final tree; maintained Windows endpoint acceptance remains separate.
