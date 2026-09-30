---
id: WI-0172
title: Provide a separate operator space for slideshow collection management
milestone: M32
status_source: ../status/work-items.yaml
depends_on: []
related_adrs: []
affected_modules: [PhotoIdentity.Web, PhotoIdentity.Integration.Tests, docs]
---

# WI-0172: Provide a separate operator space for slideshow collection management

## Objective and maintainer evidence

Provide a discoverable full-app management hub linking Smart, manual and Creative editors. Creative deletion already exists at /creative-collections but is difficult to discover. Keep editing/deletion outside the slideshow viewing surface.

Reported 2026-09-30 (Europe/Stockholm) during M32 acceptance.

## Acceptance criteria and verification

1. From full-app navigation open Manage slideshow collections.
2. Open each collection editor; rename/delete a disposable Creative Collection and verify its anchor/siblings remain intact.
3. Confirm the viewing library exposes no editing/deletion controls.

## Status

Maintained desktop/phone verification remains outstanding.

## Implementation

The full app now exposes **Manage collections** at `/slideshow-collections`, listing Smart, manual and Creative Collections with direct links to their editors. Creative links select the exact saved recipe using the `collection` query parameter, making its existing Delete control discoverable without changing its source or sibling recipes. The manual editor uses `MainLayout`, supports collection deletion with a confirmation, and returns to the management hub. No original-photo deletion is added.

The viewing library no longer links directly to the manual editing surface or Creative management; its existing **Full app** path leads to management through full-app navigation. The separate Creative editor also shows **Working…** and the server's actionable preview error.

Verify a disposable collection of each type, direct editor selection and rename/delete behavior, and confirm there are no mutation controls in the viewing library. Maintained desktop/phone acceptance remains pending.

## Follow-up validation

The affected API/Web/test projects build, and 50 focused non-host slideshow/receipt/cache/route/Creative optimization tests pass. Documentation `validate` and `generate --check` pass. PostgreSQL-backed preparation tests could not run locally because the test admin connection was unavailable; those cases remain required in CI. Maintained archive/phone acceptance is not claimed.
