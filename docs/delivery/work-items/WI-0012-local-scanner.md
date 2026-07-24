---
id: WI-0012
title: Add local folder scanning
milestone: M02
status_source: ../status/work-items.yaml
depends_on: [WI-0011]
affected_modules: [PhotoIdentity.Source.Local]
---

# WI-0012: Add local folder scanning

## Objective

Recursively catalogue supported files, record stable source metadata, detect changes and mark deletions.

## Acceptance criteria

- [ ] Repeated scans do not duplicate unchanged assets.
- [ ] Changed files create new revisions.
- [ ] Deleted files are marked without deleting labels.
- [ ] Unsupported formats are reported.
