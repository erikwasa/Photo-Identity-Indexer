---
id: WI-0013
title: Add resumable batch processing
milestone: M02
status_source: ../status/work-items.yaml
depends_on: [WI-0010, WI-0011, WI-0012]
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Cli, PhotoIdentity.Worker]
---

# WI-0013: Add resumable batch processing

## Objective

Add durable jobs, attempts, checkpoints, cancellation, bounded retries and idempotency keys for local batch processing.

## Acceptance criteria

- [ ] A stopped run resumes without duplicating completed results.
- [ ] At most the active asset is repeated after interruption.
- [ ] Transient and permanent failures are separated.
- [ ] A 500-photo sample produces a status summary.
