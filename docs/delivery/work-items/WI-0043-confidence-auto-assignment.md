---
id: WI-0043
title: Add configurable confidence groups and canonical auto-assignment
milestone: M17
status_source: ../status/work-items.yaml
depends_on: [WI-0016, WI-0033]
related_adrs: [ADR-0006]
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

- [x] High, Medium and Low policy boundaries are persisted, validated and editable.
- [x] High classification requires both the configured minimum rank-1 score and minimum rank-1/rank-2 score gap.
- [x] A rank-1 suggestion with a strong absolute score but too-small or unavailable rank-2 gap is not classified High.
- [x] The review queue can filter and order by confidence group and shows the computed group on suggestion cards.
- [x] With auto-assignment off, regeneration never creates a canonical assignment solely from a suggestion.
- [x] With auto-assignment on, an eligible High top suggestion creates an auditable canonical assignment.
- [x] Automatic assignment provenance records the exact model, rank-1 score, rank-1/rank-2 margin, policy version and thresholds used.
- [x] Automatic assignments do not affect candidate scoring until a later regeneration run.
- [x] A manual reassignment supersedes an automatic assignment and changes the active exemplar identity used by later matching.
- [x] Threshold changes affect future decisions without silently rewriting historical assignments.

## Verification requirements

Automated tests cover score-band boundaries, the High rank-gap boundary, a strong-score/ambiguous-rank rejection case, missing rank-2 margin behavior, exact-model policy isolation/versioning, toggle behavior, fixed-snapshot non-cascade behavior, audit provenance, threshold-history preservation and manual reassignment. GitHub Actions run `31429588957` passed restore, build, the full test suite, living-document validation, generated-document validation, review-app smoke verification and Windows mixed-media verification.

Human verification is still required before completion: tune representative High score, High rank-gap and Medium thresholds against a private reviewed sample before auto-assignment is enabled for routine archive use. Automatic assignment remains disabled by default until that verification is deliberately completed.

## Completion notes

- Files changed: added schema-version-11 exact-model confidence-policy persistence; canonical automatic-assignment service; CLI policy binding and help; exact-model policy API and Web editor; High/Medium/Low queue classification, filtering, ordering and badges; integration coverage for policy isolation, score/gap boundaries, provenance, non-cascade behavior, threshold-history preservation and manual supersession; and architecture/build-context documentation.
- Trade-offs: confidence-policy versions are scoped independently to exact `(model ID, model hash)` revisions because score calibration is not portable across model revisions. High is deliberately conservative and requires both absolute score and rank gap; a missing rank-2 margin can never be High. Automatic decisions reuse the canonical suggestion-acceptance boundary and are applied only after one fixed scoring snapshot.
- Deferred work: private reviewed-sample threshold tuning and human acceptance remain before WI-0043 can be marked `completed` or routine automatic assignment can be enabled. WI-0045 separately owns triggering match regeneration from the Web application.
- Commands run: GitHub Actions build workflow run `31429588957` on `wi-0043` passed restore, Release build, full tests, living-document validation, generated-document check, review-app smoke verification and Windows PowerShell mixed-media verification.
