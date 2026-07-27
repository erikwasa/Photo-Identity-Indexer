---
id: WI-0029
title: Run a 500-image local acceptance pilot
milestone: M06
status_source: ../status/work-items.yaml
depends_on: [WI-0027, WI-0028]
affected_modules: [PhotoIdentity.Cli, PhotoIdentity.Api, PhotoIdentity.Web, PhotoIdentity.Persistence.Sqlite, docs/operations]
---

# WI-0029: Run a 500-image local acceptance pilot

## Objective

Exercise the complete baseline system locally on a representative private set of 450–550 images and decide whether the workflow meets practical expectations.

## Acceptance criteria

- [ ] The source subset is representative and recorded by privacy-safe aggregate categories.
- [ ] Batch processing completes, survives restart and can resume without duplicate canonical results.
- [ ] The review application is used from Windows and Pixel to create and maintain people, assign and reject faces, undo actions, use bulk actions and review suggestions.
- [ ] Matcher regeneration does not change human labels or resurrect rejected pairs.
- [ ] The reviewed catalogue exports a reproducible gallery, validation and held-out test dataset.
- [ ] Detector recall, identification precision, unknown rejection, confusion, throughput, storage and review effort are recorded.
- [ ] Database backup, restore and cleanup are exercised before the pilot is accepted.
- [ ] Defects and usability gaps are documented with severity and a decision to fix, defer or accept each one.
- [ ] Evidence shared in the repository contains no private images, face crops, embeddings, names or local source paths.

## Matcher regeneration prerequisite

Use the supported operator command with the exact embedding model revision:

```powershell
dotnet run --project src/PhotoIdentity.Cli -- `
  match regenerate `
  --database C:\PhotoIdentityPilot\catalogue.db `
  --embedder-id sface-2021dec-fp32 `
  --embedder-hash EMBEDDER_SHA256
```

The command reports only model provenance and aggregate target/suggestion counts. It rebuilds ranked suggestions from current human-confirmed exemplars, preserves durable rejected face-person exclusions and never creates or changes canonical labels.

During the pilot, reject at least one suggestion, record the aggregate counts, run regeneration again and confirm the rejected pair remains absent while human labels and append-only review history remain unchanged.
