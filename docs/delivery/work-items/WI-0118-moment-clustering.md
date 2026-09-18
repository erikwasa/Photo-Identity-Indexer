---
id: WI-0118
title: Prototype timestamp-first photo moment clustering
milestone: M26
status_source: ../status/work-items.yaml
depends_on: [WI-0050, WI-0101]
related_adrs: [ADR-0009]
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Persistence.Postgres, PhotoIdentity.Api, PhotoIdentity.Web, PhotoIdentity.Core.Tests, PhotoIdentity.Persistence.Tests, PhotoIdentity.Integration.Tests, docs]
---

# WI-0118: Prototype timestamp-first photo moment clustering

## Objective

Define, implement and evaluate a regenerable photo-moment clustering policy that groups nearby family photos into useful sessions primarily from capture time, with other available metadata used only as optional supporting evidence.

## Why

Creative Collections need a way to include photographs that belong to the same memory even when they do not depict the selected person. The archive cannot rely on travel or GPS coverage, so the first grouping layer should work from metadata that is broadly available and should remain useful for ordinary days at home.

A moment is an inferred presentation grouping, not a canonical event or user-authored fact.

## In scope

- Define a versioned `photo moment`/moment-membership contract as derived, regenerable presentation evidence.
- Use photographic capture time as the primary grouping signal.
- Evaluate one or more simple time-gap/session policies against a private representative archive sample before fixing initial defaults.
- Allow optional supporting evidence such as overlapping identified people, generic tags, source/path proximity and GPS/place metadata when available.
- Do not require GPS, named places or semantic image embeddings.
- Define explicit handling for missing or unreliable capture timestamps; uncertain photos may remain singleton/unclustered rather than being forced into a moment.
- Keep grouping deterministic for the same input catalogue and policy version.
- Expose enough read-only API/UI diagnostics to inspect inferred moment boundaries and member photos during evaluation.
- Record aggregate evaluation observations without committing personal filenames, names, paths, photos or private sample data.
- Keep PostgreSQL as the production catalogue authority and avoid introducing a second writable moment store unless measured need justifies persistence.

## Out of scope

- Naming or classifying moments as birthdays, holidays, trips or other semantic event types.
- Treating a moment ID as a permanent archive identifier.
- Creative anchor/context expansion.
- Target-count photo selection.
- Image-content embeddings or visual-similarity models.
- Editing canonical people, tags, Places or capture metadata from moment tooling.

## Acceptance criteria

- [x] The code/documentation defines moments as derived/regenerable and distinct from canonical archive metadata.
- [x] Initial grouping works from capture time alone and does not require location metadata.
- [x] At least two plausible time-gap/session policies or parameter sets are compared on the same private representative sample before an initial policy is selected.
- [x] Evaluation includes ordinary home/family sequences as well as any available outings so the chosen behavior is not travel-biased.
- [x] Missing/ambiguous capture timestamps have an explicit conservative behavior and are not silently clustered by unrelated catalogue-observation/import time.
- [x] Optional people/tag/path/location evidence can strengthen or split a candidate grouping without becoming mandatory input.
- [x] For the same catalogue state and policy/version, moment membership and ordering are deterministic.
- [x] A maintainer can inspect representative inferred moments and obvious split/merge mistakes through a bounded read-only diagnostic/preview path.
- [x] Automated tests cover time-gap boundaries, identical timestamps, midnight/day transitions, missing capture times, deterministic tie-breaking and optional-evidence absence.
- [x] Private evaluation data remains outside the repository.

## Verification requirements

Automated tests are required for deterministic grouping and edge cases. Human maintainer verification is required against a private representative family-photo sample to judge whether inferred boundaries are useful enough to support WI-0119.

## Implementation notes

- `PhotoMomentClusterer` is a pure Core derivation over canonical photo revision/capture metadata. It never writes moment membership.
- Missing `TakenAtLocal` remains explicitly unclustered; `ObservedAtUtc` is never used as a capture-time fallback.
- Ordering is capture wall-clock time followed by revision ID, so repository enumeration order cannot change the result.
- The private representative review compared the explicit 30-minute and 90-minute time-gap policies and selected `m26-time-gap-30m-v1` as the initial default. The 90-minute policy remains available as an explicit comparison policy.
- `PhotoMomentGapPolicy` contains bounded optional-evidence seams for shared people/tags/source groups, nearby location support and distant-location splitting. The initial evaluation candidates leave all optional-evidence refinements disabled.
- `GET /api/moments/preview` reads timestamped photos through the existing catalogue query abstraction, compares both gap candidates over the same bounded page and exposes revision IDs/capture times/thumbnail URLs without persisting unrelated state.
- See [Moment clustering evaluation](../../operations/moment-clustering-evaluation.md) for private evaluation steps and privacy boundaries.

## Completion notes

- Files changed: Core clustering contract/policy, Core tests, bounded API preview, host route wiring, evaluation documentation and delivery status.
- Trade-offs: the first diagnostic intentionally samples at most 200 timestamped photos per request and flags potentially truncated boundary moments; this keeps evaluation bounded without introducing a dedicated persisted moment index.
- Maintainer verification: representative private archive review compared the 30-minute and 90-minute policies over multiple bounded samples and selected 30 minutes as the more coherent initial default. Only aggregate observations are recorded; private photo identities remain outside the repository.
- Commands run: automated verification was covered by the implementation CI; maintainer private evaluation completed on 2026-09-18.
