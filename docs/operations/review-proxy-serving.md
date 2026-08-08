# Review-proxy serving and explicit originals

This document describes the WI-0042 Slice 2 boundary: ordinary browsing stays proxy-backed, while an authoritative original is an explicit, revision-verified operation.

## Configuration

Proxy-backed collection browsing is enabled only when both settings are supplied to the API host:

```text
PhotoIdentity:ReviewProxyRoot
PhotoIdentity:ReviewProxyProfileId
```

`ReviewProxyRoot` is the local derivative root that contains the durable `review-proxies/...` paths recorded in the catalogue. It must be outside the authoritative OneDrive source root. `ReviewProxyProfileId` selects one exact registered review-proxy profile; the API does not infer a profile from dimensions or quality settings.

The implementation does not hard-code `jpeg-1600-q78` as a global default. The profile remains selected explicitly by configuration until the visual tuning decision is retained as human evidence.

## Collection resources

Collection manifest version 2 distinguishes three resources:

- `thumbnail` — the small fixed-size collection thumbnail. If a durable proxy exists for the configured profile, the thumbnail is rendered from that proxy. Otherwise an already-local authoritative original may be used as a compatibility fallback.
- `preview` — normal whole-photo browsing. If a durable proxy exists, the proxy bytes are served directly. Otherwise an already-local authoritative original may be used as a compatibility fallback.
- `original` — the explicitly named authoritative-original resource. It never falls back to a proxy. The legacy `content` route remains an alias with the same verified-original semantics.

The pre-v2 page-response `ContentUrl` compatibility property remains mapped to the thumbnail. New clients should use `PreviewUrl` for whole-photo browsing without touching the original.

## Explicit original workflow

A normal `GET /api/collections/photos/{revisionId}/original` never requests OneDrive hydration. It returns bytes only when the original is already local, is not being released, has the catalogued byte length and has the exact immutable SHA-256 recorded for that revision. Otherwise it returns no image bytes.

Use the explicit control endpoints when full resolution is needed:

```text
GET  /api/collections/photos/{revisionId}/original/status
POST /api/collections/photos/{revisionId}/original/hydrate
GET  /api/collections/photos/{revisionId}/original
POST /api/collections/photos/{revisionId}/original/release
```

Typical states are `online-only`, `downloading`, `ready`, `releasing`, `hash-mismatch`, `unavailable` and `error`. The status response also reports whether Photo Identity owns the hydration and whether hydrate, view or release is currently permitted.

Example operator flow from PowerShell after the local API is running:

```powershell
$Api = "http://localhost:5080"
$Revision = "<asset-revision-id>"

Invoke-RestMethod "$Api/api/collections/photos/$Revision/original/status"
Invoke-RestMethod -Method Post "$Api/api/collections/photos/$Revision/original/hydrate"

# Repeat status until state is ready before opening the original URL.
Invoke-RestMethod "$Api/api/collections/photos/$Revision/original/status"
Start-Process "$Api/api/collections/photos/$Revision/original"

# Release is accepted only when Photo Identity owns this hydration.
Invoke-RestMethod -Method Post "$Api/api/collections/photos/$Revision/original/release"
```

The Windows implementation uses the documented Files On-Demand pin attributes. An explicit hydration request pins an observed online-only item so the OneDrive sync client downloads it asynchronously. Release first clears the app-owned pin and then requests online-only state. Callers observe status until OneDrive completes the asynchronous transition.

## Ownership and release safety

Photo Identity creates a durable managed-hydration record only after Windows accepts an explicit hydration request for an item that was observed as online-only.

It deliberately does **not** claim ownership when an original was already local or was already pinned/downloading before the request. Those files may be viewed after revision verification, but `CanRelease` stays false and the release endpoint fails closed. This protects pre-existing local and user-pinned content across application restarts.

A release request is likewise durable. Ownership is cleared only after the file is observed online-only again. If a state-change command fails or the process stops mid-transition, ownership remains active so a later retry cannot accidentally forget which hydration Photo Identity initiated.

## Revision verification

Explicit originals are validated against the immutable catalogue revision before they are served:

1. the source path is resolved under the catalogued source root;
2. file symlinks are rejected while Cloud Files placeholders remain addressable;
3. the local byte length must equal the revision byte length; and
4. SHA-256 is computed from an open read handle and must equal the revision digest.

The verified file handle is rewound and served from that same handle, preventing the normal original endpoint from serving bytes that were never checked against the catalogue revision.

## Privacy boundary

Source roots and filenames remain server-side. Files On-Demand command failures are surfaced with path-free messages because Windows diagnostics may contain private source locations. Generated proxies, original pixels, filenames and identity data remain outside Git.

## Remaining WI-0042 work

Slice 2 now has the core proxy-backed browsing and explicit-original lifecycle. A later UI increment may expose these controls directly on collection cards without changing the API safety contract.

Slice 3 still adds configurable free-space reserve, managed-hydration byte budget, bounded concurrency, storage telemetry and policy-driven release/eviction. Slice 4 adds online-only source re-verification and end-to-end local acceptance before WI-0041 resumes real-archive verification.

## Measurement evidence

The private pilot scale-validation result for `jpeg-1600-q78` is recorded in `review-proxy-measurement.md`. Only aggregate values belong in Git; generated proxies, source filenames, pixels and identity data remain private.
