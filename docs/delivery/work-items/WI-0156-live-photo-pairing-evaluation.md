---
id: WI-0156
title: Evaluate iPhone Live Photo pairing as one logical media experience
milestone: M30
status_source: ../status/work-items.yaml
depends_on: [WI-0151]
related_adrs: []
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Api, PhotoIdentity.Web, docs]
---

# WI-0156: Evaluate iPhone Live Photo pairing as one logical media experience

## Administrative retirement — 2026-11-02

The maintainer explicitly decided **Live Photo pairing is not
needed** for the current product and M30 milestone. This workitem is
**retired without implementation**, not a failed evaluation and not
a claim that the application has reliable Live Photo pairing.

No further pairing inventory, metadata matching, UI, review,
slideshow or source-migration work is requested. Existing still
photos and short MOV videos remain independently catalogued under
their ordinary supported-media rules. Physical source assets must
never be deleted or merged to emulate a pairing relationship.

The repository's milestone lifecycle currently represents
administrative retirement with terminal `completed` status (as
used for retired Azure workitems WI-0020 and WI-0021). The evidence
and this document explicitly distinguish that lifecycle state from
delivery of a Live Photo feature.

## Objective (retired)

Determine whether paired still + MOV Live Photo assets should be
presented as one logical media item. **No longer an M30 goal.**

## Completion notes

- **Disposition:** administrative closeout only, no implementation.
- **Decision:** maintainer removed Live Photo pairing from planned
  video support on 2026-11-02.
- **Future:** if pairing becomes useful, create a new separately
  scoped workitem rather than reopening this retired feature.
