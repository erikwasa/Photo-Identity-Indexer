---
id: M06
title: Local evaluation and acceptance
status_source: ../status/milestones.yaml
depends_on: [M04, M05]
---

# M06: Local evaluation and acceptance

## Outcome

The complete baseline system is usable locally on a representative private set of approximately 500 images, including processing, matching, browser review on Windows and Pixel, and reproducible evaluation export.

## Work items

- [WI-0017](../work-items/WI-0017-evaluation.md)
- [WI-0027](../work-items/WI-0027-review-workflow.md)
- [WI-0028](../work-items/WI-0028-catalogue-evaluation-export.md)
- [WI-0029](../work-items/WI-0029-local-acceptance-pilot.md)

## Exit criteria

- A 450–550 image private subset completes batch processing and can resume safely after interruption.
- The same catalogue is reviewed from Windows and Pixel over a trusted local network.
- People can be created and maintained; faces can be assigned, rejected, undone and reviewed from ranked suggestions.
- Human labels and review actions remain canonical when suggestions are regenerated.
- A reviewed catalogue can produce a reproducible model-lab dataset without manually copying embeddings.
- The pilot records privacy-safe counts, throughput, usability findings and defects.

## Deliberate boundary

This milestone proves local functional fit and operational usability. A 500-image subset is not large enough by itself to select a production model for the full archive; broader model selection remains M11.
