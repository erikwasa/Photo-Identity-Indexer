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

- [x] Near-duplicate grouping uses derived versioned evidence and never merges source assets.
- [x] A local proxy-based experiment reports usefulness and obvious false-group behavior on representative bursts.
- [x] The selector can suppress repeated frames while retaining at least one suitable representative.
- [x] Exact duplicates and visually similar but distinct photos remain distinguishable concepts.
- [x] Automated tests cover deterministic grouping and selector interaction around threshold/tie cases.

## Verification requirements

Automated grouping/selector tests plus maintainer inspection of privacy-safe aggregate results and representative private burst groups.

## Completion notes

- Files changed: Core visual-fingerprint/grouping model, Creative selector redundancy penalty, OpenCV dHash calculator, private experiment API, Core/integration tests and operational documentation.
- Trade-offs: grouping is intentionally conservative: same inferred moment, at most 20 seconds total span and complete-link hash similarity. The selector uses a strong penalty instead of hard exclusion so large targets remain achievable.
- Threshold state: private representative review accepted `m26-dhash64-h8-20s-v1` as the initial normal Creative policy. Hamming 4 and 6 found no groups in the reviewed 84-photo burst-heavy sample; Hamming 8 found two two-photo groups, and both groups were visually judged redundant enough that one slideshow representative was sufficient.
- Privacy/storage: hashes are regenerated from already-local durable review proxies. No original hydration, source merge, destructive deduplication or new database schema is introduced.
- Maintainer verification: all 84 sampled candidates had usable local review proxies (0 missing, 0 unreadable). Hamming 8 grouped 4 photos into 2 redundant groups with 2 suppressible frames and reduced selected repeated frames from 2 to 1 in the experiment. Both H8 groups were visually reviewed and accepted as genuine redundancy. The accepted H8 policy is wired into normal Creative materialization as optional derived evidence; missing/unreadable proxies never hydrate originals or fail Creative generation.
- Commands run: PR #363 validated the evaluation implementation; the completion follow-up adds the accepted H8 normal-materialization path and regression coverage.
