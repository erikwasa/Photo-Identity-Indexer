---
id: M04
title: Minimal review application
status_source: ../status/milestones.yaml
depends_on: [M02]
---

# M04: Minimal review application

## Outcome

A local ASP.NET Core API and responsive Blazor PWA allow face review, person creation, manual labelling, rejection, undo and photo inspection from Windows and a Pixel on a trusted network.

## Work items

- [WI-0015](../work-items/WI-0015-review-ui.md)

## Exit criteria

- Labels persist after restart.
- Sensitive local paths are not unnecessarily exposed.
- Human review works comfortably on the phone.

## Current work

WI-0015 is implementing a same-origin local review host. SQLite schema version 4 stores append-only assignment, rejection and undo actions. The API owns filesystem and database access, and the Blazor WebAssembly client receives opaque image URLs and privacy-limited metadata. Automated tests cover restart persistence, audit history, reversal ordering and path redaction; Windows and Pixel interaction remain the final human acceptance boundary.
