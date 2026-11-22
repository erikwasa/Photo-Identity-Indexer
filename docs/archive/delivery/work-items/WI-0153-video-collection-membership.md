---
id: WI-0153
title: Integrate videos into explicit manual photo/video collections
milestone: M30
status_source: ../status/work-items.yaml
depends_on: [WI-0151, WI-0141, WI-0144]
related_adrs: [ADR-0012]
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Api, PhotoIdentity.Persistence.Postgres, PhotoIdentity.Web, docs]
---

# WI-0153: Integrate videos into explicit manual photo/video collections

## Scope split (maintainer 2026-11-08)

This proposed item now owns **only explicit collection membership**.
Metadata extraction/display/correction, GPS-to-Place enrichment and manual
video-level person presence belong to [WI-0212](WI-0212-video-metadata-place-and-manual-people.md).
The metadata work can proceed independently; no collection membership or
slideshow implementation is implied. The former document filename contained
"metadata" because these two concerns were originally combined.

## Objective

Allow an operator to deliberately include both existing photos and
eligible videos in an explicitly selected manually curated collection
without modifying the semantics of current photo-only collections.

## Scope when activated

- Specify a media-type-aware explicit membership contract that can refer
  to a current photo or current video asset/revision without confusing
  a video poster with a photo or assuming all videos have playable proxies.
- Provide an opt-in, human-controlled add/remove flow for video membership
  using existing video/library identifiers and guarded catalogue APIs.
- Preserve the previous photo collection membership, ordering where defined,
  stable saved state and existing photo-only display/search behavior.
- Show clearly when a member video is catalogued but has no retained playable
  proxy, is excluded/stale or is otherwise unavailable.
- Keep collection operations metadata-only, with no source reads, OneDrive
  hydration, transcoding, frame inference or automatic video jobs.
- Define an explicit selection contract that later
  [WI-0154](WI-0154-video-slideshow-playback.md) can reuse.

## Out of scope

- Video capture metadata, date/time, GPS, camera, resolution display or
  corrections, Place lookup and manual person tags: WI-0212.
- Automatic person identification or video-face inference: WI-0155.
- Dynamic video Smart Collection filters or a new media-neutral
  query engine; those require a separate decision.
- Automatically converting current photo-only saved collections into
  mixed-media collections or enabling video slideshows in this item.
- Generating missing posters/proxies simply to add a video to a collection.

## Acceptance criteria

- [ ] An explicitly chosen collection can add, display and remove an
  eligible video alongside photos; existing photo-only collections
  remain unchanged by default.
- [ ] Membership survives restart and responds predictably to current
  revision changes, exclusions, deletion and unavailable video proxies.
- [ ] Adding or browsing collection members makes no changes to original
  media or automatic processing/hydration state.
- [ ] Existing photo search, saved membership, Smart/Creative Collection
  contracts and photo-only slideshow behavior remain unchanged.
- [ ] A small maintainer-reviewed selection API/UI contract is recorded
  for a later explicit mixed slideshow, without implementing it here.

## Verification requirements

Focused disposable PostgreSQL/API and UI tests, plus a small human review
of the selected mixed collection. No original-media read is required just
to change membership.

## Completion notes

- Status: proposed/deferred. WI-0212 is separately prioritized for
  near-term metadata and person presence.
