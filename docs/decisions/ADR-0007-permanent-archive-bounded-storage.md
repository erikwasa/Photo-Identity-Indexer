---
id: ADR-0007
title: Use one permanent archive identity with bounded local materialization
status: accepted
date: 2026-08-09
supersedes: []
superseded_by: []
---

# ADR-0007: Use one permanent archive identity with bounded local materialization

## Context

The Personal OneDrive archive is logically larger than the local free space available for simultaneous hydration. The catalogue also needs to grow incrementally: a narrow month may be processed first and a broader year may be included later without duplicating earlier work.

## Decision

Represent the photo archive as one stable permanent source root plus normalized relative included folders.

Parent coverage subsumes covered children. Every synchronization revisits all included coverage for new, changed, missing and newly available files, while unchanged immutable revisions with completed exact-profile analysis are reused.

Authoritative originals remain in OneDrive. Photo Identity keeps the canonical catalogue, model-derived data and durable review proxies locally. Full-resolution originals are hydrated only as a bounded managed working set for source verification, analysis or explicit viewing.

Managed hydration is governed by explicit free-space reserve, maximum managed bytes and concurrency limits. Photo Identity may automatically release only content it hydrated/owns; pre-existing local or user-pinned content is never an eviction candidate.

Normal browsing uses review proxies and must not hydrate authoritative originals.

## Consequences

The full logical archive can be catalogued without fitting on local disk at once. Source availability, source verification, immutable content identity, analysis completion and proxy completion remain separate states.

The system must preserve managed hydration ownership durably across restart and must not count a release request as free capacity until OneDrive actually reports the source online-only.