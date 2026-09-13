---
id: WI-0111
title: Coalesce identity changes into bounded follow-up regeneration
milestone: M25
status_source: ../status/work-items.yaml
depends_on: [WI-0045, WI-0103]
related_adrs: [ADR-0006]
affected_modules: [PhotoIdentity.Api, PhotoIdentity.Web, PhotoIdentity.Integration.Tests]
---

# WI-0111: Coalesce identity changes into bounded follow-up regeneration

## Objective

Remove the operator's need to remember a separate `Regenerate matches` maintenance step after useful human identity changes by scheduling bounded follow-up regeneration from changed identity evidence.

## Why

A new manual assignment can immediately become valuable exemplar evidence, but the current fixed-snapshot design intentionally does not let it affect an already-running regeneration. Requiring a later manual regeneration leaves useful evidence idle and makes newly identified people difficult to propagate through a large backlog.

## Implementation status

PR #322 is merged and implements the bounded follow-up scheduler around the existing regeneration controller rather than adding another matching engine or queue table. CI run #1738 passed.

The durable queued condition is an exact-model identity-evidence version newer than the latest run's expected evidence. Completed automatic assignments are folded into that expected post-run version, so a run does not recursively schedule another run merely because it created automatic assignments. A 30-second default process-local debounce coalesces bursts; after restart the durable evidence mismatch is rediscovered even though the debounce clock restarts.

Automatic follow-up defaults on and can be disabled with `PhotoIdentity__IdentityMatchRegeneration__AutomaticFollowUpEnabled=false`. The coalescing interval can be configured with `PhotoIdentity__IdentityMatchRegeneration__AutomaticFollowUpDelayMilliseconds` from 0 through 600000 milliseconds. Explicit regeneration remains available when automatic follow-up is disabled.

The existing regeneration API/page now distinguishes automatic follow-up state as `queued`, `running`, `current` or `disabled` while preserving the existing stale flag for policy/evidence validity. A queued state keeps the page polling until the bounded run starts and completes.

Maintainer acceptance is deliberately deferred. The maintainer requested one combined Windows acceptance session for WI-0111, WI-0112 and WI-0113. Until that pass is recorded, WI-0111 remains `in_review` and must not be marked completed.

## In scope

- Detect identity-evidence changes that can make a later regeneration useful, including manual assignments, accepted suggestions, person merges and newly available exact-model embeddings where appropriate.
- Coalesce bursts of changes so a series of review actions does not create one full regeneration per click.
- Start a later durable regeneration only when no equivalent current run already covers the changed evidence/model revision.
- Preserve exact-model policy/evidence-version snapshotting, stale-run detection and same-run non-cascade semantics.
- Keep work bounded and observable; repeated runs must stop when no newer evidence requires another pass.
- Make automatic follow-up configurable/disableable for operators who prefer manual control.
- Expose concise state such as `matching update queued/running/current` without interrupting normal review.
- Add recovery tests proving restart does not lose or multiply queued follow-up work.

## Out of scope

- Lowering confidence thresholds.
- Letting a newly automatic assignment feed back into the same regeneration run.
- Unbounded recursive regeneration loops.
- Automatically changing canonical Unknown faces.
- Using clustering evidence; that belongs to later M25 work items.

## Acceptance criteria

- [ ] A qualifying human identity change can queue a later exact-model regeneration without a separate operator action.
- [ ] Multiple qualifying changes in a short review session are coalesced rather than producing redundant whole-catalogue runs.
- [ ] An already-running regeneration becomes stale or completes according to existing evidence-version rules; the scheduler does not silently mutate its exemplar snapshot.
- [ ] Newly produced automatic assignments never become exemplars inside the same run that produced them.
- [ ] Follow-up scheduling converges: when no newer qualifying evidence exists, no additional run is queued.
- [ ] Application restart preserves durable regeneration state without duplicating completed work.
- [ ] Operators can disable automatic follow-up and continue using explicit regeneration.
- [ ] Review remains responsive while follow-up matching runs.
- [ ] Automated coverage protects coalescing, restart, stale-evidence and no-same-run-cascade invariants.

## Verification requirements

Automated integration coverage for scheduler/run-state semantics plus human Windows verification of a manual assignment followed by visible queued/completed matching without an explicit regenerate click, review responsiveness while the run is active, convergence without a recursive second run, and disabled-mode explicit regeneration. Perform that human pass in the combined WI-0111/WI-0112/WI-0113 maintainer session before WI-0111 is completed.
