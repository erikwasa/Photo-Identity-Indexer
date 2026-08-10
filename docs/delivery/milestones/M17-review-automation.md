---
id: M17
title: Identity review automation
status_source: ../status/milestones.yaml
depends_on: [M05, M06]
---

# M17: Identity review automation

## Outcome

The everyday identity-review loop scales to a growing permanent catalogue by combining configurable suggestion confidence groups, optional canonical automatic assignment, easy suggestion regeneration, favorite people and an explicit Unknown review state.

## Work items

- [WI-0043](../work-items/WI-0043-confidence-auto-assignment.md) — classify suggestions into configurable confidence groups and optionally turn High suggestions into canonical assignments
- [WI-0044](../work-items/WI-0044-favorite-people.md) — keep favorite people at the top of person selectors and maintenance lists
- [WI-0045](../work-items/WI-0045-web-match-regeneration.md) — regenerate identity suggestions from the browser with visible progress and stale-state feedback
- [WI-0047](../work-items/WI-0047-unknown-review-state.md) — record real-but-unidentified faces without creating a synthetic person identity

## Direction change

WI-0043 intentionally changes the earlier identity-matching policy. When automatic assignment is enabled, a qualifying High suggestion may create a canonical assignment without a human acceptance click. Such assignments are auditable, can become exemplars for later matching, and can be superseded by a later manual reassignment.

The automatic policy remains user-controlled and disabled by default. Threshold changes affect future matching runs; they do not silently rewrite earlier canonical decisions.

## Verification status

As of 2026-08-11, WI-0043, WI-0044 and WI-0047 are merged into the `m17` integration branch and are in review. Automated build, regression, documentation, review-smoke and Windows verification gates are green for their final work-item heads.

Milestone-wide human verification remains for the integrated desktop and narrow/mobile review flows. WI-0043 additionally requires representative private-sample tuning of the High score, rank-1/rank-2 gap and Medium thresholds before routine automatic assignment is enabled; automatic assignment remains disabled by default while that tuning is performed.

WI-0045 is the remaining implementation item. The draft `m17` to `main` milestone PR is the integration and verification boundary; it stays draft until WI-0045 is merged and the milestone-wide verification is complete.

## Exit criteria

- Suggestion groups and automatic-assignment policy are persisted and adjustable.
- Automatic assignments have explicit model, score and policy provenance.
- Manual correction cleanly supersedes an automatic identity and changes future exemplar evidence.
- Suggestion regeneration is available through the normal web workflow.
- Unknown is a distinct auditable review state that is excluded from identity evidence and person collections.
- Favorite people are consistently prioritized without influencing model scores.
