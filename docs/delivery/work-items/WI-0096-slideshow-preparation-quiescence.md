---
id: WI-0096
title: Wait for slideshow preparation cancellation to quiesce
milestone: M22
status_source: ../status/work-items.yaml
depends_on: [WI-0093]
related_adrs: []
affected_modules: [PhotoIdentity.Api, testing]
---

# WI-0096: Wait for slideshow preparation cancellation to quiesce

## Objective

Stabilize the slideshow-original preparation lifecycle exposed by WI-0093 so ending a session does not return while its background preparation task can still hold catalogue resources.

The issue was reproduced twice while validating WI-0095 on workflow #1360:

    SlideshowOriginalPreparationServiceTests.No_progress_warning_and_retry_reuse_the_same_immutable_session
    System.IO.IOException: catalogue.db is being used by another process

The test assertions completed successfully. Failure occurred during temporary-directory deletion after `Preparation.End(...)`, showing that session cancellation was signalled but the background preparation task had not yet quiesced.

## Contract

- ending a preparation session removes it from active lookup and releases slideshow lease protection;
- cancellation must be signalled to the background preparation loop;
- the asynchronous end operation must not complete until the session's existing background RunTask has settled;
- no unconditional sleeps/retries should be added to test cleanup;
- the HTTP DELETE endpoint should await the same service lifecycle operation before returning 204;
- normal completed/failed preparation sessions should also end safely;
- no source paths or additional preparation state should be exposed.

## Acceptance criteria

- [ ] Service exposes an asynchronous end operation that waits for the session background task to finish after cancellation.
- [ ] DELETE /api/slideshows/original-preparation/{sessionId} awaits quiescence before returning.
- [ ] Existing preparation tests use the asynchronous end contract.
- [ ] The previously reproducing no-progress/retry test can immediately remove its SQLite temp directory after end.
- [ ] No test-level retry or arbitrary delay is introduced.
- [ ] Existing lease-release and session-removal semantics remain intact.
- [ ] Required CI passes.

## Non-goals

- Changing preparation progress/no-progress semantics.
- Changing OneDrive hydration concurrency.
- Altering immutable snapshot membership.
