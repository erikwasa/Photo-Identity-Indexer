---
id: ADR-0001
title: Use a modular monolith
status: accepted
date: 2026-07-24
supersedes: []
superseded_by: []
---

# ADR-0001: Use a modular monolith

## Context

The first version needs clear replacement boundaries but not distributed deployment complexity.

## Decision

Build separate modules and executables around one application model and local canonical database. Enforce dependency direction through project references and tests.

## Consequences

Development and debugging stay simple, while source, persistence, imaging, recognition and bundle adapters remain independently replaceable. Service extraction is deferred until a measured need exists.
