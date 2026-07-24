---
id: WI-0015
title: Build minimal review application
milestone: M04
status_source: ../status/work-items.yaml
depends_on: [WI-0011, WI-0013]
affected_modules: [PhotoIdentity.Api, PhotoIdentity.Web]
---

# WI-0015: Build minimal review application

## Objective

Create a local ASP.NET Core API and responsive Blazor PWA for face galleries, person creation, manual labels, rejections, undo and photo details.

## Acceptance criteria

- [ ] The UI works on Windows and a Pixel on a trusted network.
- [ ] Labels persist after restart.
- [ ] Review actions are auditable and reversible.
- [ ] Sensitive source paths are not unnecessarily returned to the browser.
