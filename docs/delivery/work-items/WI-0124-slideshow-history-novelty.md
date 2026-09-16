---
id: WI-0124
title: Track slideshow exposure and novelty for Creative Collections
milestone: M26
status_source: ../status/work-items.yaml
depends_on: [WI-0120]
related_adrs: []
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Api, PhotoIdentity.Web, PhotoIdentity.Persistence.Postgres, PhotoIdentity.Integration.Tests, docs]
---

# WI-0124: Track slideshow exposure and novelty for Creative Collections

## Objective

Record lightweight presentation history so repeated Creative Collections can favor useful photos that have not been shown recently.

## Why

A deterministic selector can otherwise converge on the same strongest photos every time. Presentation history can add freshness without redefining photo quality or canonical archive metadata.

## In scope

- Define presentation-history state such as last shown time and show count for immutable revisions.
- Record exposure only after a photo is actually presented under a defined slideshow/session rule rather than merely selected in a candidate set.
- Add an optional novelty preference that penalizes recently/frequently shown photos while respecting anchors, explicit presentation preferences and target count.
- Support discovery concepts such as rarely shown or not shown recently without changing ordinary Smart Collection membership.
- Bound history storage/query cost at archive scale.

## Out of scope

- Inferring dislike from navigation speed or accidental skips.
- Cross-user personalization/authentication.
- Treating show history as canonical photographic metadata.

## Acceptance criteria

- [ ] Slideshow exposure is persisted as presentation state separate from archive/identity facts.
- [ ] The application can report last-shown/show-count signals for eligible revisions without exposing private source paths.
- [ ] An optional novelty policy can produce a materially different valid selection while preserving hard eligibility and presentation-preference rules.
- [ ] History updates are idempotent/restart-safe under the chosen session semantics.
- [ ] Automated tests cover repeated sessions, unseen photos and history-disabled behavior.

## Verification requirements

Automated persistence/selection tests plus maintainer comparison of repeated Creative Collection runs with novelty disabled and enabled.

## Completion notes

- Files changed:
- Trade-offs:
- Deferred work:
- Commands run:
