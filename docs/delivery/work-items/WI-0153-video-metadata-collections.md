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

## Objective

Let videos participate in appropriate manual metadata and collection workflows using the same canonical concepts as photos.

## Why

Videos should not become a disconnected parallel library.

## In scope

- Apply effective date and Place contracts to videos.
- Allow media-neutral manual tags/people/location/date metadata.
- Include videos in Smart Collections using documented semantics.
- Allow explicit collections to mix images/videos.
- Add media-kind filtering only if compatibility requires it.

## Out of scope

- Treating video frame identities as photo-level people evidence before WI-0155.
- Changing canonical identity semantics.
- Slideshow timing.

## Acceptance criteria

- [ ] A video can receive supported manual metadata without source mutation.
- [ ] Smart Collection queries can include matching videos.
- [ ] Existing saved photo collections retain documented compatibility.
- [ ] Explicit collections preserve mixed-media membership.

## Verification requirements

Persistence/query/API/UI tests plus mixed-media library verification.

## Completion notes

- Files changed:
- Trade-offs:
- Deferred work:
- Commands run:
