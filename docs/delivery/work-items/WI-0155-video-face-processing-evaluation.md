---
id: WI-0155
title: Evaluate face discovery and identity processing inside video
milestone: M30
status_source: ../status/work-items.yaml
depends_on: [WI-0151, WI-0188]
related_adrs: [ADR-0006]
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Recognition.Onnx, PhotoIdentity.Api, PhotoIdentity.Persistence.Postgres, docs]
---

# WI-0155: Evaluate face discovery and identity processing inside video

## Current status — 2026-11-08

**In progress on the long-lived `wi-0155` feature branch**, which remains
separate from production `main`. Experimental development started on
2026-11-05; PRs [#757](https://github.com/erikwasa/Photo-Identity-Indexer/pull/757)
(read-only existing-catalogue identity evidence) and
[#760](https://github.com/erikwasa/Photo-Identity-Indexer/pull/760)
(higher-resolution, denser frame coverage) were merged into that feature
branch, **not `main`**.

The current feasibility question is whether a few explicitly approved local
videos provide at least one *usable face observation per visible person*,
and, when eligible existing Photo Identity identity evidence exists, useful
**read-only, ranked known-person suggestions** for human review. Include
unknown-to-gallery people, missed faces, ambiguous and incorrect matches,
runtime and human review effort; do not mistake more detected frames or
higher raw similarity for identification accuracy. No production catalogue
writes, canonical identity assignments, background archive scans or
automatic video hydration are authorized.

The detailed experiments and evolving acceptance procedure remain on
`wi-0155`. A final maintainer decision about **integration into Photo
Identity versus a separate component** is deliberately deferred until
multi-video review. This status update does not merge the prototype or
approve production recognition.

## Product direction (maintainer 2026-11-02)

**Face recognition in videos is wanted**, but the architecture,
review UX and accuracy requirements are not fixed. This item should
begin as a **bounded feasibility prototype and requirements discovery**,
not automatically implement person tracks, a full review workflow,
large retention schemes or a video-wide identity engine. It was initially
proposed behind playback/compatibility priority; the 2026-11-05
isolated-feature-branch decision superseded that scheduling constraint.

A prototype **may live in a separate experimental repository** if
that best isolates dependencies and development churn. Such a
prototype must use approved private local fixtures and must not
write to the maintained Photo Identity catalogue, source originals
or canonical identities. A separate repository is an option, not
a mandatory gate or excuse to reimplement the production data model.

## First stage — limited prototype / discovery

- Reuse existing photo face-detection/embedding models on **a few
  operator-selected already-local video frames** with timestamps.
- Establish which actual user tasks matter: identifying a known
  person in a clip, locating their appearances and choosing a
  representative still. Unknown person outcomes are legitimate.
- Observe rough quality, duplicate detections and per-clip run time,
  without requiring statistically significant population recall,
  GPU accounting or full tracking accuracy as a first milestone.
- Try basic near-adjacent-frame deduplication only if duplication
  materially affects the output. Temporal multi-object tracking
  is an **optional subsequent experiment**, not a prerequisite.
- Document a human-reviewed decision about the **smallest useful
  next production slice** and required safeguards. A no-go may
  reject a particular implementation approach, not the entire
  long-term product goal.

## Out of scope for this first stage

- Automatic canonical identity assignment, photo review-queue
  flooding, mandatory track start/end models, bulk archive scanning,
  a new recognition service and large-scale precision/recall
  evaluation before a promising prototype exists.
- Face processing as a prerequisite for ordinary video playback,
  captions, collection browsing or online-only hydration.

## Acceptance criteria — initial evaluation only

- [ ] A small approved real clip/frame set shows whether existing
  models can find and suggest known people, with explicit examples
  of duplicates, misses and uncertainty.
- [ ] Basic runtime/review burden is recorded; a rough, reproducible
  comparison is sufficient for the initial decision.
- [ ] Maintainer reviews the prototype and identifies the desired
  user behavior and a smallest safe production follow-up, or records
  why further experimentation is needed. No unreviewed canonical
  assignments or original-file changes occur.
- [ ] Any complex tracking, production persistence or full-scale
  benchmarking becomes a **separately scoped workitem** only when
  evidence shows it is worth doing.

## Verification requirements

Bounded prototype and maintainer review. No changes to production
catalogue or biometric governance during exploratory work.

## Completion notes

- Status: in_progress on `wi-0155`; the first work item is still a
  feasibility/prototype decision, not production recognition acceptance.
