---
id: ADR-0008
title: Scope exclusion to one source copy and purge retained Photo Identity data
status: accepted
date: 2026-08-29
supersedes: []
superseded_by: []
---

# ADR-0008: Scope exclusion to one source copy and purge retained Photo Identity data

## Context

Photo Identity indexes a personal OneDrive-backed archive that may contain exact duplicate files, files that are later deleted during ordinary photo cleanup, files that are moved or renamed, and private source photos that must remain backed up in OneDrive but must not remain accessible through Photo Identity.

The existing catalogue distinguishes source presence from immutable revision/analysis state and retains missing-photo history. That is useful for accidental deletion and move reconciliation, but a privacy exclusion requires stronger semantics: local proxies, crops, embeddings, identifications and photo metadata must actually be removed.

Exclusion must also be predictable when the same bytes exist at multiple paths. The operator wants to control each source copy independently rather than have one exclusion unexpectedly hide another copy or follow a later move.

## Decision

Treat exclusion as a durable source-locator privacy boundary keyed logically by SourceId plus normalized source key/path.

- Exact duplicate files remain independent source copies.
- SHA-256 duplicate grouping never propagates exclusion.
- An excluded source locator remains excluded while that locator is observed or reappears.
- Excluded source locators do not participate in automatic move/rename reconciliation.
- If excluded bytes appear at another path, that path is a new independently controlled source copy and must be excluded separately.
- Included assets may still preserve identity across an exact, unambiguous move/rename.
- Source disappearance alone remains a non-destructive missing/removed state until the operator chooses exclusion/purge.

When exclusion is requested, make the exclusion durable and deny all media/processing access before cleanup. Then perform a crash-safe, resumable purge of Photo Identity's photo-specific retained data and filesystem derivatives. The source original remains untouched.

After purge, retain only a minimal source-locator tombstone and purge operational state required to prevent re-indexing at that locator and to retry cleanup. Do not retain the excluded photo's revision hash, pixels, dimensions, location, tags, faces, embeddings or identity links.

This purge is an intentional privacy exception to the normal rule that canonical review history is append-only.

## Consequences

Source-copy control remains understandable: two identical paths can be treated differently, and moving an excluded file does not silently carry policy to the destination.

Duplicate detection and move reconciliation can still use authoritative hashes for included catalogue state, but excluded content is not a persistent content-level denylist.

The purge path becomes safety-critical. It must coordinate SQLite deletion with on-disk derivative deletion, remain idempotent across crashes/restarts, expose pending/failed cleanup, and keep excluded media inaccessible even while cleanup is incomplete.

Re-including a purged locator requires fresh catalogue/analysis work because the previous revision-linked data was deliberately erased.
