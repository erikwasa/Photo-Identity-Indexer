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

## Product direction (maintainer 2026-11-02)

**Face recognition in videos is wanted**, but the architecture,
review UX and accuracy requirements are not fixed. This item should
begin as a **bounded feasibility prototype and requirements discovery**,
not automatically implement person tracks, a full review workflow,
large retention schemes or a video-wide identity engine. It was initially
proposed behind playback/compatibility priority; see the new 2026-11-05
experimental-branch scheduling decision below.

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

- Status: proposed; video face recognition remains a desired feature,
  but the first workitem is only a feasibility/prototype decision.

## Experimental development checkpoint — 2026-11-05

The maintainer selected WI-0155 for parallel work and explicitly chose a
**long-lived `wi-0155` integration branch**. Feature changes are reviewed
as pull requests targeting `wi-0155`; merging them into production
`main` is deferred until a separate human decision. This scheduling
decision supersedes the 2026-11-02 instruction not to start yet.

The first feature branch `wi-0155-offline-frame-feasibility` introduces
an isolated, opt-in, pre-extracted-local-frame CLI with the **existing
verified YuNet/SFace models and OpenCV face-alignment contracts**. It
reports aggregate detection/embedding counts, elapsed time and a
tentative adjacent-frame near-repeat count. Input is capped at
12 ordinary JPEG/PNG stills / ten minutes, from one explicitly selected
clip manifest, with no original video access, OneDrive, PostgreSQL,
canonical identity writes, persistent embeddings or API/UI changes.
Source-free fake-model tests exercise input limits, privacy-safe
output and one-to-one repeat-count behavior.

This is development infrastructure for an **evaluation**, not a
claim that the models correctly identify familiar people in videos.
A human-approved private pilot, known-person comparison design,
miss/duplicate review and a smallest-production-slice decision
remain pending. None of the work-item acceptance checkboxes is
fulfilled by implementation alone. See
[the experimental Windows procedure](../../operations/video-face-feasibility-wi0155.md).
