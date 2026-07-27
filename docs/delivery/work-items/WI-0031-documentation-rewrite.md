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

- [ ] The README provides one clear start-here path and points to detailed runbooks rather than duplicating them.
- [ ] A local operator runbook covers setup, model installation, 500-image processing, review, suggestions, evaluation, queries, backup and cleanup.
- [ ] A multi-model runbook covers installing, processing, selecting revisions and comparing reports.
- [ ] Architecture documentation explains applications, modules, canonical data, derived artefacts, model revisions, review history and optional Azure scale-out.
- [ ] Command examples are PowerShell-first, copyable and include expected success signals.
- [ ] Troubleshooting covers missing crops, unavailable models, resumable jobs, database locking, trusted-network access and recovery from interrupted work.
- [ ] A glossary defines catalogue, revision, occurrence, exemplar, suggestion, assignment, model revision and bundle.
- [ ] Stale, contradictory and duplicated guidance is removed or redirected.
