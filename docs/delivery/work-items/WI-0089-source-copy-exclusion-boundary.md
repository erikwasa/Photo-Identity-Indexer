---
id: WI-0089
title: Add durable source-copy exclusion and access enforcement
milestone: M23
status_source: ../status/work-items.yaml
depends_on: [WI-0041, WI-0042]
related_adrs: [ADR-0008]
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Persistence.Sqlite, PhotoIdentity.Api, PhotoIdentity.Web, PhotoIdentity.Worker, documentation]
---

# WI-0089: Add durable source-copy exclusion and access enforcement

## Objective

Introduce a durable source-copy-specific exclusion state that immediately makes one source locator inaccessible and non-processable through Photo Identity while leaving the OneDrive/source original untouched.

## Why

A private photo can legitimately remain backed up in personal OneDrive while being unsuitable for an application used by other people. A normal visibility filter is insufficient because Photo Identity could still retain/serve originals, proxies, face crops, embeddings or metadata.

## In scope

- Persist exclusion against one source plus normalized source key/path, independently of content hash and duplicate grouping.
- Make exclusion durable before any purge work starts.
- Keep exclusion distinct from automatic source presence/missing state.
- Ensure the same excluded locator stays excluded across restart/rescan and if that locator later reappears.
- Do not propagate exclusion to exact duplicates at other paths.
- Do not carry exclusion across a move/rename; the new path is independently included until explicitly excluded.
- Add a central enforceable exclusion check below UI-only filtering.
- Immediately deny excluded content from analysis/job scheduling, detector/embedding/identity regeneration, metadata/tag/Places enrichment, derivative generation, Smart Collections, slideshow manifests, photo/face review, original/proxy media serving, explicit original viewing and managed hydration.
- Permit only the minimal archive/status operations needed to show the source locator as excluded and manage purge/retry/restore.
- Ensure normal scanning can observe the excluded locator's presence without reopening it for analysis/content extraction.

## Out of scope

- Actual filesystem/database purge; WI-0090 owns deletion.
- Deleting the source original.
- Content-hash denylisting.
- Excluding all exact duplicates in one implicit action.
- UI workflows beyond the minimum enforcement/status plumbing; WI-0091 owns operator UX.

## Acceptance criteria

- [ ] Exclusion is keyed to one source copy/locator and is durable across restart.
- [ ] Excluding one of two exact duplicate paths does not exclude the other.
- [ ] A moved/renamed excluded file is not automatically excluded at its new path.
- [ ] A source file at an actively excluded locator cannot be scheduled for new photo, face, metadata, place or identity work.
- [ ] Smart Collections, slideshow manifests and normal review/library queries cannot return excluded content.
- [ ] Original/proxy/hydration endpoints reject excluded content even if a caller holds a previously valid opaque revision/resource identifier.
- [ ] The excluded original is never modified or deleted.
- [ ] Exclusion remains effective while purge is pending or failed.
- [ ] Repeated scans do not recreate revisions/proxies/analysis for the excluded locator.
- [ ] Logging and API errors do not expose private source paths or photo content.

## Verification requirements

Automated cross-layer tests are required for scheduler/query exclusion and direct media-resource denial, including a request made with an identifier captured before exclusion. Add tests for duplicate independence, same-locator persistence and moved-excluded-path behavior.

## Completion notes

- Files changed:
- Trade-offs:
- Deferred work:
- Commands run:
