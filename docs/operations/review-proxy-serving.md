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

Before an online-only source or revision is pinned, Photo Identity reserves its complete observed/catalogued logical byte length and checks both constraints:

1. after reserving the requested item, current volume free space must remain at or above `MinimumFreeSpaceReserveBytes`; and
2. managed local/downloading bytes plus the requested item must remain at or below `MaximumManagedHydrationBytes`.

Admission decisions are serialized so simultaneous requests cannot both observe the same spare budget and overcommit it. Managed downloads count against `MaximumConcurrentOperations`; managed content-verification reads are bounded by the same configured operation limit.

If capacity is insufficient, Photo Identity requests release of already-local managed content, oldest durable `last needed` first. Viewing or reusing managed content updates that timestamp. Pre-existing local or user-pinned files have no managed lease and are never eviction candidates.

OneDrive release is asynchronous. Release-requested content remains counted against managed reserved bytes until OneDrive is actually observed online-only. A request that triggered eviction therefore remains blocked and should be retried after storage status shows that release completed.

## Source observations and authoritative re-verification

Archive synchronization keeps lightweight observations for each current source item: logical size, last-write timestamp and media type. Reading those properties does not require opening a Files On-Demand placeholder.

These observations are deliberately **not** content identity. Their purpose is to decide whether authoritative verification is required:

- `verified` — the latest lightweight observation matches the retained baseline associated with a SHA-256-verified revision;
- `needs-source-verification` — a previously verified source has diverged from its retained lightweight baseline, or an exact byte/hash check later found that local content no longer matches the expected revision; and
- `unverified` — the source has never had authoritative bytes hashed, which is common for a first-time online-only item.

Once `needs-source-verification` has been observed, matching metadata on a later placeholder scan does not clear it. Only a successful local SHA-256 verification clears that state.

A first-time online-only source has no revision ID yet, so source verification temporarily owns hydration at the asset/source level. That lease enters the same free-space, managed-byte, concurrency and LRU policy as revision-level hydration. After the file becomes local, Photo Identity hashes authoritative bytes, establishes or reselects the immutable content revision, and atomically transfers managed ownership to that revision. Downstream analysis/proxy generation can therefore continue without losing ownership or hydrating the same file a second time.

If re-verification establishes a different current revision while an older archive-analysis run is still active, that run is cancelled before old queued work resumes. Detector/embedder analysis is never scheduled for a source while its verification state is `needs-source-verification` or `unverified`.

The Archive status and item APIs expose source-verification state separately from OneDrive availability. An item may therefore be `online-only` and `verified`, or `online-only` and `needs-source-verification`; these are intentionally different dimensions.

## Bounded archive analysis

`POST /api/archive/analysis/step` and the Archive page's **Advance archive** action use one bounded lifecycle rather than requiring the operator to hydrate online-only content manually.

Each call advances at most one governed source-verification, processing or post-analysis step:

```text
reconcile one unverified / metadata-divergent source first
-> if needed, request bounded source hydration without opening the placeholder
-> once local, SHA-256 hash authoritative bytes and establish/reselect the revision
-> transfer managed ownership to that revision
-> cancel stale active analysis if the current revision changed
-> finish any analyzed revision whose selected proxy is still missing
-> if needed, hydrate that exact revision under the storage policy
-> generate and durably record the proxy
-> release it only if Photo Identity owns the hydration
-> otherwise select the next verified exact-profile analysis revision
-> hydrate an online-only verified revision under the storage policy when needed
-> once local availability is observed, run one resumable analysis attempt
-> persist exact-profile analysis completion
-> generate the selected proxy
-> request release of managed hydration
```

Hydration observations made during this workflow update archive availability, so a successful `online-only -> downloading -> local` transition does not require a separate manual archive sync before analysis can proceed.

Analysis completion and proxy completion remain separate durable states. The orchestrator always checks for analyzed current revisions whose selected proxy is missing before starting more inference. If proxy generation fails after analysis committed, a later **Advance archive** call retries the proxy/release phase without rerunning detector/embedder inference.

## Storage telemetry

Privacy-safe aggregate storage state is available at:

```text
GET /api/archive/storage
```

The response separates:

- current logical source bytes in the configured archive;
- currently available free bytes on the archive volume;
- Photo-Identity-managed local, downloading, releasing and total reserved bytes across source-verification and revision-level hydration;
- active managed-content and in-progress hydration counts;
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

## Human acceptance remains pending

Slices 1–4 now have automated implementation coverage, but WI-0042 is not complete until the maintainer performs the combined real Windows/OneDrive review. Use `bounded-archive-acceptance.md` for that gate.

That review includes the previously deferred Slice 2 explicit-original lifecycle, Slice 3 capacity/release behavior, Slice 4 source re-verification, and the still-missing human evidence for the 100-image multi-candidate proxy visual tuning decision. Automated tests and the 556-image aggregate scale measurement do not substitute for those human checks.

WI-0041 must remain blocked until the combined acceptance passes and the production hydration policy/profile values are deliberately selected.

## Measurement evidence

The private pilot scale-validation result for `jpeg-1600-q78` is recorded in `review-proxy-measurement.md`. Only aggregate values belong in Git; generated proxies, source filenames, pixels and identity data remain private.
