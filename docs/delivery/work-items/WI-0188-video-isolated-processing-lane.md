---
id: WI-0188
title: Introduce an opt-in, isolated video processing lane
milestone: M30
status_source: PhotoIdentity.Docs
depends_on: [WI-0150, WI-0151]
related_adrs: []
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Api, PhotoIdentity.Persistence.Postgres, docs]
---

# WI-0188: Introduce an opt-in, isolated video processing lane

## Objective

Schedule on-demand heavy video jobs without disrupting ordinary photo ingestion, recognition, captioning, search or slideshow use.

## In scope

- Default-off video enablement and separate visible queue for selected videos; existing photo workflows never silently queue full-video processing.
- Limits for concurrent video jobs, CPU/GPU/IO, memory, hydrated source bytes/time, derived frames and local disk; photo work has priority and can pause video processing.
- Cancellation, progress, restart/retry, bounded temp-file cleanup and resumable/idempotent work; avoid stale partial results.
- Revision/model/preset/policy version identifiers; invalidate evidence after source change or exclusion while keeping originals immutable.
- Explicit pause/stop, operator-visible failure diagnostics and aggregate resource metrics without logging personal media contents or paths.

## Out of scope

Proxy format implementation, caption/embedding inference, face assignment, cloud workers or a replacement photo-job framework.

## Acceptance criteria

- [ ] Video processing is off by default after upgrade and only runs when enabled and selected.
- [ ] Concurrent photo/video benchmark records photo job throughput and search/slideshow latency against a photo-only baseline with WI-0150-agreed bounds.
- [ ] Restart, cancellation, exclusions, online-only files and malformed video cannot leak partial derivatives or unlimited hydrated files.
- [ ] Work is independently pausable/observable and obeys configurable concurrency/resource limits.

## Verification requirements

Deterministic job lifecycle tests, plus real mixed-load CPU/GPU/IO and photo regression measurements.

## Completion notes

- Files changed:
- Trade-offs:
- Deferred work:
- Commands run:
