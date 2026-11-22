---
id: WI-0031
title: Rewrite operator and architecture documentation
milestone: M15
status_source: ../status/work-items.yaml
depends_on: [WI-0025, WI-0030]
affected_modules: [README.md, docs/architecture, docs/operations, docs/models, docs/delivery, PowerShell scripts]
---

# WI-0031: Rewrite operator and architecture documentation

## Objective

Turn the proven local workflows into a concise, connected documentation set that explains what the system does, how its data moves and how to run and test it.

## Acceptance criteria

- [x] The README provides one clear start-here path and points to detailed runbooks rather than duplicating them.
- [x] A local operator runbook covers setup, model installation, 500-image processing, review, suggestions, evaluation, queries, backup and cleanup.
- [x] A multi-model runbook covers installing, processing, selecting revisions and comparing reports.
- [x] Architecture documentation explains applications, modules, canonical data, derived artefacts, model revisions, review history and optional Azure scale-out.
- [x] Command examples are PowerShell-first, copyable and include expected success signals.
- [x] Troubleshooting covers missing crops, unavailable models, resumable jobs, database locking, trusted-network access and recovery from interrupted work.
- [x] A glossary defines catalogue, revision, occurrence, exemplar, suggestion, assignment, model revision and bundle.
- [x] Stale, contradictory and duplicated guidance is removed or redirected.

## Start-here and operator guide

`README.md` is concise project orientation rather than a competing runbook. It points to `docs/operations/local-operator-guide.md` as the authoritative end-to-end Windows sequence.

The operator guide covers:

- prerequisites, repository verification and pinned model installation;
- isolated source, output, catalogue, publish, evaluation and backup paths;
- baseline processing, status and resume;
- Windows and trusted-network Pixel browser use;
- human review, people maintenance and exact-model suggestion regeneration;
- deterministic evaluation and collection queries;
- the neutral collection manifest;
- stopped-state backup and cleanup; and
- recovery for unavailable models, interrupted work, stale web assets, locks and missing files.

## Evaluation and multi-model guidance

`docs/operations/local-evaluation.md` now focuses on one exact detector/embedder revision from an accepted reviewed catalogue.

`docs/operations/multi-model-comparison.md` is the authoritative comparison path around `Invoke-MultiModelComparison.ps1`. It documents:

- fixed source, detector, alignment, review and evaluation scope;
- pinned FP32 and INT8 installation;
- resumable same-catalogue processing;
- exact-model suggestion regeneration;
- deterministic split/report assertions;
- machine-generated and human acceptance gates; and
- the accepted recommendation to retain `sface-2021dec-fp32` as the current default.

Baseline, candidate and model-governance pages use the same exact-revision and recommendation language.

## Architecture reconciliation

The architecture set now describes the implemented system rather than roadmap-era plans:

- runtime application responsibilities;
- project and adapter dependency boundaries;
- canonical versus regenerable data;
- immutable source and face identities;
- exact model provenance and coexisting embeddings;
- append-only human review history;
- advisory identity suggestions;
- collection and neutral-manifest privacy boundaries; and
- optional bundle-only Azure processing without canonical identity access.

## Shared vocabulary and stale-guidance cleanup

`docs/glossary.md` defines the common catalogue, revision, occurrence, exemplar, suggestion, assignment, model-revision and bundle terms plus related operational vocabulary.

The documentation index separates the authoritative operator sequence from specialized evaluation, persistence, model and architecture references. Roadmap-era statements about planned modules, initial screens and future resumability were removed or replaced with current behavior.

## Final script and verification housekeeping

After PR #65 merged and both build #406 and multi-model workflow #6 passed, the PowerShell entry points received one final stale-text and behavior audit:

- the completed WI-0033 manual-verification directory and its one-off session reporter were removed;
- `verify-review.ps1` now reports only the disposable fixture, published-application smoke checks and optional interactive URLs that it actually provides;
- `Invoke-MultiModelComparison.ps1` now produces a general `multi-model-comparison` report and checklist rather than WI-0030-labelled evidence;
- `build.ps1`, `test.ps1` and `models/install-models.ps1` default to Release and explicitly fail on non-zero native exit codes; and
- `verify-local.ps1` and the published smoke script were reviewed and retained because their descriptions and output remain current and work-item-neutral.

The retired WI-0033 work-item page records why the temporary acceptance artifacts no longer remain in the repository.

## Completion evidence

WI-0031 completed on 2026-08-02 through three reviewed and merged slices:

- PR #64 established the current README, authoritative local operator guide and documentation routing;
- PR #65 completed the multi-model, architecture, model-governance, persistence and glossary rewrite; and
- PR #66 removed retired verification artifacts and made permanent PowerShell entry points work-item-neutral and Release-first.

The final automated gates passed in build #408 and multi-model workflow #7, covering Release build, full tests, documentation validation, generated-document consistency, published review smoke, Windows PowerShell media verification and PowerShell 5.1/7 comparison-script self-tests.

Independent clean-environment and trusted-network device validation remains explicitly scoped to WI-0032; it is not claimed by this completion record.