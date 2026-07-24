---
id: ADR-0002
title: Keep canonical labels independent of models
status: accepted
date: 2026-07-24
supersedes: []
superseded_by: []
---

# ADR-0002: Keep canonical labels independent of models

## Context

Embeddings and clusters are model-specific and may need regeneration.

## Decision

Attach human-confirmed identities to stable face occurrences. Store detector observations, crops, embeddings and suggestions as versioned derived data.

## Consequences

Models can be compared or replaced without losing labelling work. The system must reconcile detector observations to canonical face occurrences.
