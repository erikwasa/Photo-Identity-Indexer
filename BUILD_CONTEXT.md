# Build context

Formal lifecycle status is resolved through `PhotoIdentity.Docs`; read the target shard and linked document before continuing.

## Current focus

WI-0182 / issue #490: PR #509 implements the evaluation-only technical-quality harness over durable review proxies. It adds versioned raw sharpness/exposure/clipping/contrast/resolution evidence, q78 profile-gated candidate reasons, deterministic PostgreSQL proxy sampling, private JSON review export, timing/storage measurements and synthetic analyzer coverage. No collection semantics or catalogue quality persistence are introduced. Keep WI-0182 in review until representative private-archive review/runtime evidence is recorded.

WI-0186 / issue #512: PR #515 scopes Visual/Caption/Combined search through an optional saved Smart Collection without weakening exact Smart Collection semantics. The server resolves one eligibility set, applies it before semantic top-k and inside caption SQL, and keeps explicit saved search-result collections immutable. PR #517 corrected the large-scope PostgreSQL timeout. The 2026-10-13 maintainer retest succeeded for Fideli (5,638 eligible) in all three modes and Mor och Dotter (1,057 eligible); provenance badges behave as expected. Keep WI-0186 in review until independent exact-membership and saved-list restart verification is recorded.

WI-0187 / issue #513: PR #516 lets Creative Collections use either the existing exact Smart Collection anchor or a versioned ranked Search anchor with Visual/Caption/Combined mode, optional Smart scope and bounded top-N admission. Search anchor materialization reuses WI-0186 retrieval, then enters the existing one-level moment/context and deterministic Creative selection pipeline. The maintainer now reports the search-anchor workflow works as expected. Keep WI-0187 in review pending the specific two-recipe, legacy compatibility and snapshot-immutability evidence in its acceptance criteria.

M30: WI-0150 completed and ADR-0012 accepted. WI-0151 has now started with an isolated opt-in pilot (MOV/MP4 source enumeration and FFprobe/FFmpeg still poster generation), documented in docs/operations/video-metadata-pilot.md. This checkpoint does **not** yet persist video assets/revisions/metadata/posters or change gallery/other photo operations. Continue WI-0151 by implementing PostgreSQL media-kind/revision-bound storage, privacy/exclusion checks and API browsing, then local HDR/rotation verification before marking complete.

## Maintainer acceptance — 2026-10-04 (Europe/Stockholm)

The maintainer completed the current maintained UI review batch:

- WI-0165 is accepted after PR #505; slideshow counts, player-triggered **Prepared** continuity, reload/revalidation restoration and stale-state behavior work as expected.
- WI-0178 is accepted after PR #501; the maintained desktop and phone/PWA slideshow-library search/filter experience works as expected.
- WI-0185 is accepted after PRs #506, #507 and #508; the date-only audit filters and the remaining cross-person assignment audit workflow work as expected.

WI-0165, WI-0178 and WI-0185 are completed. M35 is completed. Issues #499 and #503 are resolved; issue #481 was already closed by PR #501 and now has maintained acceptance evidence.

The maintainer previously accepted WI-0163, WI-0167, WI-0169, WI-0170, WI-0171, WI-0172, WI-0173, WI-0175, WI-0176, WI-0177, WI-0179, WI-0180, WI-0181, WI-0183 and WI-0184.

## Next concrete step

WI-0151: after the isolated CLI pilot merges, integrate stable asset/revision identities, opt-in video discovery into the PostgreSQL catalogue, source-copy exclusion/cleanup and durable posters into the application; verify photo-only search/query defaults, OneDrive no-hydration and HDR/rotation on private samples. Other remaining review evidence: WI-0186 saved-list restart/exact Smart membership, WI-0187 Creative snapshots, WI-0182 photo-quality evaluation.

## Relevant pointers

- docs/delivery/work-items/WI-0186-scoped-semantic-caption-search.md
- docs/operations/semantic-caption-photo-search-evaluation.md
- docs/delivery/work-items/WI-0187-search-creative-anchors.md
- docs/operations/creative-collection-preview.md
- docs/delivery/work-items/WI-0182-photo-quality-scoring-evaluation.md
- docs/operations/photo-quality-evaluation.md
- docs/decisions/ADR-0012-isolated-video-media-architecture.md
- docs/operations/video-codec-survey.md
- docs/operations/video-codec-survey-evidence-2026-10-14.md
