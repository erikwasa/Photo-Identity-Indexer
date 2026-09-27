---
id: M33
title: Archive synchronization performance
status_source: ../status/milestones.yaml
depends_on: [M21, M24]
---

# M33: Archive synchronization performance

## Outcome

Included-folder synchronization completes in an acceptable, measured time at maintained-archive scale while retaining durable background operation, complete coverage reconciliation and immutable-revision safety.

Background execution remains important for reliability, but it is not treated as a substitute for reducing the underlying server-side work.

## Work items

- [WI-0166](../work-items/WI-0166-included-folder-sync-performance.md) - measure the current PostgreSQL-era synchronization cost and implement a safe, verified throughput correction.

## Delivery principles

- Measure the current production path before selecting an optimization.
- Compare no-change and small-change runs using the same representative coverage.
- Preserve full included-coverage discovery and reconciliation semantics from ADR-0007.
- Keep diagnostics aggregate and privacy-safe.
- Prefer a focused correction with correctness and performance guards over a broad archive-pipeline rewrite.
- Judge success by server-side elapsed time and retained correctness, not by whether the browser remains responsive.

## Exit criteria

- [x] Current archive-scale phase timings identify the dominant synchronization cost.
- [x] A maintainer-reviewed performance expectation is explicit.
- [x] Before/after evidence shows a material reduction in no-change and small-change synchronization time on the same coverage.
- [x] Correctness regressions cover changed, unchanged, reappearing, online-only, missing and moved assets plus parent-folder expansion.
- [x] Durable background behavior and conflict exclusion remain intact.
- [x] A non-private automated guard makes the corrected scaling behavior visible in normal development/CI.
- [x] Maintainer verification accepts synchronization latency on the maintained archive.

## Completion evidence — 2026-09-27

The maintainer accepted a target of at least 75% lower no-change wall clock and no more than 30 seconds for a stable maintained-archive synchronization. On the same PostgreSQL catalogue and coverage, `main` commit `6e3d52753231ed849a8321cdb80d506909f6fb50` completed the controlled no-change run in `192.264856 s`; corrected commit `92bab66ac741fb4baf5fa9c2ab830efa8cfcc559` completed the stable repeat in `7.421021 s`, a `96.14%` reduction. The corrected repeat hashed zero files and used one exclusion plus one persistence batch for each of 17 included folders.

A bounded one-photo change completed in `7.351566 s`, created exactly one revision and hashed exactly one `2,144,504`-byte file. The maintainer also confirmed that navigating away from Archive and returning did not interrupt the durable background synchronization. GitHub Actions build run `#2450` passed for the verified implementation commit.
