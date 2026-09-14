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

- [ ] The code/documentation defines moments as derived/regenerable and distinct from canonical archive metadata.
- [ ] Initial grouping works from capture time alone and does not require location metadata.
- [ ] At least two plausible time-gap/session policies or parameter sets are compared on the same private representative sample before an initial policy is selected.
- [ ] Evaluation includes ordinary home/family sequences as well as any available outings so the chosen behavior is not travel-biased.
- [ ] Missing/ambiguous capture timestamps have an explicit conservative behavior and are not silently clustered by unrelated catalogue-observation/import time.
- [ ] Optional people/tag/path/location evidence can strengthen or split a candidate grouping without becoming mandatory input.
- [ ] For the same catalogue state and policy/version, moment membership and ordering are deterministic.
- [ ] A maintainer can inspect representative inferred moments and obvious split/merge mistakes through a bounded read-only diagnostic/preview path.
- [ ] Automated tests cover time-gap boundaries, identical timestamps, midnight/day transitions, missing capture times, deterministic tie-breaking and optional-evidence absence.
- [ ] Private evaluation data remains outside the repository.

## Verification requirements

Automated tests are required for deterministic grouping and edge cases. Human maintainer verification is required against a private representative family-photo sample to judge whether inferred boundaries are useful enough to support WI-0119.

## Completion notes

- Files changed:
- Trade-offs:
- Deferred work:
- Commands run:
