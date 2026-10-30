# WI-0189 — local playable proxy retention checkpoint

This checkpoint adds a **pure, fail-closed quota planner**, not an eviction
worker or a new media-serving API. It is intentionally independent of the
production WI-0188 processing lane until catalogue-backed ownership and
lease fencing are connected and reviewed.

The planner takes only opaque derivative IDs, sizes, last-access timestamps
and pinned flags. It rejects invalid/duplicate or over-budget inventory,
rejects proxies over the per-file limit, prefers oldest unpinned records and
returns a **complete suggested eviction list** only when sufficient capacity
can be reclaimed. Nothing is deleted, written or hydrated by this code.

Before turning on automatic retention, the remaining implementation must:
- Obtain a consistent catalogue snapshot of *only* application-owned,
  revision/preset-matched playable-proxy files.
- Make sure no file is in use by playback, a writer, or a running lease.
- Recheck path canonicalization, non-reparse-point cache ownership, exclusion
  and current source revision immediately before any file operation.
- Delete only verified application-owned derivatives, with crash-safe
  reconciliation and a bounded operator-visible report.
- Wire configurable cache/per-file limits and validated output profile through
  processing admission and publication; test real MOV/MP4 orientation, silent
  video, audio, seeking, quota and preservation of originals.

Run the isolated deterministic policy tests on Windows:

```powershell
dotnet test tests/PhotoIdentity.Integration.Tests/PhotoIdentity.Integration.Tests.csproj --filter FullyQualifiedName~VideoProxyRetentionPolicyTests
```

**Not yet claimed:** automatic quota enforcement, eviction, revised presets,
real-video quality/size acceptance, or completion of WI-0189.
