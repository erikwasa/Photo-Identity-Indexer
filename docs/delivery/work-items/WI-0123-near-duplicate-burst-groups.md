---
id: WI-0123
title: Detect burst and near-duplicate photo groups for creative selection
milestone: M26
status_source: ../status/work-items.yaml
depends_on: [WI-0120]
related_adrs: []
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Imaging.OpenCv, PhotoIdentity.Persistence.Postgres, PhotoIdentity.Core.Tests, PhotoIdentity.Persistence.Tests, docs]
---

# WI-0123: Detect burst and near-duplicate photo groups for creative selection

## Objective

Detect visually redundant photos inside moments so Creative Collection selection can avoid showing long runs of effectively the same scene.

## Why

Timestamp/moment diversity cannot distinguish several frames taken seconds apart that show nearly identical content. A lightweight visual redundancy layer could improve slideshow quality substantially before introducing semantic image models.

## In scope

- Evaluate a bounded proxy-based perceptual similarity approach such as perceptual hashing or another inexpensive OpenCV-compatible descriptor.
- Group obvious bursts/near-duplicates as derived, regenerable evidence attached to immutable revisions.
- Keep exact source duplicate identity separate from visual near-duplicate grouping.
- Prefer one or a small representative number of frames from a redundant group during Creative Collection selection.
- Measure false grouping and missed redundancy on a private representative sample before choosing thresholds.

## Out of scope

- Destructive deduplication or deleting archive photos.
- General semantic image similarity/search.
- Treating visual groups as canonical event/moment identity.

## Acceptance criteria

- [ ] Near-duplicate grouping uses derived versioned evidence and never merges source assets.
- [ ] A local proxy-based experiment reports usefulness and obvious false-group behavior on representative bursts.
- [ ] The selector can suppress repeated frames while retaining at least one suitable representative.
- [ ] Exact duplicates and visually similar but distinct photos remain distinguishable concepts.
- [ ] Automated tests cover deterministic grouping and selector interaction around threshold/tie cases.

## Verification requirements

Automated grouping/selector tests plus maintainer inspection of privacy-safe aggregate results and representative private burst groups.

## Completion notes

- Files changed:
- Trade-offs:
- Deferred work:
- Commands run:
