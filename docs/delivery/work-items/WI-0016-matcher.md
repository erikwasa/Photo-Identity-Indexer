---
id: WI-0016
title: Add identity matcher
milestone: M05
status_source: ../status/work-items.yaml
depends_on: [WI-0009, WI-0015]
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Persistence.Sqlite]
---

# WI-0016: Add identity matcher

## Objective

Compare unlabelled embeddings with human-confirmed exemplars using exact cosine similarity and persist ranked suggestions with score margins.

## Acceptance criteria

- [ ] Best and second-best candidates are recorded.
- [ ] Rejected face-person pairs are filtered.
- [ ] Suggestions can be regenerated without changing labels.
- [ ] Only human-confirmed examples are used as exemplars.
