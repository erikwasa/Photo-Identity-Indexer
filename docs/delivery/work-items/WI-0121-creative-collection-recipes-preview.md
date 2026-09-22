---
id: WI-0121
title: Productize Creative Collection recipes and previews
milestone: M26
status_source: ../status/work-items.yaml
depends_on: [WI-0120]
related_adrs: []
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Api, PhotoIdentity.Web, PhotoIdentity.Persistence.Sqlite, PhotoIdentity.Persistence.Postgres, PhotoIdentity.Cli, PhotoIdentity.Integration.Tests, PhotoIdentity.Persistence.Tests, docs]
---

# WI-0121: Productize Creative Collection recipes and previews

## Objective

Turn the M26 experimental pipeline into a reusable, inspectable Creative Collection recipe that can be previewed before slideshow playback.

## Why

Moment/context/selection heuristics are much more useful when an operator can save the intent separately from one generated result, understand how many anchors/context photos were found and regenerate after catalogue changes without changing exact Smart Collection semantics.

## In scope

- Define a versioned Creative Collection recipe referencing an exact Smart Collection anchor source plus context, target-count, diversity and ordering policies.
- Provide preview counts for anchors, expanded candidates, selected photos and represented moments/time periods.
- Preserve provenance so direct matches and contextual additions remain distinguishable.
- Regenerate a recipe deterministically for the same catalogue and policy version.
- Hand the finalized immutable revision sequence to the existing slideshow snapshot/playback boundary.
- Keep recipes separate from ordinary Smart Collection definitions.

## Out of scope

- Natural-language recipe generation.
- Semantic image models as a prerequisite.
- Replacing exact Smart Collections.

## Acceptance criteria

- [x] A Creative Collection recipe can be saved, loaded and regenerated without changing its anchor Smart Collection.
- [x] Preview exposes anchor, context, candidate and final selected counts with clear provenance.
- [x] The operator can adjust at least target count and context strength/policy before playback.
- [x] Regeneration is deterministic for a fixed catalogue/policy version and final playback remains snapshot-based.
- [x] Automated tests cover persistence, preview provenance, regeneration and zero/small/large result sets.

## Verification requirements

Automated repository/integration tests plus maintainer review of at least two representative private recipes before treating this as the normal Creative Collection entry point.

## Completion notes

- Files changed: Creative recipe Core/repository contracts, SQLite/PostgreSQL schema and repositories, shared Creative materialization service, recipe/preview/snapshot API routes, Smart Collections recipe/preview UI, Creative slideshow snapshot routing, focused persistence/integration tests and operational documentation.
- Trade-offs: the first productized shape stores one Creative recipe per exact Smart Collection. Everyday controls are intentionally limited to target count plus Focused/Balanced/Broad context; the accepted 30-minute moment policy, metadata-first diversity selector and chronological ordering remain versioned recipe fields rather than additional UI knobs.
- Persistence: recipes are separate catalogue rows keyed by the anchor Smart Collection and cascade on anchor deletion. SQLite schema advances to 17 and PostgreSQL to 24.
- Playback: Creative mode materializes an immutable recipe snapshot before the existing slideshow viewer starts; Classic slideshow behavior is unchanged and original preparation consumes the same Creative snapshot revision IDs.
- Maintainer verification: representative private saved Creative recipes were reviewed through the Smart Collections preview/save/play flow and accepted as working as intended. No private collection identifiers or photo details are recorded in the repository.
- Follow-on work: later M26 items own photo preferences, history/novelty and semantic experiments. WI-0158 specifically lifts this item's deliberate one-recipe-per-anchor limitation by adding named Creative Collection identities, multiple Creative Collections per Smart Collection and Slideshows-page discovery/launch.
- Commands run: GitHub Actions validation on PR #362 passed build/fast tests, both integration shards, living/generated documentation checks, launcher verification and Windows package verification. Maintainer private recipe review completed on 2026-09-18.
