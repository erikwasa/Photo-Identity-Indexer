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

Classify exact-model identity suggestions into user-configurable High, Medium and Low groups and optionally convert qualifying High suggestions into canonical identity assignments. High confidence is intentionally conjunctive: rank 1 must have both a sufficiently strong absolute score and a sufficiently large score gap over rank 2.

## Why

Manual acceptance of every strong suggestion will become a major review bottleneck as the permanent catalogue grows. The maintainer has explicitly chosen a new direction in which sufficiently strong model matches may become canonical automatically when that behavior is enabled. Absolute score alone is not enough evidence when two people have nearly equal scores, so the rank-1/rank-2 gap must also participate in the High-confidence policy.

## In scope

- Persist an exact-model suggestion policy with adjustable High score, High rank-1/rank-2 gap, and Medium score boundaries.
- Define High as rank-1 score at or above the configured High score threshold **and** a persisted rank-1/rank-2 score margin at or above the configured High margin threshold.
- Treat a strong absolute score with an insufficient or unavailable rank-2 margin as non-High; if it still meets the Medium score threshold it belongs to Medium.
- Keep automatic assignment disabled by default and expose an explicit on/off setting.
- Add group badges, filtering and ordering to the review queue.
- When enabled, convert eligible High top suggestions into normal canonical assignments with append-only audit history.
- Record enough provenance to identify the exact model revision, suggestion score, rank-1/rank-2 margin, configured thresholds/policy version and automatic actor that created an assignment.
- Allow automatic assignments to become active exemplars in later regeneration runs.
- Make a later manual reassignment supersede the automatic assignment normally; the manually selected identity then becomes the active exemplar identity.
- Use one fixed exemplar snapshot for a regeneration. Score all targets first, then apply qualifying automatic assignments so newly automatic exemplars cannot cascade through the same run.
- Preserve rejected face-person exclusions and all existing review-history guarantees.

## Out of scope

- Automatically undoing existing assignments when thresholds are later changed.
- Automatically assigning Medium or Low suggestions.
- Person-specific thresholds or confidence heuristics based on ranks beyond the rank-1/rank-2 comparison unless separately introduced after observing real results.

## Acceptance criteria

- [ ] High, Medium and Low policy boundaries are persisted, validated and editable.
- [ ] High classification requires both the configured minimum rank-1 score and minimum rank-1/rank-2 score gap.
- [ ] A rank-1 suggestion with a strong absolute score but too-small or unavailable rank-2 gap is not classified High.
- [ ] The review queue can filter and order by confidence group and shows the computed group on suggestion cards.
- [ ] With auto-assignment off, regeneration never creates a canonical assignment solely from a suggestion.
- [ ] With auto-assignment on, an eligible High top suggestion creates an auditable canonical assignment.
- [ ] Automatic assignment provenance records the exact model, rank-1 score, rank-1/rank-2 margin, policy version and thresholds used.
- [ ] Automatic assignments do not affect candidate scoring until a later regeneration run.
- [ ] A manual reassignment supersedes an automatic assignment and changes the active exemplar identity used by later matching.
- [ ] Threshold changes affect future decisions without silently rewriting historical assignments.

## Verification requirements

Automated tests must cover score-band boundaries, the High rank-gap boundary, a strong-score/ambiguous-rank rejection case, missing rank-2 margin behavior, toggle behavior, fixed-snapshot non-cascade behavior, audit provenance and manual reassignment. Human verification must tune representative score and rank-gap thresholds against a private reviewed sample before auto-assignment is enabled for routine archive use.

## Completion notes

- Files changed:
- Trade-offs:
- Deferred work:
- Commands run:
