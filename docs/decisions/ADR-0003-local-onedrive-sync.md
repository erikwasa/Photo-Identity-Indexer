---
id: ADR-0003
title: Use local OneDrive sync as the photo source
status: accepted
date: 2026-07-24
supersedes: []
superseded_by: []
---

# ADR-0003: Use local OneDrive sync as the photo source

## Context

Personal OneDrive is separate from the enterprise Azure tenant, where app registrations and application identities are restricted.

## Decision

Use the official Windows OneDrive sync client for authentication and local filesystem access. Azure workers receive prepared bundles and never access OneDrive.

## Consequences

No Graph registration or token handling is required. Placeholder hydration, staging and content-based move reconciliation become application concerns.
