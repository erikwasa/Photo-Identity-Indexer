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

- [ ] Current archive-scale phase timings identify the dominant synchronization cost.
- [ ] A maintainer-reviewed performance expectation is explicit.
- [ ] Before/after evidence shows a material reduction in no-change and small-change synchronization time on the same coverage.
- [ ] Correctness regressions cover changed, unchanged, reappearing, online-only, missing and moved assets plus parent-folder expansion.
- [ ] Durable background behavior and conflict exclusion remain intact.
- [ ] A non-private automated guard makes the corrected scaling behavior visible in normal development/CI.
- [ ] Maintainer verification accepts synchronization latency on the maintained archive.
