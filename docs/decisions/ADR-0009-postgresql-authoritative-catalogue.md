---
id: ADR-0009
title: Use PostgreSQL as the long-term authoritative catalogue
status: accepted
date: 2026-09-01
supersedes: []
superseded_by: []
---

# ADR-0009: Use PostgreSQL as the long-term authoritative catalogue

## Context

Photo Identity has grown from a mostly local, operator-driven SQLite application into a system with concurrent interactive and background catalogue workloads: archive advancement, analysis/post-analysis, match regeneration, automatic place enrichment, slideshow preparation, review actions and read-heavy gallery/status queries.

A real-catalogue failure on 2026-09-01 showed SQLite table-lock contention causing `ArchiveAdvancementHostedService` to throw while match regeneration was active. Because the recovery write was also locked, the exception escaped and the .NET host shut down. The same run showed catalogue-backed status requests stretching from milliseconds to many seconds under contention.

The application also needs to progress through the full existing archive and then sustain smaller daily updates. Repeated operator comparisons between alternative database configurations are not a product goal.

## Decision

Use PostgreSQL as Photo Identity's long-term authoritative catalogue.

- PostgreSQL becomes the system of record for catalogue, review, identity, archive-processing, metadata, smart-collection, slideshow and operational state.
- SQLite remains supported only during migration, import/cutover and rollback/snapshot scenarios; new authoritative runtime features must not add new SQLite-only persistence.
- Run PostgreSQL locally through a Podman-compatible container setup for the primary operator environment. Keep the database connection/configuration portable enough that native or externally managed PostgreSQL remains possible.
- Preserve existing stable IDs, history, evidence-version semantics and privacy boundaries during migration.
- Migrate by bounded vertical slices so the application remains buildable and testable throughout.
- Do not require comparative SQLite/PostgreSQL benchmark exercises before migration. Add low-overhead production metrics instead.
- Do not assume PostgreSQL alone fixes inefficient algorithms or query shapes. Match regeneration, gallery queries, image serving and settings loading receive explicit corrective work.
- Keep pgvector/approximate nearest-neighbor search as an optional later optimization. Initial migration preserves current exact matching semantics.

## Consequences

Photo Identity gains a database designed for concurrent readers/writers and a clearer path to larger catalogues, richer query plans and future vector-search acceleration.

The cost is a real persistence migration: current application code directly depends on many `Sqlite*` repositories, SQL dialect details and schema helpers. The migration therefore requires a database-neutral boundary, PostgreSQL schema/migrations, a verified SQLite-to-PostgreSQL importer, operational backup/restore and cutover procedures.

The local operator environment gains one infrastructure dependency: PostgreSQL must be running before Photo Identity can start normally. The launcher/operator experience must make this state visible and straightforward to recover.

## Rejected alternatives

### Harden SQLite as the long-term catalogue

WAL, private cache and retry handling could materially improve the current system, but the product direction now includes multiple independent background writers plus interactive writes. Continuing to design around SQLite's single-writer constraint would keep concurrency coordination as a recurring application concern.

### Split authoritative state between SQLite and PostgreSQL

This would introduce cross-database consistency, synchronization and backup complexity without a compelling benefit. One authoritative relational catalogue is preferred.
