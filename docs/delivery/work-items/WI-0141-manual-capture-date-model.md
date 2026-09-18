---
id: WI-0141
title: Add manual capture-date overrides with provenance and precision
milestone: M28
status_source: ../status/work-items.yaml
depends_on: [WI-0050]
related_adrs: []
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Api, PhotoIdentity.Persistence.Postgres, PhotoIdentity.Persistence.Tests, docs]
---

# WI-0141: Add manual capture-date overrides with provenance and precision

## Objective

Represent a user-supplied effective capture date separately from extracted metadata, including year-only and year-month precision.

## Why

The current capture model stores one exact extracted timestamp. Reusing it for manual corrections would lose provenance, cannot represent uncertainty cleanly and risks metadata refresh overwriting the user's decision.

## In scope

- Add revision-bound manual capture-date state separate from extracted metadata.
- Support year, year-month and full-date precision without inventing day/time.
- Record manual provenance and reversible history/state.
- Define one effective capture-date/range contract: manual override when present, otherwise extracted time.
- Expose the effective range to query consumers without mutating originals.

## Out of scope

- Writing EXIF/XMP back to originals.
- Guessing missing month/day values.
- Using filesystem observation time as capture truth.

## Acceptance criteria

- [ ] `YYYY`, `YYYY-MM` and `YYYY-MM-DD` can be stored with explicit precision/provenance.
- [ ] Metadata reinspection cannot overwrite the manual override.
- [ ] Clearing reveals the extracted capture value again.
- [ ] Query code can consume an effective inclusive date range.
- [ ] PostgreSQL persistence and reversibility are covered.

## Verification requirements

Repository/persistence tests are required; manual UI verification belongs to WI-0142.

## Completion notes

- Files changed:
- Trade-offs:
- Deferred work:
- Commands run:
