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

- [ ] The user can assign year, year-month or full date.
- [ ] Manual dates are visibly distinct from extracted metadata.
- [ ] Clearing restores extracted value when present.
- [ ] Edits survive restart on phone and desktop.
- [ ] Tests cover validation, replace and clear.

## Verification requirements

Automated HTTP/UI coverage plus maintainer verification at all three precision levels.

## Completion notes

- Files changed:
- Trade-offs:
- Deferred work:
- Commands run:
