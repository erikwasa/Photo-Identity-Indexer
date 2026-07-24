---
id: ADR-0004
title: Treat Azure as disposable compute
status: accepted
date: 2026-07-24
supersedes: []
superseded_by: []
---

# ADR-0004: Treat Azure as disposable compute

## Context

Azure credits are useful for batch inference, but enterprise policies restrict identities and the budget is limited.

## Decision

Keep canonical data local. Send finite portable bundles to temporary Azure compute using interactive control and SSH/SCP or short-lived SAS. Return result bundles and deallocate or delete resources.

## Consequences

The worker remains cloud-independent and no permanent cloud identity is required. Bundle integrity, checkpointing and idempotent import become first-class requirements.
