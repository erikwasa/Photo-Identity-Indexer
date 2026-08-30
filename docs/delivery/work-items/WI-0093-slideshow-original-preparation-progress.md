---
id: WI-0093
title: Make slideshow original preparation observable and recoverable
milestone: M22
status_source: ../status/work-items.yaml
depends_on: [WI-0042, WI-0086]
related_adrs: []
affected_modules: [PhotoIdentity.Api, PhotoIdentity.Web, PhotoIdentity.Source.OneDriveSync, documentation]
---

# WI-0093: Make slideshow original preparation observable and recoverable

## Objective

Correct the opaque best-quality preparation behavior observed during real-phone M22 review, where a 56-photo collection remained at 1 / 56 photos ready for a long period with no indication whether OneDrive downloads had started, were queued or were stalled.

The current implementation already limits managed hydration concurrency and uses the documented Windows Files On-Demand pin request. The missing product contract is useful progress/state reporting and a recovery path when OneDrive makes no observable progress.

## Investigation finding

The current UI reports only originals that have become fully local and revision-verified.

With the default managed hydration concurrency of 2, a valid 56-photo state can therefore be:

    1 ready
    2 downloading
    53 queued

while the screen still displays only 1 / 56.

Windows documents attrib +p as the scriptable Always available/Pinned request and states that pinning an online-only file makes the sync app download its contents. The existing adapter uses that request. This work item should first make the real OneDrive/preparation state observable rather than replacing the hydration safety model speculatively.

## Progress contract

Extend path-free preparation status to expose at minimum:

- ready/verified count;
- actively downloading count;
- queued online-only count;
- waiting-for-release count;
- verifying count if separately observable;
- total count;
- last observed progress time or elapsed no-progress duration;
- concise current phase/message.

Do not return source paths or filenames.

The UI should render a concise form such as:

    Preparing originals
    1 / 56 ready
    2 downloading · 53 queued
    Waiting for OneDrive

Progress means an aggregate state/count transition, a newly ready revision, a newly accepted hydration request, or a release completing.

## No-progress recovery

Preparation must not remain indefinitely opaque.

If the aggregate state makes no progress for a conservative centralized/testable threshold:

- keep the session safe and paused;
- show a parent-visible **OneDrive has not made progress** warning;
- offer **Retry preparation** and **Cancel preparation**;
- Retry should restart/reconcile preparation for the same immutable snapshot rather than silently taking a new collection snapshot;
- already-local or already-managed content must be reused under the existing ownership rules;
- Retry must not claim pre-existing local/user-pinned files;
- do not weaken full-snapshot storage preflight.

A warning is preferable to aggressive automatic failure because a legitimate large OneDrive download may take a long time.

## Diagnostics

Add privacy-safe diagnostics for:

- aggregate state counts;
- managed hydration requests for the preparation;
- time since last progress;
- whether preparation is waiting for OneDrive download, managed release or verification.

## Acceptance criteria

- [ ] Status distinguishes ready, downloading, queued and release-wait states.
- [ ] One local original plus multiple online-only originals visibly shows active/queued hydration work.
- [ ] Existing maximum-concurrent-hydration policy remains authoritative.
- [ ] No online-only file is opened before full-snapshot preflight admission.
- [ ] A stuck fake OneDrive state produces a parent-visible no-progress warning rather than an indefinitely opaque counter.
- [ ] Retry operates on the same immutable revision set and preserves ownership/release safety.
- [ ] Cancel retains existing safe cancellation/lease-release behavior.
- [ ] Capacity failure remains distinct from OneDrive no-progress.
- [ ] Progress/error responses and logs remain path-free.
- [ ] Automated tests cover progress buckets, no-progress detection, retry and cancellation.
- [ ] Real-phone verification confirms a mixed local/online collection either reaches ready with understandable status or surfaces an actionable no-progress state.

## Non-goals

- Replacing the bounded hydration policy.
- Increasing concurrency merely to make the counter move faster.
- Claiming or releasing pre-existing local/user-pinned originals.
