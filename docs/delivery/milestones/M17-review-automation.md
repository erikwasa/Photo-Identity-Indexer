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

M17 completed human verification on 2026-08-11 after all four work items were merged and the integrated milestone was merged to `main` through PR #122.

The maintainer reviewed the integrated workflow on a Windows laptop and Pixel, including High/Medium/Low confidence behavior and threshold tuning, optional automatic assignment and audit/correction behavior, favorite-person ordering and controls, Unknown versus false-detection behavior and later assignment, and browser-triggered regeneration with progress/stale-state feedback. The automated work-item and integrated repository gates were already green before the human pass.

All four M17 work items are therefore `completed` and human-verified.

## Minor post-verification UI follow-ups

Two non-blocking Faces-page layout observations remain and do not require separate work items:

- on the laptop layout, the `Unknown person`, `Assign` and `False detection` buttons do not fit comfortably inside the face card;
- on Pixel, the persistent menu consumes roughly half of the screen and remains fixed while the page scrolls, which is unacceptable for normal mobile use.

These are presentation fixes only and do not reopen any M17 acceptance criterion.

## Exit criteria

- [x] Suggestion groups and automatic-assignment policy are persisted and adjustable.
- [x] Automatic assignments have explicit model, score and policy provenance.
- [x] Manual correction cleanly supersedes an automatic identity and changes future exemplar evidence.
- [x] Suggestion regeneration is available through the normal web workflow.
- [x] Unknown is a distinct auditable review state that is excluded from identity evidence and person collections.
- [x] Favorite people are consistently prioritized without influencing model scores.
