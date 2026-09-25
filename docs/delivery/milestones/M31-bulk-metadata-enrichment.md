---
id: M31
title: Bulk archive metadata enrichment
status_source: ../status/milestones.yaml
depends_on: [M19, M28]
---

# M31: Bulk archive metadata enrichment

## Outcome

Photo Identity can safely enrich missing catalogue capture dates and Places at archive scale from explicit, reviewable operator rules instead of requiring one-photo-at-a-time UI editing.

The milestone is catalogue-only. Originals and extracted metadata remain source evidence and are never rewritten. Automation must preserve partial date precision, manual provenance, existing Place/GPS information and ambiguity rather than converting uncertain context into invented facts.

## Work items

- [WI-0163](../work-items/WI-0163-bulk-metadata-enrichment.md) - add a dry-run-first CLI for conservative filename/directory date recovery and explicit date-range-to-Place rules.

## Delivery principles

- Dry-run and inspection before writes.
- Existing effective metadata wins by default.
- Explicit operator rules over inferred travel/location assumptions.
- Partial date precision is first-class and must constrain Place inference.
- Ambiguity is surfaced rather than guessed.
- Use existing append-only PostgreSQL metadata repositories for accepted changes.
- Keep source originals read-only.

## Exit criteria

- [ ] A maintainer can preview recoverable missing dates without changing the catalogue.
- [ ] Catch-all directories such as `1970` cannot accidentally become capture dates.
- [ ] Explicit trip/event date ranges can propose Places only for photos whose full effective date range is contained by the rule.
- [ ] Existing dates, named Places and valid GPS are protected from default bulk replacement.
- [ ] Apply is explicit, auditable, rerunnable and uses existing metadata history models.
- [ ] A private report supports human review of proposed source/revision changes.
- [ ] Maintainer validates a representative production dry-run before broad application.
