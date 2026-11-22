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

- [x] The source subset is representative and recorded by privacy-safe aggregate categories.
- [x] Batch processing completes, survives restart and can resume without duplicate canonical results.
- [x] The review application is used from Windows and Pixel to create and maintain people, assign and reject faces, undo actions, use bulk actions and review suggestions.
- [x] Matcher regeneration does not change human labels or resurrect rejected pairs.
- [x] The reviewed catalogue exports a reproducible gallery, validation and held-out test dataset.
- [x] Detector recall, identification precision, unknown rejection, confusion, throughput, storage and review effort are recorded.
- [x] Database backup, restore and cleanup are exercised before the pilot is accepted.
- [x] Defects and usability gaps are documented with severity and a decision to fix, defer or accept each one.
- [x] Evidence shared in the repository contains no private images, face crops, embeddings, names or local source paths.

## Completion evidence

The human maintainer completed the private local pilot on 2026-07-30. The representative subset remained within the 450–550 image acceptance range. Batch restart and resume, Windows and Pixel review, matcher regeneration, deterministic catalogue export, evaluation, aggregate metric capture, storage measurement, database backup, restore and cleanup all completed successfully.

Only privacy-safe conclusions are recorded in the repository. The private catalogue, source photos, crops, embeddings, names, raw manifests, reports and local paths remain outside version control.

## Accepted pilot finding

The baseline is functionally correct, but sustained review is too slow on both Windows and Pixel. Exact elapsed review time was not captured, so the effort finding is qualitative rather than a throughput baseline. The issue is classified as an S3 usability defect with disposition **fix before proceeding**.

[WI-0033](WI-0033-review-throughput.md) addresses the observed click cost: keyboard submission when creating people, continuous previous/next navigation, automatic advance after accepting suggestions, suggestion summaries in the gallery, per-person assignment audit and suggestion-aware grouping for bulk review.

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
