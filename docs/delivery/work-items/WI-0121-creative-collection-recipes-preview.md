---
id: WI-0121
title: Productize Creative Collection recipes and previews
milestone: M26
status_source: ../status/work-items.yaml
depends_on: [WI-0120]
related_adrs: []
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Api, PhotoIdentity.Web, PhotoIdentity.Persistence.Postgres, PhotoIdentity.Integration.Tests, docs]
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

- [ ] A Creative Collection recipe can be saved, loaded and regenerated without changing its anchor Smart Collection.
- [ ] Preview exposes anchor, context, candidate and final selected counts with clear provenance.
- [ ] The operator can adjust at least target count and context strength/policy before playback.
- [ ] Regeneration is deterministic for a fixed catalogue/policy version and final playback remains snapshot-based.
- [ ] Automated tests cover persistence, preview provenance, regeneration and zero/small/large result sets.

## Verification requirements

Automated repository/integration tests plus maintainer review of at least two representative private recipes before treating this as the normal Creative Collection entry point.

## Completion notes

- Files changed:
- Trade-offs:
- Deferred work:
- Commands run:
