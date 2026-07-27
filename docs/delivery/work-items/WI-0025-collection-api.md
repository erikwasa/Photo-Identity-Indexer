---
id: WI-0025
title: Add collection-ready queries
milestone: M14
status_source: ../status/work-items.yaml
depends_on: [WI-0015, WI-0016, WI-0029]
affected_modules: [PhotoIdentity.Api, PhotoIdentity.Web, PhotoIdentity.Persistence.Sqlite]
---

# WI-0025: Add collection-ready queries

## Objective

Expose stable local queries and neutral exports for photos containing one or more people, using the reviewed 500-image catalogue as the first acceptance dataset.

## Acceptance criteria

- [ ] Any-person and all-person semantics are explicit.
- [ ] Confirmed-only results are supported and are the safe default.
- [ ] Suggestion-backed results are opt-in and identify their model revision and threshold.
- [ ] Date, confidence, review-state and person filters can be combined predictably.
- [ ] Results can be inspected through the local web interface on Windows and Pixel.
- [ ] A neutral collection manifest can feed later slideshow or album applications without exposing unnecessary local paths.
- [ ] Query counts and representative results are checked against the pilot catalogue.
