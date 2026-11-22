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

- [x] `YYYY`, `YYYY-MM` and `YYYY-MM-DD` can be stored with explicit precision/provenance.
- [x] Metadata reinspection cannot overwrite the manual override.
- [x] Clearing reveals the extracted capture value again.
- [x] Query code can consume an effective inclusive date range.
- [x] PostgreSQL persistence and reversibility are covered.

## Verification requirements

Repository/persistence tests are required; manual UI verification belongs to WI-0142.

## Completion notes

- Files changed: Core capture-date precision/range/state contracts, PostgreSQL schema v27 and manual-date repository, PostgreSQL Smart Collection effective-date projection/filtering, DI composition, focused Core/persistence tests, PostgreSQL runtime docs and delivery tracking.
- Trade-offs: extracted `taken_at_local` remains untouched as source evidence. Manual dates are append-only set/clear actions and expose an inclusive date range; year/month precision is never converted into a persisted fake day or time. Smart Collection date filtering uses range overlap. Slideshow ordering may use the range start only as a deterministic presentation key, not as asserted capture precision.
- Deferred work: no SQLite manual-date implementation is added because WI-0141 targets the supported PostgreSQL model and M29 is already scheduled to remove SQLite runtime support. Photo Details editing and structured Smart Collection controls were delivered by WI-0142/WI-0143.
- Verification: maintainer accepted WI-0141 on 2026-09-22 through the combined manual capture-date review with WI-0142.
- Commands run: implementation prepared through the GitHub connector; PR #392 runs the normal build/Core/persistence/integration/docs/package/PostgreSQL CI gates.
