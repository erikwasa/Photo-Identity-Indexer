---
id: M02
title: Local catalogue and jobs
status_source: ../status/milestones.yaml
depends_on: [M01]
---

# M02: Local catalogue and jobs

## Outcome

A local folder can be recursively catalogued and processed into SQLite with durable jobs, revisions, crops, embeddings, retries and resume support.

## Work items

WI-0011 through WI-0013.

## Exit criteria

- A 500-photo folder is indexed.
- Interrupted processing resumes.
- Reruns are idempotent.
- Changed files create new revisions.
- Unsupported formats and failures are reported separately.

## Completion

WI-0011 established transactional SQLite persistence, migrations and operational policy. WI-0012 added recursive local-folder scanning, immutable revisions and deletion markers. WI-0013 added leased resumable orchestration, production local inspection, start/resume commands and private 500-photo verification. M02 completed on 2026-07-26.
