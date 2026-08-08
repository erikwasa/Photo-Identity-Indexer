---
id: WI-0023
title: Process the full archive
milestone: M12
status_source: ../status/work-items.yaml
depends_on: [WI-0022, WI-0041, WI-0042]
affected_modules: [PhotoIdentity.Cli, PhotoIdentity.Worker, infra/azure]
---

# WI-0023: Process the full archive

## Objective

Complete coverage of the eligible archive after the permanent incremental-ingestion workflow, bounded local-storage workflow and later production-model decision are available. Use bounded, resumable processing with progress, failure and completeness reporting rather than replacing the live catalogue with a separate one-shot batch result or requiring the full OneDrive archive to remain hydrated locally.

## Acceptance criteria

- [ ] Every eligible asset has an explicit terminal or pending state.
- [ ] Runs respect applicable item, runtime, local-storage and spend caps.
- [ ] Failures can be retried without duplicate results.
- [ ] Existing reviewed identities and catalogue history remain part of the permanent catalogue while coverage expands.
- [ ] Full-archive completion does not require simultaneous hydration of the complete source archive; WI-0042 proxy and bounded-hydration semantics remain in force.
- [ ] No unexplained missing assets remain.
