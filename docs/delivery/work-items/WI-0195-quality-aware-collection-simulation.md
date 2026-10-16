---
id: WI-0195
title: Simulate quality-aware visual redundancy during collection creation
milestone: M37
status_source: PhotoIdentity.Docs
depends_on: [WI-0193, WI-0194]
related_adrs: []
affected_modules: [PhotoIdentity.Core, tools, docs]
---

# WI-0195: Simulate quality-aware visual redundancy during collection creation

## Objective

Test whether independently evaluated absolute quality and visual redundancy actually improve real collection creation before Photo Identity persists either signal or changes production selection behavior.

## In scope

- Reuse only quality and redundancy policies accepted by WI-0193/WI-0194; do not retrain either signal using collection-level preferences.
- Run representative maintained-archive collection scenarios with four variants: current behavior, quality preference only, visual redundancy suppression only, and combined quality + redundancy.
- Include Smart-, Creative- and search-anchored candidate pools where those paths are available, with varied small/large pools and content types.
- Keep collection eligibility/relevance semantics unchanged in the simulator. Quality is a bounded preference after eligibility/relevance, not permission for unrelated high-quality photos to outrank relevant candidates.
- Simulate Very Low exclusion separately so its value/risk can be reviewed independently from quality preference.
- When candidates are judged redundant by the accepted similarity policy, prefer the independently calculated absolute quality rating as the primary representative selector, with deterministic fallback to existing collection score/order when quality is tied/unavailable.
- Prefer greedy collection-time suppression against already-kept representatives rather than creating canonical permanent similarity groups unless evaluation evidence specifically justifies grouping.
- Produce blinded comparison output so the maintainer can choose/prefer collection variants without knowing which policy generated them.
- Measure collection preference, repetitive-photo count, obviously poor-photo count, meaningful-photo loss, target-size attainment and how often the higher-quality candidate wins within redundant sets.
- Finish with explicit separate go/no-go decisions for quality preference, Very Low exclusion, redundancy suppression and the combined policy, plus a bounded production integration plan.

## Out of scope

- Persisting production quality scores or visual embeddings.
- Changing Search/Smart/Creative APIs/UI or saved collection semantics.
- Automatically deleting/hiding photos.
- Creating final production work inside this item.

## Acceptance criteria

- [ ] Representative real collection scenarios produce all four policy variants without mutating saved collections or slideshow snapshots.
- [ ] Blind maintainer review can compare variant quality and redundancy without model/policy labels leaking into the choice.
- [ ] Combined behavior reduces unwanted repetition and/or poor-photo selection without unacceptable loss of distinct meaningful photos or failure to reach requested collection size.
- [ ] Redundant-set representative choice uses absolute quality independently of the similarity model and records deterministic fallback behavior.
- [ ] Quality preference never overrides eligibility and remains bounded relative to search/collection relevance.
- [ ] Separate adoption decisions are recorded for quality preference, Very Low exclusion, visual redundancy and combined collection use.
- [ ] If production adoption is justified, the completion notes define the smallest follow-up persistence/API/selection/UI work rather than implementing it here.

## Verification requirements

Automated simulator determinism tests plus blinded maintainer review of representative real collection outputs and privacy-safe aggregate comparison evidence.

## Completion notes

- Files changed:
- Trade-offs:
- Deferred work:
- Commands run:
