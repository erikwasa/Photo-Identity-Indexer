---
id: WI-0038
title: Roll out the selected detector pipeline
milestone: M16
status_source: ../status/work-items.yaml
depends_on: [WI-0035]
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Persistence.Sqlite, PhotoIdentity.Worker, PhotoIdentity.Web]
---

# WI-0038: Roll out the selected detector pipeline

## Objective

Adopt the first detector pipeline that meets the M16 target without attaching new detections to the wrong reviewed face occurrences.

## Scope

- Give the complete detector pipeline a versioned identity or configuration hash.
- Include threshold, resize, scale, tile, rotation and merge policy in provenance where applicable.
- Reconcile detections by geometry and landmarks rather than ordinal alone.
- Preserve existing people, assignments, rejections and append-only review history.
- Put ambiguous matches and newly found faces into an explicit review path.
- Reprocess the pilot before any full-archive operation.

## Acceptance criteria

- [ ] Detector-pipeline identity distinguishes materially different detection behaviour.
- [ ] Existing reviewed faces cannot silently change person because detection ordering changed.
- [ ] New faces are added without overwriting existing occurrences.
- [ ] Ambiguous reconciliation requires human review.
- [ ] Pilot reprocessing passes the accepted recall target and catalogue invariants.
- [ ] Operator documentation explains migration, rollback and evidence retention.
