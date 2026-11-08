---
id: WI-0213
title: Reevaluate video-level descriptions and search after face feasibility
milestone: M30
status_source: ../status/work-items.yaml
depends_on: [WI-0155, WI-0190, WI-0191]
related_adrs: [ADR-0012, ADR-0013]
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Api, PhotoIdentity.Persistence.Postgres, PhotoIdentity.Web, docs]
---

# WI-0213: Reevaluate video-level descriptions and search after face feasibility

## Reminder and trigger (maintainer 2026-11-08)

**Proposed/deferred until WI-0155 yields useful multi-video evidence and
a decision about its future architecture.** The completed WI-0190/0191
scope gives us timestamped *still-frame* captions/embeddings and a
small-cohort opt-in Video Search. That is valid frame evidence, **not**
a description of what a whole moving video contains.

The WI-0155 experiment may expose substantially more sampled frames
per video (its up-to-180-frame experiment is **not** a production
sampling requirement). Revisit video descriptions, evidence coverage,
processing cost and retrieval together when representative clips and
a safe identity-data boundary are better understood. Do not reopen or
invalidate the accepted WI-0190/0191 results.

## Questions to evaluate, not predetermined implementation

- What useful **video-level summary** can the system create from
  ordered temporal evidence, instead of displaying a list of unrelated
  still captions? Consider subjects, changing scenes, approximate
  sequence, visible activities and meaningful uncertainty.
- Compare a few coherent *clip-level* summaries with existing
  individual frame captions on human-reviewed real examples. Record
  invented actions, missed scenes, continuity errors and cases where
  no credible whole-clip summary is possible.
- Decide how sampling/scene coverage should scale when the available
  evidence increases: sampling strategy, quality, timestamps, repetition
  and budgets for caption/embedding work. **More frames do not
  automatically mean more understanding or better search.**
- Preserve timestamped supporting evidence and distinguish visible
  frame facts from temporal inferences. Still frames alone cannot
  establish exact action duration, dialogue, sound, speech or an
  unsampled event. Audio-aware analysis or transcription requires a
  separate privacy and implementation decision.
- If WI-0155 results are useful and **explicitly authorized** for
  production, evaluate whether separately proven known/unknown
  person signals improve descriptions, and keep provenance/manual
  person tags distinct from inferred identity evidence.
- Revisit Video Search retrieval and result presentation: one ranked
  result per video with useful timestamps, clip-level summary vs
  frame-level match, avoiding artificial ranking dominance from videos
  with many samples and preserving photo-search isolation.
- Establish practical cost, privacy, model/processing governance,
  revision and exclusion invalidation, safe fallback, and whether
  an initial **bounded evaluation** is enough before any new
  production description model, index or persisted schema is proposed.

## Out of scope until separately decided

- Treating the experimental 180-frame sampling cap as mandatory
  production processing or automatically reprocessing the archive.
- Auto-transcription, speech/voice identity, unapproved cloud models
  or ungrounded claims about sounds/events between sampled frames.
- Rewriting production photo captions/search, auto-assigning
  canonical identities, or changing existing Video Search defaults.
- Mandatory full tracking or automatic video summarization as a
  prerequisite for video browsing/playback or WI-0212 manual metadata.

## Acceptance criteria — scope reminder/evaluation

- [ ] Review concrete WI-0155 outcomes and choose a representative,
  explicitly approved known/unknown multi-video evaluation set.
- [ ] Compare frame-only results with at least one practical,
  human-reviewed video-level description approach; explicitly
  document temporal coverage, false claims and uncertainty.
- [ ] Measure retrieval usefulness, rank fairness with different
  frame counts, user-facing summary/timestamp quality and
  approximate runtime/storage cost against current Video Search.
- [ ] Preserve opt-in processing, private provenance,
  source/revision/exclusion checks, original safety and unchanged
  photo-search contracts throughout the trial.
- [ ] Record a maintainer decision: small versioned production slice,
  further experiment or defer; define independent follow-up
  acceptance before changing the production pipeline.

## Verification requirements

Initially a design/evaluation review only, after WI-0155 progresses.
Any later implementation requires focused model/evidence tests and
maintainer inspection of private clips; no implicit production
schema/model/index or archive-wide processing commitment.

## Completion notes

- Status: proposed/deferred; reminder work item only. Not started.
- WI-0190/0191 remain completed under their accepted scoped criteria.
