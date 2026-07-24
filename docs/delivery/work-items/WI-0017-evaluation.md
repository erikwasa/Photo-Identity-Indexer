---
id: WI-0017
title: Add evaluation harness
milestone: M06
status_source: ../status/work-items.yaml
depends_on: [WI-0016]
affected_modules: [PhotoIdentity.Cli, tools/model-lab]
---

# WI-0017: Add evaluation harness

## Objective

Manage gallery, validation and test datasets and report detector recall, identification precision, unknown rejection, confusion, throughput and threshold sweeps.

## Acceptance criteria

- [ ] Repeated runs with fixed inputs are reproducible.
- [ ] Test data is not used to choose thresholds.
- [ ] Reports identify model hashes and pipeline versions.
- [ ] Archive runtime and cost can be projected from measured throughput.
