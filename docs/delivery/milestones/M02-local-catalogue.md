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

## Current work

WI-0011 persistence and WI-0012 local-folder scanning are complete and verified. WI-0013 is adding expiring leases, checkpoints, cancellation, bounded retries and resumable worker orchestration before the production inspection handler and real 500-photo verification complete M02.
