---
id: WI-0142
title: Add manual and imprecise capture-date editing to Photo Details
milestone: M28
status_source: ../status/work-items.yaml
depends_on: [WI-0141]
related_adrs: []
affected_modules: [PhotoIdentity.Api, PhotoIdentity.Web, PhotoIdentity.Integration.Tests, docs]
---

# WI-0142: Add manual and imprecise capture-date editing to Photo Details

## Objective

Expose manual capture-date editing in Photo Details while clearly showing precision and provenance.

## Why

Family archive photos sometimes lack trustworthy EXIF dates, but approximate year or month information is still valuable for discovery and slideshows.

## In scope

- Add a compact editor for year, year-month or full-date values.
- Show extracted versus manual source and precision.
- Allow replacement and clearing.
- Refresh dependent details without modifying the original.
- Keep mobile input practical.

## Out of scope

- Editing arbitrary EXIF fields.
- Forcing a day when only year/month is known.
- Batch assignment in the first slice.

## Acceptance criteria

- [x] The user can assign year, year-month or full date.
- [x] Manual dates are visibly distinct from extracted metadata.
- [x] Clearing restores extracted value when present.
- [x] Edits survive restart on phone and desktop.
- [x] Tests cover validation, replace and clear.

## Verification requirements

Automated HTTP/UI coverage plus maintainer verification at all three precision levels.

## Completion notes

- Files changed: Photo Details capture-date contracts/endpoints, the new responsive `PhotoCaptureDateEditor`, Photo viewer composition, extracted-metadata labeling, editor-model coverage, PostgreSQL application coverage and delivery tracking.
- Trade-offs: the editor uses one compact text field for `YYYY`, `YYYY-MM` or `YYYY-MM-DD`, preserving imprecision while staying practical on phone. Extracted metadata remains visible separately and is never rewritten. The temporary SQLite compatibility provider projects extracted dates read-only; manual mutation remains PostgreSQL-only ahead of M29.
- Deferred work: none for WI-0142. Structured Smart Collection date controls are delivered separately by WI-0143.
- Verification: maintainer accepted WI-0142 on 2026-09-22 during the combined M28 manual review, covering manual/imprecise date editing and the WI-0141 model behavior.
- Commands run: implementation prepared through the GitHub connector; PR #394 runs the normal build/integration/docs/package/PostgreSQL CI gates.
