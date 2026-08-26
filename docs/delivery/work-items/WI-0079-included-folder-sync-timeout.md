---
id: WI-0079
title: Investigate included-folder synchronization timeout and scaling
milestone: M21
status_source: ../status/work-items.yaml
depends_on: [WI-0041]
related_adrs: [ADR-0007]
affected_modules: [PhotoIdentity.Api, PhotoIdentity.Web, PhotoIdentity.Persistence.Sqlite, PhotoIdentity.Source.Local]
---

# WI-0079: Investigate included-folder synchronization timeout and scaling

## Priority

**Critical.** This is the first issue to investigate after the 2026-08-26 consolidated maintainer verification.

## Problem statement

The maintainer reports that **Sync included folders** is taking increasingly long to complete as the real catalogue/archive grows. Recent attempts have failed from the browser with:

```text
net_http_request_timedout, 100
```

The observed trend is important: this is not only a single transient timeout. Repeated synchronization attempts have become progressively slower, and the current operator workflow can now exceed the browser/request lifetime.

The synchronization operation is expected to be safe to repeat, discover new files in previously included folders, avoid reprocessing unchanged immutable revisions unnecessarily, and remain usable as archive coverage grows. The current behavior threatens that operating model.

## Investigation objective

Establish where synchronization time is being spent, whether work grows linearly or worse with catalogue size, and whether a long-running catalogue synchronization is incorrectly coupled to one browser HTTP request.

Do **not** choose or implement a fix until the investigation has produced evidence for the dominant cost and the maintainer has reviewed the solution options.

## Investigation questions

- What exact endpoint/application path is invoked by **Sync included folders**, and which work is performed synchronously before the request returns?
- How does runtime change with number of included roots, folders, files, catalogue revisions and previously analyzed files?
- Is every synchronization rescanning all included folders and/or revalidating unchanged files?
- Are filesystem enumeration, SHA-256 verification, SQLite queries/writes, OneDrive state checks, metadata inspection, archive advancement or UI result construction responsible for material portions of the delay?
- Are there repeated per-file database queries or other N+1 behavior whose cost grows with catalogue size?
- Does the browser timeout merely hide a server operation that continues safely, or does request cancellation abort/partially cancel synchronization?
- Is durable/background orchestration more appropriate for this operation, similar to the browser-lifetime lesson learned for GeoNames, or can the synchronous path remain bounded after eliminating avoidable work?
- What progress/status information is needed so the operator can distinguish scanning, reconciliation, useful changes and completion?

## Evidence to capture

Use the maintainer catalogue where safe and add instrumentation before optimization if existing diagnostics are insufficient. Record at least:

- included-root count;
- directories/files enumerated;
- candidate/new/changed/unchanged revision counts;
- elapsed time per major synchronization phase;
- database query/write counts or representative timings where practical;
- hashing/verification counts and bytes read where practical;
- OneDrive/local-state checks;
- request duration and whether server work survives browser cancellation;
- repeat-run timings with no filesystem changes;
- timing after adding a small new folder to an otherwise unchanged included hierarchy.

## Safety constraints

- Preserve immutable revision/hash guarantees.
- Do not mark an unchanged file as analyzed merely to make synchronization faster.
- Do not weaken OneDrive/locality checks or cause unbounded hydration.
- Repeated synchronization must remain idempotent and must not create duplicate catalogue revisions/jobs.
- Do not hide a long operation solely by increasing the browser timeout without understanding the scaling problem.

## Investigation acceptance criteria

- [ ] The timeout is reproduced or otherwise characterized against a realistically sized included-folder set.
- [ ] End-to-end and per-phase timing evidence identifies the dominant cost(s).
- [ ] Repeat-run behavior with no changes is measured separately from a run with a small number of new files.
- [ ] The effect of browser/request cancellation is known: server work either continues durably or cancellation semantics are explicitly documented.
- [ ] Catalogue-size scaling is characterized well enough to distinguish expected linear scanning from avoidable super-linear/repeated work.
- [ ] At least two viable correction strategies are compared with safety, complexity and operator-experience tradeoffs.
- [ ] The maintainer selects the implementation direction before product-code changes begin.
- [ ] The eventual implementation plan includes regression/performance evidence that prevents synchronization time from silently degrading again.

## Source finding

During the final consolidated M19/M20 maintainer verification on 2026-08-26, all planned acceptance checks passed, but the maintainer separately reported increasingly slow **Sync included folders** requests and recent failures with `net_http_request_timedout, 100`. This issue is deliberately separated from already-passed M19/M20 acceptance and from WI-0076 archive analysis model-session throughput.
