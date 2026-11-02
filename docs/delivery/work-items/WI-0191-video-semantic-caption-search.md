---
id: WI-0191
title: Offer opt-in semantic and caption search over indexed videos
milestone: M30
status_source: PhotoIdentity.Docs
depends_on: [WI-0190]
related_adrs: []
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Api, PhotoIdentity.Persistence.Postgres, PhotoIdentity.Web, docs]
---

# WI-0191: Offer opt-in semantic and caption search over indexed videos

## Objective

Make video frame evidence discoverable through Visual, Caption and Combined retrieval without changing existing photo Search behavior.

## In scope

- Start as a separate Video Search view or explicit media-type selector with Photos as the unchanged default.
- Reuse currently supported text-to-image query embeddings, caption retrieval and reciprocal-rank fusion where suitable; do not claim photo scores equal calibrated video relevance.
- Aggregate segment/frame evidence into **one result per video revision** with provenance, representative timestamp/interval and explainable top match.
- Filter excluded, inaccessible, unsupported, stale or missing-evidence revisions before ranking; do not trigger new video decoding/hydration during search.
- Keep existing saved photo-result membership and exact Smart Collection filters unchanged; video Smart scope comes only after WI-0153 explicitly defines eligibility.
- Benchmark realistic frame volumes, result limit, latency and query relevance before adopting heavier index technology.

## Out of scope

Audio/subtitle search, mixed-media Creative Collection recipes, live semantic Smart Collection filters and automatic generation on search.

## Acceptance criteria

- [ ] Visual, Caption and Combined modes return videos only when video mode is selected, with one result per source revision.
- [ ] Results retain the grounded matching frame timestamp/description and explicit generated-evidence provenance.
- [ ] Existing photo-only search eligibility, ranking, saving and baseline latency are preserved.
- [ ] Missing/stale/excluded videos never leak; requests are read-only with respect to video processing.
- [ ] Maintainer verifies representative queries with small and large video sets.

## Verification requirements

Query/ranking/access tests and maintained-archive comparison of video search relevance and photo-search regression.

## Implementation checkpoint 1 — isolated read-only search backend (2026-11-02)

The first WI-0191 branch implements a separate `POST /api/video-search/query`
route, enabled only when the existing explicit video-library configuration
`PhotoIdentity:VideoLibraryEnabled=true` is active. It does not change
`/api/photo-search/query`, the photo page, Smart Collection eligibility,
photo result selections or saved photo collections. The endpoint returns
only indexed videos and generated captions with a grounded frame timestamp,
evidence policy/model labels and the existing revision-bound poster URL.
It neither decodes media nor schedules video processing/hydration.

An isolated PostgreSQL repeatable-read, read-only snapshot selects at most
256 current, supported SDR video revisions with published successful
FrameEvidence batches, local-folder source authority, no deletion and no
source-copy exclusion. It verifies frame count, ordinals, timestamp order,
vector dimensions and finite values before exposing candidates to ranking.
The pure core ranker reduces each video to its best Visual and Caption
frame separately, performs reciprocal-rank fusion **once per video**, and
uses the already-approved local CLIP text encoder for Visual matching only
when evidence model hash/preprocessing match. Caption-only matching works
without the model; no remote or third-party inference is added.

**Not delivered by this checkpoint:** the dedicated browser Video Search
view, accessible poster/playback navigation, production-quality caption
full-text ranking, proof of local availability after a proxy eviction,
large-corpus scaling/relevance comparison and maintainer verification.
The initial caption ranker is intentionally simple token overlap/phrase
matching, and the 256-video ceiling is explicit rather than silently
presented as full-library coverage. Existing acceptance boxes remain
**0/5 pending** until the full end-to-end workitem is verified.
See [backend pilot procedure](../../operations/video-search-backend-checkpoint.md).

## Maintained-host backend smoke — 2026-11-02

After PR #711 merged and its final Windows CI run
[passed](https://github.com/erikwasa/Photo-Identity-Indexer/actions/runs/41426752736),
the maintainer exercised the opt-in endpoint against the existing local
PostgreSQL catalogue and approved CLIP installation. Caption, Semantic
and Combined each returned **3 video revisions from 3 candidates**;
all modes reported one result per revision and no candidate-limit
truncation. Model availability was intentionally false for Caption
(which does not initialize CLIP), and true for Semantic and Combined;
the separately approved local CLIP file check returned true.
The observed elapsed values were **32.3, 77.2 and 59.9 ms** respectively.
These are small-pilot observations, not a benchmark.

The Combined response carried frame-relative timestamps, generated
FrameEvidence provenance, caption and semantic source flags, sampling
policy versions, CLIP/caption model identities, and revision-bound
poster routes. The maintainer supplied no personally identifying media
details for repository documentation.

Photo-search regression smoke: the initial photo Caption query returned
HTTP success with zero matches, followed by a separate **known-positive
Caption query** returning **10 photo results (limit 10)**. The response
reported **26,904 indexed photos** and **24,770 displayable captions**.
This establishes that the existing photo Caption API can return results
against the maintained catalogue after the video-search change. It does
**not** verify unchanged relevance/ranking, other photo search modes,
saved-photo workflows or baseline latency without a before/after
comparison. The query text and photo identifiers are deliberately omitted.

Local focused test and docs-command outputs, poster byte availability,
exclusion negative cases, large-corpus results, and video relevance
judgments were not reported by this pilot. The browser Video Search
page remains unimplemented; the five work-item acceptance boxes remain open.

## Implementation checkpoint 2 — separate Video Search browser (2026-11-02)

A dedicated Blazor WebAssembly route `/video-search` now uses the
checkpoint-1 video-only query API. It has explicit Visual, Caption and
Combined modes; bounded result limits; a natural-language query field;
one card per returned video revision with stored frame timestamp,
generated description, source badges, and expandable model, sampling and
prompt provenance. Poster requests use only the revision-bound local
`/api/videos/{revision}/poster` path; invalid paths are never rendered,
and missing poster bytes display an honest placeholder rather than
triggering source hydration or thumbnail generation. A visible notice
explains 256-candidate truncation, and Visual-unavailable mode is
explained to the user instead of presenting empty results as conclusive.
All returned caption text is HTML-encoded by Blazor and is not included
in browser URL parameters.

The browser checks the existing opt-in video-library endpoint before
showing search controls, and handles a 404 if the operator disables the
library while the page is open. The existing photo `/search` experience,
photo selection state, slideshow collection actions and their endpoint
contracts remain untouched. Navigation is under Advanced > Video
search and on the existing Videos page. There is **no** video playback,
Smart Collection integration, original-source access, background
processing or OneDrive hydration.

New pure presentation tests cover timestamp formatting and refusing
poster URLs that do not match their revision. Full client compilation,
CI, manual layout and actual poster availability are unverified until
this PR is validated. The maintained-host query evidence currently
covers three eligible video revisions only, so the complete workitem
still needs relevant larger-set and exclusion/eligibility verification.
**Do not mark all five acceptance criteria complete at this checkpoint.**

## Completion notes

- Files changed:
- Trade-offs:
- Deferred work:
- Commands run:
