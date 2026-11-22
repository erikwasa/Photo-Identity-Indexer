---
id: M14
title: Collection-ready API
status_source: ../status/milestones.yaml
depends_on: [M06]
---

# M14: Collection-ready API

## Outcome

The locally reviewed catalogue can answer practical people-in-photo queries and produce neutral manifests for later collection, slideshow or album tools.

## Work items

- [WI-0025](../work-items/WI-0025-collection-api.md)

## Exit criteria

- Any-person and all-person semantics are explicit.
- Confirmed-only results are supported.
- Suggestion-backed results are opt-in and model-versioned.
- Filters and exports can be exercised against the 500-image pilot catalogue.
- A neutral collection manifest does not expose unnecessary local source paths.

## Completion

M14 completed on 2026-08-02.

The final local workflow provides confirmed and exact-model advisory collection queries, explicit any/all and review-state semantics, a responsive Windows/Pixel collection workspace, bounded server-generated thumbnails, and a complete versioned neutral manifest with opaque HTTP resource URLs.

Automated validation passed through GitHub Actions build #401. The operator completed private-catalogue Windows and Pixel verification and retained detailed counts and representative-result evidence outside Git; only the privacy-safe completion statement is recorded in the canonical work-item registry.
