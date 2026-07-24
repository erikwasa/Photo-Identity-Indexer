---
id: WI-0018
title: Add portable bundles
milestone: M07
status_source: ../status/work-items.yaml
depends_on: [WI-0013]
affected_modules: [PhotoIdentity.Transfer.Bundles, PhotoIdentity.Worker]
---

# WI-0018: Add portable bundles

## Objective

Implement job and result bundles with manifests, checksums, full-image, reduced-image and face-crop profiles, plus idempotent result import.

## Acceptance criteria

- [ ] A worker processes a bundle without database access.
- [ ] Corrupt or stale results are rejected.
- [ ] Reimporting the same bundle is harmless.
- [ ] Human labels are unaffected by bundle import.
