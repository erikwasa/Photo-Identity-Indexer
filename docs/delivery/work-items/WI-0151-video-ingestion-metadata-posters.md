---
id: WI-0151
title: Ingest supported videos and extract metadata plus poster proxies
milestone: M30
status_source: ../status/work-items.yaml
depends_on: [WI-0150]
related_adrs: []
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Source.Local, PhotoIdentity.Source.OneDriveSync, PhotoIdentity.Api, PhotoIdentity.Persistence.Postgres, docs]
---

# WI-0151: Ingest supported videos and extract metadata plus poster proxies

## Objective

Recognize supported video files as catalogue assets and derive enough metadata/poster imagery for normal library browsing.

## Why

The first useful step is making videos visible/searchable rather than silently unsupported files.

## In scope

- Recognize the activated MP4/MOV subset.
- Persist media kind, duration, dimensions/rotation, frame rate and container/codec/stream details, and available capture time/camera/GPS using documented precedence.
- Generate bounded durable *still* posters/review images tied to source revision; playable lower-resolution video is WI-0189.
- Make initial video discovery opt-in and do not automatically enqueue all discovered videos for analysis.
- Respect OneDrive availability and hydration rules, avoid large-file hydration for cheap enumeration and classify cases requiring local video bytes.
- Report unsupported codecs/containers explicitly.

## Out of scope

- Face recognition across frames.
- Universal transcoding.
- Video job scheduling (WI-0188), smaller playable video cache (WI-0189) and captions/embeddings (WI-0190).
- Live Photo pairing.

## Acceptance criteria

- [ ] Supported videos appear as assets with duration/media kind.
- [ ] A durable poster supports browsing without replaying the original.
- [ ] Date, GPS, duration, rotation, stream metadata and unsupported/unknown fields behave predictably.
- [ ] Default-off video support does not change photo-only ingestion or query results.
- [ ] Unsupported variants remain visible in reporting.
- [ ] Original videos remain read-only.

## Verification requirements

Source, metadata, persistence and representative real-file verification.

## Completion notes

- Files changed:
- Trade-offs:
- Deferred work:
- Commands run:
