---
id: WI-0031
title: Rewrite operator and architecture documentation
milestone: M15
status_source: ../status/work-items.yaml
depends_on: [WI-0025, WI-0030]
affected_modules: [README.md, docs/architecture, docs/operations, docs/models, docs/delivery]
---

# WI-0031: Rewrite operator and architecture documentation

## Objective

Turn the proven local workflows into a concise, connected documentation set that explains what the system does, how its data moves and how to run and test it.

## Acceptance criteria

- [x] The README provides one clear start-here path and points to detailed runbooks rather than duplicating them.
- [x] A local operator runbook covers setup, model installation, 500-image processing, review, suggestions, evaluation, queries, backup and cleanup.
- [ ] A multi-model runbook covers installing, processing, selecting revisions and comparing reports.
- [ ] Architecture documentation explains applications, modules, canonical data, derived artefacts, model revisions, review history and optional Azure scale-out.
- [ ] Command examples are PowerShell-first, copyable and include expected success signals.
- [ ] Troubleshooting covers missing crops, unavailable models, resumable jobs, database locking, trusted-network access and recovery from interrupted work.
- [ ] A glossary defines catalogue, revision, occurrence, exemplar, suggestion, assignment, model revision and bundle.
- [ ] Stale, contradictory and duplicated guidance is removed or redirected.

## Start-here and operator-guide slice

The first slice makes `README.md` a concise project orientation rather than a second runbook. It points directly to `docs/operations/local-operator-guide.md`, which is now the authoritative end-to-end local sequence.

The operator guide covers:

- Windows prerequisites, repository verification and pinned model installation;
- isolated source, output, catalogue, publish, evaluation and backup paths;
- explicit baseline model selection, batch status and resumable processing;
- publishing and using the Windows/Pixel browser application on a trusted private network;
- canonical human review and exact-model suggestion regeneration;
- deterministic reviewed-catalogue export and evaluation;
- collection browsing and the versioned neutral manifest;
- quiesced SQLite backup, restore references and cleanup; and
- common recovery paths for unavailable models, interrupted runs, stale browser assets, database locks and missing source or crop files.

`docs/index.md` now names the operator guide as the authoritative command sequence and treats evaluation, persistence, model and architecture documents as specialized references.

## Next slice

1. Rewrite the multi-model workflow around the accepted FP32-versus-INT8 process and retained FP32 recommendation.
2. Reconcile architecture applications, module boundaries, canonical data, derived artefacts and optional Azure scale-out.
3. Add the shared glossary and redirect or remove stale duplicated guidance.
4. Run documentation validation and generated-document checks before moving WI-0031 to review.
