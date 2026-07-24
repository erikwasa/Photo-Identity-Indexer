---
id: WI-0023
title: Process the full archive
milestone: M12
status_source: ../status/work-items.yaml
depends_on: [WI-0022]
affected_modules: [PhotoIdentity.Cli, PhotoIdentity.Worker, infra/azure]
---

# WI-0023: Process the full archive

## Objective

Partition the eligible archive into bounded, budget-controlled jobs and produce progress, failure and completeness reports.

## Acceptance criteria

- [ ] Every eligible asset has an explicit terminal or pending state.
- [ ] Runs respect item, runtime and spend caps.
- [ ] Failures can be retried without duplicate results.
- [ ] No unexplained missing assets remain.
