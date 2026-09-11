# Build context

This file is intentionally a short handoff for the next development or verification session. It should describe only the current focus, the next concrete step and the small set of documents needed to continue.

Formal work-item lifecycle status and evidence are resolved by PhotoIdentity.Docs from the current registry plus archived terminal history.

## Current focus

**M24 WI-0101 through WI-0105 and WI-0108 are completed. WI-0106 PostgreSQL operations and sustained archive catch-up is now the final substantive M24 work.**

The production launcher selects PostgreSQL as the single authoritative catalogue. `/health` was maintainer-verified with `status: ok`, `catalogueProvider: postgresql`, PostgreSQL `status: ready` and schema version 23. The preserved final SQLite backup remains a rollback/migration artifact and must not be treated as a second writable authority.

WI-0108 closed with measured evidence rather than speculative PostgreSQL/hash optimization. Direct one-photo and 11-photo server measurements were in the tens of milliseconds and showed no latency growth by slideshow position. Real-phone playback was already perceived as responsive.

The preventive prefetch correction merged through PR #304. Before the correction, one 11-photo phone run produced 39 `collection-viewer-preview-open` operations, 50 collection API requests and 22 original hash reads. After the correction, the same workflow produced 12 preview opens, 13 collection API requests and 6 original hash reads; 10 of 11 displayed images were explicit prefetch hits. Browser presentation averaged about 7.7 ms overall, with images 2–11 presenting in roughly 0.1–0.2 ms after becoming current. Resource Timing remained about 157 ms on average, confirming that the improvement came from effective bounded prefetch rather than hiding or weakening server verification.

No PostgreSQL query/index rewrite, image-quality change, immutable-verification weakening or prefetch-window increase was justified by WI-0108 evidence.

Consolidated real-phone M22 acceptance still has two separate functional gaps tracked by WI-0107: direct originating-gesture fullscreen launch and durable/revalidated prepared-original receipt state. Do not mix those functional corrections into M24 WI-0106.

A separate Collections / Library navigation gap remains outside this M24 thread. WI-0076 also remains separately recorded as in_progress and is not part of the M24 closeout.

## Next concrete step

For the M24 thread:

1. Start WI-0106 now that WI-0108 is complete.
2. Verify PostgreSQL-authoritative startup and restart behavior using the production launcher/configuration.
3. Exercise documented backup and restore against the real catalogue and confirm restored schema/provider health.
4. Resume sustained real-archive catch-up and observe throughput/backlog/health long enough to expose operational degradation rather than only a short smoke run.
5. After catch-up, run a small daily-style increment and verify incremental behavior without full regeneration.
6. Reconcile WI-0106 acceptance evidence and close M24 only after its operational exit criteria pass.

## Relevant files

- docs/delivery/work-items/WI-0106-postgresql-operations-and-archive-catchup.md
- docs/delivery/work-items/WI-0108-slideshow-performance.md
- docs/operations/slideshow-performance-diagnostics.md
- docs/operations/slideshow-browser-performance-diagnostics.md
- measure-slideshow-performance.ps1
- measure-slideshow-browser-performance.ps1
- verify-postgres.ps1
- Start-PhotoIdentity.ps1
- docs/delivery/status/work-items.yaml

## Repository validation

    ./build.ps1
    ./test.ps1
    dotnet run --project tools/PhotoIdentity.Docs -- validate
    dotnet run --project tools/PhotoIdentity.Docs -- generate --check
    ./verify-review.ps1 -Mode Smoke -Configuration Release

Live PostgreSQL verification:

    ./verify-postgres.ps1
