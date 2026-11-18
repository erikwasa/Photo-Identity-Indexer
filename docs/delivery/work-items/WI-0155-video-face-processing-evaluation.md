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


## 2026-11-18 — Offline main integration candidate (pending acceptance)

**Proposed integration:** Only the standalone opt-in video-face feasibility
tool, its Windows/Node/C# tests and documented private review runners
are under consideration for `main`. Normal Photo Identity APIs,
workers, still-image indexing, database schema, identity persistence
and automatic video processing are **not changed**. The full
chronological experiment diary remains on the `wi-0155` branch;
this checkpoint is the concise integration summary.

**Production workflow decision:** A video should carry a **deduplicated
set of known people confirmed present**, not an exhaustively resolved
set of face groups. One credible face crop may be sufficient.
Provisional groups and top-three suggestions assist human discovery.
Repeated groups of the same person need not merge. A mixed group may
have an incorrectly grouped crop annotated as excluded and that
person independently confirmed. Other groups can remain explicitly
unreviewed. The operator may stop *manual* review when satisfied;
automatic video sampling must still report bounded coverage honestly.
The current standalone tool produces only **private proposals**,
not production associations. No automatic identity assignment is approved.

**Evidence from the private four-pass experiment:** The approved A/B
coarse/refined evaluation used 204 frames, 530 YuNet detections
and embeddings, an eligible read-only snapshot of 240 people and
596 still-image exemplars. V04/V06 coarse human review found
2/7 distinct known people, but only 0/2 and 1/7 known people
were suggested correctly in the top three. The separately reviewed
refined shortlist confirmed two known people on each video and
clearer crops for two people on A and one on B; B also yielded
three wrong-person suggestions, one background detection and 13
missing-geometry ranked occurrences. These are bounded human-review
observations, **not whole-video person recall or precision**.

**Rejected or removed:** Batch merge of already-labelled groups
has been removed, while optional pairwise diagnostic merging remains.
No mandatory complete group classification, hard independent-detector
suppression, requirement of multiple appearances, mandatory dense
refinement, video-reference enrolment or uncalibrated auto-matching.
The historical G/C/N/U counterfactuals and auditors are retained as
**optional offline diagnostics**, not new production processing.

**Current acceptance blockers:** Main-based full CI/integration
verification, local V04/V06 crop-review Save/Import and source-bound
aggregate validation, runtime/face-model rights and redistribution,
private crop retention/deletion, startup/network/database no-write
checks and a rollback plan. Persisting video/person presence in
production remains a separate future implementation choice.
`WI-0155` is still **in progress**.

See [pre-main decision/removal record](../../operations/video-face-wi0155-pre-main-decision-record.md)
and [bounded offline verification](../../operations/video-face-feasibility-wi0155.md).

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
