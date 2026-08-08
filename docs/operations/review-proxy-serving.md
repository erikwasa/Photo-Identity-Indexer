# Review-proxy serving

This document describes the WI-0042 Slice 2 collection-serving boundary. It is intentionally narrower than the complete bounded-hydration workflow.

## Configuration

Proxy-backed collection browsing is enabled only when both settings are supplied to the API host:

```text
PhotoIdentity:ReviewProxyRoot
PhotoIdentity:ReviewProxyProfileId
```

`ReviewProxyRoot` is the local derivative root that contains the durable `review-proxies/...` paths recorded in the catalogue. It must be outside the authoritative OneDrive source root. `ReviewProxyProfileId` selects one exact registered review-proxy profile; the API does not infer a profile from dimensions or quality settings.

The current implementation does not hard-code `jpeg-1600-q78` as a global default. The profile is selected explicitly by configuration.

## Collection resources

Collection manifest version 2 distinguishes three resources:

- `thumbnail` — the small fixed-size collection thumbnail. If a durable proxy exists for the configured profile, the thumbnail is rendered from that proxy. Otherwise an already-local authoritative original may be used as a compatibility fallback.
- `preview` — the normal whole-photo browsing resource. If a durable proxy exists, the proxy bytes are served directly. Otherwise an already-local authoritative original may be used as a compatibility fallback.
- `original` — the explicitly named authoritative-original resource. It never falls back to a proxy. The legacy `content` route remains an alias for compatibility and has the same original-only semantics.

A normal collection page uses the preview resource, so an online-only authoritative original with a valid permanent proxy can remain unhydrated during ordinary browsing.

## Safety boundary

Proxy resolution uses only durable proxy metadata and a configured derivative root. The resolved path must remain under that root, the file must exist, its encoded length must match the durable record and the file itself must not be a reparse point.

Normal proxy browsing does not open the authoritative original. If no proxy exists, the compatibility fallback uses the existing local-original resolver, which does not treat an online-only OneDrive placeholder as usable photo content.

## Current limitation

This increment establishes proxy-backed browsing and explicit preview/original API semantics only. It does not yet complete explicit full-resolution original access from an online-only state.

Before WI-0042 can claim that acceptance criterion, a later Slice 2 increment must:

1. explicitly request OneDrive hydration rather than relying on an image GET to recall data;
2. expose hydration/progress state to the operator;
3. validate the hydrated authoritative content against the immutable revision before serving it as the original;
4. distinguish content that Photo Identity hydrated from content that was already local or user-pinned; and
5. provide explicit or policy-driven release only for Photo-Identity-owned hydration.

Capacity limits, managed-hydration byte accounting, bounded concurrency and eviction remain Slice 3 concerns.

## Measurement evidence

The private pilot scale-validation result for `jpeg-1600-q78` is recorded in `review-proxy-measurement.md`. Only aggregate values belong in Git; generated proxies, source filenames, pixels and identity data remain private.
