---
id: ADR-0010
title: Keep production processing on local hardware
status: accepted
date: 2026-09-12
supersedes: [ADR-0004]
superseded_by: []
---

# ADR-0010: Keep production processing on local hardware

## Context

Photo Identity was originally designed so temporary Azure compute could be used for identity-free batch processing while the Windows machine retained canonical data. The local workflow has since matured substantially: the permanent archive, bounded OneDrive hydration, governed model sessions, PostgreSQL catalogue, backup/restore and archive-scale catch-up are all operating successfully on maintainer-owned hardware.

The remaining value of an Azure execution path no longer justifies maintaining separate cloud deployment, checkpointing, cost-control and local/cloud consistency work as part of the planned product roadmap.

## Decision

Production Photo Identity processing runs on maintainer-controlled local hardware.

The Windows machine remains the trusted application/control environment. The authoritative PostgreSQL catalogue, personal source access, derived biometric data, review history, model execution and normal archive processing remain local. Personal OneDrive continues to be accessed through the Windows sync client rather than a cloud API.

Azure is not a planned processing target. Existing portable-bundle and historical Azure documentation may be retained as historical design material or reusable offline-transfer tooling, but current delivery work must not assume an Azure pilot, Azure checkpointing or Azure evidence gate.

A future cloud or remote-compute path would require a new architecture decision and newly scoped work; it must not be inferred from ADR-0004 or the retired M09-M11 work.

## Consequences

- M09 and M10 are retired without implementation.
- M11's Azure-dependent production-model selection plan is retired; the governed production archive models are selected from the accepted local evidence and operating configuration.
- Full-archive operation is judged on the local production system. M24/WI-0106 provides the accepted archive-scale catch-up and daily-increment evidence.
- No Azure-specific checkpointing, VM lifecycle, Blob/SAS or local/cloud numerical-consistency work is required for the current roadmap.
- Portable job bundles remain useful as a compute-isolation/file-transfer contract, but they no longer imply planned Azure execution.
