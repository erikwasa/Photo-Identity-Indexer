---
id: WI-0136
title: Make slideshow startup and original preparation exception-driven
milestone: M27
status_source: ../status/work-items.yaml
depends_on: [WI-0094, WI-0107, WI-0108]
related_adrs: []
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Persistence.Postgres, PhotoIdentity.Web, PhotoIdentity.Integration.Tests, docs]
---

# WI-0136: Make slideshow startup and original preparation exception-driven

## Objective

Keep routine snapshot/original preparation out of the normal viewing flow so starting a slideshow usually means one action followed by playback, while preserving explicit recovery when preparation genuinely needs attention.

## Why

The current library exposes Prepare originals and detailed preparation state because M22 needed observable, safe hydration behavior. That remains important operationally, but it makes ordinary consumption feel technical. Persisted preferences should decide what preparation is desired; the UI should interrupt only for meaningful failures, capacity limits or no-progress states.

## In scope

- When the persisted Prepare originals preference is off, start ordinary playback without exposing preparation controls.
- When it is on, perform the existing preflight/preparation contract automatically as part of slideshow start.
- Keep successful preparation progress visually minimal/ambient rather than operator-like where practical.
- Surface actionable recovery only for no-progress, insufficient capacity, failed verification or explicit user cancellation.
- Preserve the ability for a parent/operator to pre-prepare originals outside ordinary child consumption, but move that capability out of the primary collection-card action surface.
- Preserve storage ownership, bounded hydration and prepared-original verification semantics from M22.
- Ensure startup remains honest: do not silently claim best-quality readiness when only proxy/available playback is possible.

## Out of scope

- Removing the Prepare originals preference.
- Weakening storage/free-space limits.
- Automatically downloading originals when the persisted preference says not to.

## Acceptance criteria

- [ ] Normal library cards do not expose routine Prepare/Retry/Cancel controls when no action is required.
- [ ] Starting with Prepare originals enabled automatically follows the existing safe preparation lifecycle.
- [ ] Successful preparation proceeds into playback without an additional confirmation click.
- [ ] Capacity/no-progress/verification failures remain explicit and provide parent-safe recovery choices.
- [ ] Standalone pre-preparation remains available in a secondary parent/operator path.
- [ ] Storage ownership and eviction protections remain unchanged.
- [ ] Tests cover prepare-off happy path, prepare-on success, no-progress, capacity failure, verification failure and continue-with-available recovery.

## Verification requirements

Run representative proxy-only and prepared-original slideshows on the supported phone path and verify the happy path contains no unnecessary decision points.

## Completion notes

- Files changed: `Slideshows.razor`, `Slideshow.razor`, `SlideshowPreparationExperience`, focused integration tests and delivery handoff/status files.
- Trade-offs: the existing M22 snapshot, storage-admission, hydration, lease and immutable-verification lifecycle is retained rather than replaced. Routine preparation controls move out of each gallery card into a collapsed parent/operator tool surface; active preparation remains visible only as ambient progress or an exception cue. No-progress recovery now also permits explicit continue-with-available playback.
- Deferred work: representative proxy-only and prepared-original phone-path verification remains pending in the separate verification thread; WI-0137 still owns the broader real-device slideshow polish pass.
- Commands run: implementation prepared for the normal build/integration/docs CI gates; manual phone-path acceptance remains pending.
