---
id: ADR-0004
title: Treat Azure as disposable compute
status: superseded
date: 2026-07-24
supersedes: []
superseded_by: [ADR-0010]
---

# ADR-0004: Treat Azure as disposable compute

## Context

Azure credits were originally considered useful for batch inference, while enterprise policies restricted identities and the budget was limited.

## Historical decision

Keep canonical data local. Send finite portable bundles to temporary Azure compute using interactive control and SSH/SCP or short-lived SAS. Return result bundles and deallocate or delete resources.

## Supersession

ADR-0010 supersedes this as current architectural intent. Production processing now stays on maintainer-controlled local hardware and Azure is no longer a planned execution target. This ADR is retained as the historical rationale for the earlier disposable-compute design.

## Historical consequences

The worker was designed to remain cloud-independent and no permanent cloud identity was required. Bundle integrity, checkpointing and idempotent import became first-class requirements; those portable-bundle properties remain useful even though Azure execution is no longer planned.
