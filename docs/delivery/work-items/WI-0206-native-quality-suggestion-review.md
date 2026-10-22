---
id: WI-0206
title: Native operator-controlled technical-quality suggestions and Archive exclusion review
milestone: M40
status_source: PhotoIdentity.Docs
depends_on: [WI-0195]
related_adrs: [ADR-0008]
affected_modules: [PhotoIdentity.Api, PhotoIdentity.Web, PhotoIdentity.Core, PhotoIdentity.Integration.Tests, docs]
---

# WI-0206: Native operator-controlled technical-quality suggestions and Archive exclusion review

## Objective

Provide a native low-stakes **suggestions, not automatic decisions** workflow for photos that may have technical-quality problems or be unwanted. Operators can keep, dismiss or consider a suggestion; source-copy-local **Exclude from Photo Identity** remains an explicit, separately confirmed irreversible action and never alters the OneDrive original.

## Origin and canonical ownership

This accepted Archive cleanup product work was initially recorded only in **closed GitHub issue #629** and PRs named with the misleading issue-derived label **WI-0629**. The user did not authorize that separate planning/status system. The 2026-10-22 issue retirement audit migrated the requirements, decisions, private aggregate acceptance and PR history into this canonical **WI-0206** under M40. Historical issue/PR names are evidence, not repository-native IDs.

Research M37/WI-0195 established **GO for technical suggestions with a human deciding**, NOT GO for automatic photo exclusion, quality tiers or automatic redundancy suppression. The retained [M37 decision ledger](../milestones/M37-decision-ledger.md) remains authoritative on the research outcomes.

## Delivered contract

- Produce a bounded, deterministic, **explainable technical suggestion** shortlist from current verified local durable review proxies. Blur, clipping, exposure and related evidence is uncertain; a defect flag is not proof that a photo has no aesthetic or sentimental value.
- Display actual photo thumbnails in the native `/quality-suggestions` page; permit Keep, Dismiss, Consider exclusion and resumable navigation, with durable local recipe/use-case-specific feedback that persists across app restarts.
- Manual review first, **no automatic purge or omission** from Archive, Search, Smart/Creative Collections or slideshows. Per-context controls distinguish context preferences from actual collection membership/selection, which remains unchanged.
- An explicit existing source-copy-local **Exclude from Photo Identity** action is separate from a reversible collection hide. Confirmed exclusion removes the app's retained derivatives/identity evidence; original synced OneDrive source stays untouched. No click/keystroke outside an open confirmation dialog may trigger it.
- Full-viewport photo-centered keyboard workflow: **1 Keep / 2 Dismiss / 3 Consider**, left/right arrows navigate; **E** opens clear exclusion warning, **C or Enter** confirms only within that open dialog, **Escape** cancels. Confirm is focused natively; repeat/modified key events are ignored; no one-key purge.
- Narrow/resumable scans and batching must remain deterministic, not silently import the separate research pilot's private review JSON. Local reviewer preference JSON is **not** included in the normal PostgreSQL backup.
- The native reviewer does not yet provide a continuous per-photo quality rating, global near-duplicate truth or context-scoped reversible omission. Those are separate [WI-0205](WI-0205-continuous-photo-quality-rating.md) and [WI-0204](WI-0204-reversible-collection-scoped-alternative-omission.md) contracts.

## Implementation and maintainer evidence

- **PR #631:** local read-only pilot suggestion generator and reviewer. On **2026-10-21**, the maintainer inspected **129/129** technical suggestions: **72 Keep, 37 Dismiss, 20 Consider exclusion**; none triggered automatic exclusions. The operator judged most Consider cases genuinely unwanted and review reasonably convenient. This is a *suggestion-utility* decision, not a 98%-precision deletion claim.
- **PR #633:** first integrated native `/quality-suggestions` UI, local durable review choices, scoped context toggles, separate exclusion confirmation. Maintained app smoke tests passed for review, image loading, persisted feedback and unchanged existing features.
- **PR #640:** substantial keyboard-driven review and bounded scan/paging improvements. The operator's initial UX review identified scrolling, small image viewport and confirmation-placement problems.
- **PR #643:** full-viewport single-photo-centered layout with stable keyboard navigation and visible in-place exclusion dialog. Maintainer reported the viewport review was **much better**.
- **PR #644:** E opens dialog, C/Enter confirms only within it, Escape cancels; guarded repeat/modifier behavior and regression tests.
- **Final maintained acceptance (2026-10-21):** After PR #644 merged, maintainer reported **“Archive quality review works as expected and existing feature regressions are OK.”** Validated full-viewport keyboard flow, review actions, correct separate confirmation, no unintended original changes and unchanged existing workflows. This is the reported acceptance; a destructive confirmation was not requested merely for testing.

## Acceptance criteria

- [x] Automatically surface uncertain technical candidates without automatic inclusion/exclusion change.
- [x] Keep/Dismiss/Consider are clearly manual, durable, resumable and per-use-case separated.
- [x] Native photo-centered reviewer, keyboard navigation and explicit separately gated confirmation work on maintained app.
- [x] User's local acceptance found suggestion review useful, with **129/129** candidate decisions and no auto action.
- [x] Existing Archive, Search, Smart, Creative, slideshow and OneDrive-original safety boundaries are preserved; manual source exclusion remains explicit.
- [x] Limitation between reversible context-specific omission and irreversible source-copy purge is documented and visibly communicated.

## Verification and completion notes

- **Completed** as native Archive technical suggestion review, based on 2026-10-21 maintainer verification following merged PR #644.
- PR evidence: [#631](https://github.com/erikwasa/Photo-Identity-Indexer/pull/631), [#633](https://github.com/erikwasa/Photo-Identity-Indexer/pull/633), [#640](https://github.com/erikwasa/Photo-Identity-Indexer/pull/640), [#643](https://github.com/erikwasa/Photo-Identity-Indexer/pull/643), [#644](https://github.com/erikwasa/Photo-Identity-Indexer/pull/644).
- Operator runbook: [Native quality suggestion review](../../operations/native-quality-suggestion-review.md), which includes the interactive safety checks and detail of the accepted keyboard flow.
- Trade-offs: the proxy technical scan remains a **proposal generator**, not a calibrated photo-quality estimator. Original photos remain immutable. No unattended exclusions; separate opt-in filtering belongs to other items.
