# Review-proxy serving and bounded originals

This document describes the WI-0042 collection-serving and bounded-original boundary: ordinary browsing stays proxy-backed, while authoritative originals are explicit, revision-verified and storage-governed operations.

## Review-proxy configuration

Proxy-backed collection browsing is enabled when both settings are supplied:

```text
PhotoIdentity:ReviewProxyRoot
PhotoIdentity:ReviewProxyProfileId
```

Automatic archive processing also needs the exact generation settings for that same profile:

```text
PhotoIdentity:ReviewProxyMaximumLongEdge
PhotoIdentity:ReviewProxyJpegQuality
```

`ReviewProxyRoot` must be outside the authoritative OneDrive source root. `ReviewProxyProfileId` is durable catalogue identity; changing encoder settings requires a different profile ID. Automatic processing does not infer settings from a name such as `jpeg-1600-q78`. If a profile ID is already registered, configured settings must match its durable canonical definition exactly.

The implementation therefore does not hard-code `jpeg-1600-q78` as a global default. The profile and exact settings remain explicit configuration until the visual tuning decision is retained as human evidence.

## Bounded hydration policy

New Photo-Identity-managed hydration is disabled until all three limits are explicitly configured:

```text
PhotoIdentity:ArchiveHydration:MinimumFreeSpaceReserveBytes
PhotoIdentity:ArchiveHydration:MaximumManagedHydrationBytes
PhotoIdentity:ArchiveHydration:MaximumConcurrentOperations
```

No production values are guessed in code. `MinimumFreeSpaceReserveBytes` may be zero but must not be negative. `MaximumManagedHydrationBytes` and `MaximumConcurrentOperations` must be positive. Invalid numeric configuration stops application startup; missing values leave managed hydration disabled with an actionable storage-status message.

Before an online-only original is pinned, Photo Identity reserves its complete catalogued logical byte length and checks both constraints:

1. after reserving the requested original, current volume free space must remain at or above `MinimumFreeSpaceReserveBytes`; and
2. managed local/downloading bytes plus the requested original must remain at or below `MaximumManagedHydrationBytes`.

Admission decisions are serialized so simultaneous requests cannot both observe the same spare budget and overcommit it. Managed originals currently downloading count against `MaximumConcurrentOperations`; managed revision-verification reads are bounded by the same configured operation limit.

If capacity is insufficient, Photo Identity requests release of already-local managed originals, oldest durable `last needed` first. Viewing a managed original or renewing its hydration updates that timestamp. Pre-existing local or user-pinned files have no managed lease and are never eviction candidates.

OneDrive release is asynchronous. Release-requested originals remain counted against managed reserved bytes until OneDrive is actually observed online-only. A request that triggered eviction therefore remains blocked and should be retried after storage status shows that release completed.

## Bounded archive analysis

`POST /api/archive/analysis/step` uses the bounded lifecycle rather than requiring the operator to hydrate online-only revisions manually.

Each call advances at most one governed processing attempt plus associated durable proxy/release work:

```text
finish any analyzed revision whose selected proxy is still missing
-> if needed, hydrate that exact original under the storage policy
-> generate and durably record the proxy
-> release it only if Photo Identity owns the hydration
-> otherwise select the next exact-profile analysis revision
-> hydrate an online-only revision under the storage policy when no local pending revision exists
-> once local availability is observed, record that observation and run one resumable analysis attempt
-> persist exact-profile analysis completion
-> generate the selected proxy
-> request release of managed hydration
```

Hydration observations made during this workflow update archive availability, so a successful `online-only -> downloading -> local` transition does not require a separate manual archive sync before analysis can proceed.

Analysis completion and proxy completion remain separate durable states. The orchestrator always checks for analyzed current revisions whose selected proxy is missing before starting more inference. If proxy generation fails after analysis committed, a later step retries the proxy/release phase without rerunning detector/embedder inference.

Only revisions that already have an immutable catalogue revision can enter this bounded analysis queue. Detecting and re-verifying source changes for previously verified online-only assets remains Slice 4 work.

## Storage telemetry

Privacy-safe aggregate storage state is available at:

```text
GET /api/archive/storage
```

The response separates:

- current logical source bytes in the configured archive;
- currently available free bytes on the archive volume;
- Photo-Identity-managed local, downloading, releasing and total reserved original bytes;
- active managed-original and in-progress hydration counts;
- configured minimum reserve, managed-byte budget and operation limit; and
- durable review-proxy bytes for the explicitly configured proxy profile.

Source paths and filenames are not returned. Release-requested bytes remain visible in releasing/reserved totals until online-only state is observed.

## Collection resources

Collection manifest version 2 distinguishes three resources:

- `thumbnail` — small fixed-size collection thumbnail, preferably rendered from the durable proxy;
- `preview` — normal whole-photo browsing, preferably the durable proxy bytes; and
- `original` — authoritative original only. It never falls back to a proxy. The legacy `content` route remains an alias with the same verified-original semantics.

The pre-v2 page-response `ContentUrl` compatibility property remains mapped to the thumbnail. New clients should use `PreviewUrl` for whole-photo browsing without touching the original.

## Explicit original workflow

A normal `GET /api/collections/photos/{revisionId}/original` never requests hydration. It returns bytes only when the original is already local, is not being released, has the catalogued byte length and has the exact immutable SHA-256 recorded for that revision.

Use the explicit control endpoints when full resolution is needed:

```text
GET  /api/collections/photos/{revisionId}/original/status
POST /api/collections/photos/{revisionId}/original/hydrate
GET  /api/collections/photos/{revisionId}/original
POST /api/collections/photos/{revisionId}/original/release
```

Typical states are `online-only`, `downloading`, `ready`, `releasing`, `hash-mismatch`, `unavailable` and `error`. The status response also reports whether Photo Identity owns the hydration and whether hydrate, view or release is currently permitted.

Example operator flow:

```powershell
$Api = "http://localhost:5080"
$Revision = "<asset-revision-id>"

Invoke-RestMethod "$Api/api/archive/storage"
Invoke-RestMethod "$Api/api/collections/photos/$Revision/original/status"
Invoke-RestMethod -Method Post "$Api/api/collections/photos/$Revision/original/hydrate"
Invoke-RestMethod "$Api/api/collections/photos/$Revision/original/status"
Start-Process "$Api/api/collections/photos/$Revision/original"
Invoke-RestMethod -Method Post "$Api/api/collections/photos/$Revision/original/release"
```

The Windows implementation uses Files On-Demand pin attributes. Explicit hydration pins an observed online-only item so the OneDrive sync client downloads it asynchronously. Release clears the app-owned pin and requests online-only state; callers observe status until the transition completes.

## Ownership and revision safety

Photo Identity creates durable managed-hydration ownership only after Windows accepts its explicit hydration request for an item observed online-only. It does **not** claim originals that were already local or already pinned/downloading. Those files may be viewed after revision verification, but automatic release fails closed.

Release ownership is cleared only after online-only state is observed again. If a state-change command fails or the process stops mid-transition, ownership remains active.

Explicit originals are validated against the immutable revision before serving: source path must remain under the catalogued root, file symlinks are rejected while Cloud Files placeholders remain addressable, byte length must match, and SHA-256 must equal the recorded revision digest. The verified handle is rewound and served from that same handle.

## Privacy boundary

Source roots and filenames remain server-side. Files On-Demand command failures are surfaced with path-free messages. Storage telemetry contains aggregate byte/state counts only. Generated proxies, original pixels, filenames and identity data remain outside Git.

## Remaining WI-0042 work

Slice 2 real Windows/OneDrive acceptance remains pending until the maintainer can perform it; later automated work does not make that gate pass implicitly.

Slice 3 implements bounded admission, managed-byte accounting, concurrency limits, LRU managed release, storage telemetry and bounded analysis/proxy orchestration. Automated working-set coverage proves cumulative logical bytes can exceed the configured managed budget while peak managed reservation stays inside it.

Slice 4 adds online-only source-change/re-verification state plus real-machine/end-to-end acceptance and policy tuning before WI-0041 resumes real-archive verification.

## Measurement evidence

The private pilot scale-validation result for `jpeg-1600-q78` is recorded in `review-proxy-measurement.md`. Only aggregate values belong in Git; generated proxies, source filenames, pixels and identity data remain private.
