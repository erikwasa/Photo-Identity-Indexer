---
id: WI-0030
title: Run a multi-model local comparison
milestone: M08
status_source: ../status/work-items.yaml
depends_on: [WI-0019, WI-0029]
affected_modules: [PhotoIdentity.Cli, PhotoIdentity.Api, PhotoIdentity.Web, PhotoIdentity.Persistence.Sqlite, tools/model-lab]
---

# WI-0030: Run a multi-model local comparison

## Objective

Repeat the accepted local workflow with baseline and candidate model revisions on the same 500-image corpus and compare their practical and measured behaviour.

## Acceptance criteria

- [ ] Both models process the same immutable source revisions and retain separate provenance.
- [ ] People, labels and review history are shared canonical data rather than copied per model.
- [ ] The web interface can select or clearly distinguish model revisions and their suggestions.
- [ ] Suggestions from different models cannot overwrite or be mistaken for each other.
- [ ] The same gallery, validation and held-out test split is evaluated for each compatible embedder.
- [ ] Detector counts, identification metrics, unknown rejection, confusion, throughput, storage and operator review effort are compared.
- [ ] Representative disagreements are reviewed manually without using test results to tune thresholds.
- [ ] The outcome records a recommendation, remaining uncertainty and whether a larger evaluation set is required.
