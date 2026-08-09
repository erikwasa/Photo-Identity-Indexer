---
id: WI-0043
title: Add configurable confidence groups and canonical auto-assignment
milestone: M17
status_source: ../status/work-items.yaml
depends_on: [WI-0016, WI-0033]
related_adrs: []
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Persistence.Sqlite, PhotoIdentity.Api, PhotoIdentity.Web]
---

# WI-0043: Add configurable confidence groups and canonical auto-assignment

## Objective

Classify exact-model identity suggestions into user-configurable High, Medium and Low score groups and optionally convert qualifying High suggestions into canonical identity assignments.

## Why

Manual acceptance of every strong suggestion will become a major review bottleneck as the permanent catalogue grows. The maintainer has explicitly chosen a new direction in which sufficiently strong model matches may become canonical automatically when that behavior is enabled.

## In scope

- Persist an exact-model suggestion policy with adjustable score boundaries for High, Medium and Low.
- Keep automatic assignment disabled by default and expose an explicit on/off setting.
- Add group badges, filtering and ordering to the review queue.
- When enabled, convert eligible High top suggestions into normal canonical assignments with append-only audit history.
- Record enough provenance to identify the exact model revision, suggestion score, configured thresholds/policy version and automatic actor that created an assignment.
- Allow automatic assignments to become active exemplars in later regeneration runs.
- Make a later manual reassignment supersede the automatic assignment normally; the manually selected identity then becomes the active exemplar identity.
- Use one fixed exemplar snapshot for a regeneration. Score all targets first, then apply qualifying automatic assignments so newly automatic exemplars cannot cascade through the same run.
- Preserve rejected face-person exclusions and all existing review-history guarantees.

## Out of scope

- Automatically undoing existing assignments when thresholds are later changed.
- Automatically assigning Medium or Low suggestions.
- Person-specific thresholds or margin-based policy unless separately introduced after observing real results.

## Acceptance criteria

- [ ] High, Medium and Low score boundaries are persisted, validated and editable.
- [ ] The review queue can filter and order by confidence group.
- [ ] With auto-assignment off, regeneration never creates a canonical assignment solely from a suggestion.
- [ ] With auto-assignment on, an eligible High top suggestion creates an auditable canonical assignment.
- [ ] Automatic assignment provenance records the exact model and policy evidence used.
- [ ] Automatic assignments do not affect candidate scoring until a later regeneration run.
- [ ] A manual reassignment supersedes an automatic assignment and changes the active exemplar identity used by later matching.
- [ ] Threshold changes affect future decisions without silently rewriting historical assignments.

## Verification requirements

Automated tests must cover score-band boundaries, toggle behavior, fixed-snapshot non-cascade behavior, audit provenance and manual reassignment. Human verification must tune representative thresholds against a private reviewed sample before auto-assignment is enabled for routine archive use.

## Completion notes

- Files changed:
- Trade-offs:
- Deferred work:
- Commands run:
