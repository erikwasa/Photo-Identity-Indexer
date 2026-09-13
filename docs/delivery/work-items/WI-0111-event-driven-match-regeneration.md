---
id: WI-0111
title: Coalesce identity changes into bounded follow-up regeneration
milestone: M25
status_source: ../status/work-items.yaml
depends_on: [WI-0045, WI-0103]
related_adrs: [ADR-0006]
affected_modules: [PhotoIdentity.Api, PhotoIdentity.Persistence.Postgres, PhotoIdentity.Web, PhotoIdentity.Integration.Tests]
---

# WI-0111: Coalesce identity changes into bounded follow-up regeneration

## Objective

Remove the operator's need to remember a separate `Regenerate matches` maintenance step after useful human identity changes by scheduling bounded follow-up regeneration from changed identity evidence.

## Why

A new manual assignment can immediately become valuable exemplar evidence, but the current fixed-snapshot design intentionally does not let it affect an already-running regeneration. Requiring a later manual regeneration leaves useful evidence idle and makes newly identified people difficult to propagate through a large backlog.

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

Automated integration coverage for scheduler/run-state semantics plus human Windows verification of a manual assignment followed by visible queued/completed matching without an explicit regenerate click.
