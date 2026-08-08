---
id: WI-0023
title: Process the full archive
milestone: M12
status_source: ../status/work-items.yaml
depends_on: [WI-0022, WI-0041]
affected_modules: [PhotoIdentity.Cli, PhotoIdentity.Worker, infra/azure]
---

# WI-0023: Process the full archive

## Objective

Complete coverage of the eligible archive after the permanent incremental-ingestion workflow is proven and the later production-model decision is available. Use bounded, resumable processing with progress, failure and completeness reporting rather than replacing the live catalogue with a separate one-shot batch result.

## Acceptance criteria

- [ ] Every eligible asset has an explicit terminal or pending state.
- [ ] Runs respect applicable item, runtime and spend caps.
- [ ] Failures can be retried without duplicate results.
- [ ] Existing reviewed identities and catalogue history remain part of the permanent catalogue while coverage expands.
- [ ] No unexplained missing assets remain.
