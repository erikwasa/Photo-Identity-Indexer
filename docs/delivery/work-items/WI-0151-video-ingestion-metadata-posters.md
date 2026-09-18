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
- Persist media kind, duration and available capture metadata.
- Generate bounded browser-compatible poster/review proxies.
- Respect OneDrive availability and hydration rules.
- Report unsupported codecs/containers explicitly.

## Out of scope

- Face recognition across frames.
- Universal transcoding.
- Live Photo pairing.

## Acceptance criteria

- [ ] Supported videos appear as assets with duration/media kind.
- [ ] A durable poster supports browsing without replaying the original.
- [ ] Unsupported variants remain visible in reporting.
- [ ] Original videos remain read-only.

## Verification requirements

Source, metadata, persistence and representative real-file verification.

## Completion notes

- Files changed:
- Trade-offs:
- Deferred work:
- Commands run:
