---
id: M24
title: PostgreSQL catalogue migration and archive-scale operation
status_source: ../status/milestones.yaml
depends_on: []
---

# M24: PostgreSQL catalogue migration and archive-scale operation

## Outcome

Photo Identity runs its authoritative catalogue on PostgreSQL, can migrate the maintainer's existing SQLite catalogue without losing identity/history, remains responsive while background workloads run, and can continue full-archive analysis toward a steady-state daily-update workflow.

The architectural decision is recorded in [ADR-0009](../../decisions/ADR-0009-postgresql-authoritative-catalogue.md).

## Delivery principles

- Progress toward completing the real archive is more important than comparative benchmark exercises.
- Preserve current semantics before introducing optional PostgreSQL-specific acceleration.
- Keep each migration slice independently testable and avoid a big-bang rewrite.
- Add low-overhead metrics to the application rather than requiring repeated manual workload comparisons.
- Keep the existing SQLite catalogue as a read-only rollback artifact until PostgreSQL cutover is accepted.
- Do not mix M22 functional acceptance work into this milestone; slideshow performance findings discovered during M22 acceptance are tracked here only when they are performance/scale concerns.
- Do not assume PostgreSQL alone fixes database-independent repeated hashing, file I/O, browser serving or UI work.

## Work items

- [WI-0097](../work-items/WI-0097-postgresql-runtime-foundation.md) - establish the local PostgreSQL/Podman runtime, configuration, migrations and health boundary.
- [WI-0098](../work-items/WI-0098-persistence-boundary-foundational-schema.md) - introduce a database-neutral persistence boundary and PostgreSQL foundational catalogue/processing schema.
- [WI-0099](../work-items/WI-0099-postgresql-archive-background-persistence.md) - migrate archive/background-processing persistence and make hosted services resilient under concurrent database activity.
- [WI-0100](../work-items/WI-0100-postgresql-review-identity-persistence.md) - migrate review, people, suggestion and identity-matching persistence.
- [WI-0101](../work-items/WI-0101-postgresql-library-remaining-persistence.md) - migrate library, metadata, Places, smart collections, slideshow, detector and remaining authoritative persistence.
- [WI-0102](../work-items/WI-0102-sqlite-postgresql-catalogue-migration.md) - build and verify repeatable SQLite-to-PostgreSQL migration, cutover and rollback.
- [WI-0103](../work-items/WI-0103-scalable-match-regeneration.md) - redesign match regeneration for bounded PostgreSQL-scale processing.
- [WI-0104](../work-items/WI-0104-operator-query-ui-performance.md) - correct review/gallery/settings query and rendering bottlenecks that remain database-independent.
- [WI-0105](../work-items/WI-0105-operational-metrics-observability.md) - add low-overhead catalogue/background-work metrics and failure diagnostics.
- [WI-0106](../work-items/WI-0106-postgresql-operations-and-archive-catchup.md) - operationalize backup/startup/recovery and resume full-archive processing through steady-state readiness.
- [WI-0108](../work-items/WI-0108-slideshow-performance.md) - remove slideshow-library, startup and image-transition latency bottlenecks, including database-independent repeated original verification.

## Delivery sequence

1. WI-0097 creates the PostgreSQL runtime and migration foundation.
2. WI-0098 establishes the abstraction/schema needed by all subsequent slices.
3. WI-0099 through WI-0101 move authoritative runtime domains to PostgreSQL.
4. WI-0102 migrates the existing catalogue and performs controlled cutover.
5. WI-0103, WI-0104 and WI-0108 remove known algorithm/query/UI/playback scaling defects on the migrated catalogue and surrounding runtime.
6. WI-0105 adds durable low-cost observability across the migrated runtime; WI-0108 may add narrowly scoped slideshow timing evidence where needed for diagnosis.
7. WI-0106 proves backup/recovery, normal startup and sustained archive catch-up, then hands off to smaller daily updates.

WI-0103 through WI-0105 may begin before final cutover when dependencies allow, but must not block safe migration progress unnecessarily. WI-0108 should optimize the final PostgreSQL-backed slideshow path rather than spending effort on SQLite-only query shapes, while database-independent serving/verification findings may be investigated earlier.

## Slideshow performance findings carried into M24

Real-phone M22 acceptance on 2026-09-02 established the following performance baseline for WI-0108:

- loading saved Smart Collections on `/slideshows` is materially slow;
- starting an already-prepared one-photo slideshow takes roughly 20 seconds before the image is shown, and reopening it immediately remains slow;
- image-to-image loading is materially slow and appears to increase as playback advances.

Known implementation hypotheses include expensive Smart Collection snapshot/current-state evaluation and repeated full-file SHA-256 verification when already-local originals are served. These hypotheses must be measured and corrected without weakening immutable revision safety. Comparative SQLite/PostgreSQL benchmark runs are not required.

## Exit criteria

- Normal application startup uses PostgreSQL as the authoritative catalogue.
- The maintainer's existing SQLite catalogue can be migrated repeatably with stable identifiers, relationships and review/identity history preserved.
- No normal API or hosted-service runtime path depends on SQLite authoritative writes.
- A database lock/contention event cannot terminate the whole application.
- Match regeneration no longer reloads invariant evidence for every target and progresses in bounded resumable batches.
- Settings, Face Gallery and common review interactions no longer perform catalogue-size-dependent work that blocks the whole page/action unnecessarily.
- The slideshow library, slideshow startup and repeated image navigation no longer exhibit the identified avoidable catalogue-scale/file-verification latency; an unchanged prepared original is not fully re-hashed on every display.
- Low-overhead metrics expose background throughput, failures and key API/database/slideshow timing without storing personal filenames, image content or embeddings.
- PostgreSQL startup, persistent storage, backup, restore and upgrade procedures are documented for the Podman/WSL2 operator environment.
- The real archive can continue analysis for long-running catch-up without the prior host-shutdown/contention failure mode.
- After catch-up, a small set of newly added photos can be synchronized, analyzed, enriched and reviewed without requiring a full-archive maintenance cycle.

## Risks

- A partial persistence abstraction can accidentally create SQLite/PostgreSQL semantic divergence.
- Migration defects could damage review/identity history unless IDs, row counts, foreign keys and critical invariants are validated before cutover.
- Running two writable authoritative catalogues during migration would create split-brain risk; cutover must enforce one writer of record.
- PostgreSQL removes the single-writer ceiling but does not fix inefficient application algorithms automatically.
- Container lifecycle/storage mistakes can make a local database appear unavailable; operator diagnostics and backups must be first-class.
