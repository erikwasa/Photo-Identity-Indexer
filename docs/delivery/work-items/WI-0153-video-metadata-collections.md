---
id: WI-0153
title: Integrate video with manual metadata and collection queries
milestone: M30
status_source: ../status/work-items.yaml
depends_on: [WI-0151, WI-0141, WI-0144]
related_adrs: []
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Api, PhotoIdentity.Persistence.Postgres, PhotoIdentity.Web, docs]
---

# WI-0153: Integrate video with manual metadata and collection queries

## Status and intent (maintainer 2026-11-02)

**Proposed but deferred; do not start as part of the next M30 slice.**
Video organization and collections are wanted later, but basic
browser playback, a first archive-driven compatibility expansion
and opt-in online-only handling take priority. No new mixed-media
Smart Collection query engine is authorized by this workitem.

## Objective when resumed

Offer ordinary useful video metadata and, eventually, explicit
photo/video collection membership without changing existing
photo-only collections. Keep the interface friendly rather than
exposing every FFprobe stream attribute.

## Useful fields and scope

- Prefer an existing embedded capture date/time with documented
  precedence and provenance; show date and **time of day** when
  reliable, not a fabricated recording time from filesystem mtime.
- Location: GPS / Place when present, optionally altitude when
  reliable; do not infer location from a filename or invent a value.
- Camera/device: make and model, if present.
- Basic video quality/format for users: resolution/display dimensions,
  duration and optionally frames per second. Show simple labels;
  codec/pixel format/stream internals remain optional diagnostics.
- Use the existing catalogue technical projection where possible
  rather than reprobe/hydrate originals on every metadata page.
- Allow ordinary manual video tags/date/location edits later without
  writing back to originals.
- In a **later separately approved phase**, support explicit manual
  photo/video collections while preserving existing photo membership.

## Explicitly postponed

- **Video-enabled Smart Collection filters and dynamic membership**:
  require a distinct future acceptance decision/workitem if users
  need them. Do not silently broaden saved photo Smart filters.
- Media-neutral semantic/identity inference; applying faces from
  video frames to canonical people requires WI-0155 policy.
- Automatic high-detail technical extraction, editing source metadata,
  reverse geocoding or inferred altitude from unsupported fields.
- Building explicit mixed collections immediately. Their design is
  listed for the future, not an active release gate.

## Acceptance plan once resumed

- [ ] Maintainer chooses which basic display fields and manual edits
  are useful based on existing catalogue records; unsupported values
  show as unknown without creating new processing jobs.
- [ ] A video displays accurate available date/time, GPS, make/model,
  resolution/duration and optionally FPS/altitude without source writes.
- [ ] Existing photos and saved collections remain unchanged.
- [ ] If an explicit mixed-media collection phase is deliberately
  activated, membership is stable and verified independently from
  Smart Collection query semantics.

## Verification requirements

Focused local metadata display/persistence tests when implemented.
The scope is intentionally not a mandate to support video-enabled
Smart Collections. Collections are postponed, not cancelled.

## Completion notes

- Status: proposed/deferred by maintainer, 2026-11-02.
