---
id: WI-0166
title: Reduce included-folder synchronization time at archive scale
milestone: M33
status_source: ../status/work-items.yaml
depends_on: [WI-0079, WI-0099, WI-0101]
related_adrs: [ADR-0007]
affected_modules: [PhotoIdentity.Api, PhotoIdentity.Core, PhotoIdentity.Persistence.Postgres, PhotoIdentity.Source.Local, PhotoIdentity.Integration.Tests, docs]
---

# WI-0166: Reduce included-folder synchronization time at archive scale

## Priority

**High.** Folder synchronization is durable background work now, but it still takes too long on the maintained archive. Moving work out of the browser request solved cancellation and operator responsiveness; it did not solve the underlying wall-clock cost.

## Problem statement

**Sync included folders** remains slow as the catalogue and configured archive coverage grow. The operator can leave the page while synchronization runs, but new or changed photos are still delayed by the total scan duration and repeated synchronization consumes local I/O and database capacity for too long.

WI-0079 measured and corrected the earlier SQLite-era per-file transaction and unconditional hashing costs. The current production catalogue is PostgreSQL-only and synchronization runs through durable background orchestration, so its old measurements are historical evidence rather than a current performance explanation. This item must establish the present cost shape before selecting a correction.

## Objective

Measure the current PostgreSQL synchronization path on representative archive scale, identify avoidable work in no-change and small-change runs, and implement a safe correction that materially reduces elapsed time while preserving complete coverage reconciliation and immutable-revision guarantees.

Background execution, a longer timeout, faster polling, or less visible progress is not sufficient evidence of improvement. The primary outcome is lower server-side synchronization wall-clock time.

## Scope

- Trace the current browser-to-background-worker path and the PostgreSQL/local-source synchronization pipeline.
- Capture an end-to-end baseline on the maintained catalogue for:
  - a no-change repeat synchronization;
  - a small-change run after adding or changing a bounded folder/file set; and
  - where practical, a narrower representative fixture suitable for repeatable automated performance checks.
- Record privacy-safe aggregate counts and elapsed time for the major phases, including:
  - included roots, directories and supported files enumerated;
  - OneDrive/local availability and filesystem metadata checks;
  - retained-baseline reads and unchanged-revision reuse decisions;
  - files and bytes content-hashed;
  - PostgreSQL reads, writes, round trips and transaction time at a useful aggregate level;
  - new, changed, unchanged, missing and reconciled-move results;
  - missing-item/move reconciliation and final status construction; and
  - total queued, running and wall-clock duration.
- Determine whether overlapping coverage, repeated full-table/scope queries, N+1 database access, per-file persistence, unnecessary writes, repeated sorting/materialization, hashing, or source enumeration dominates current runtime.
- Select and implement the smallest correction supported by the measurements. Candidate techniques may include set-oriented/batched PostgreSQL operations, preloaded scope baselines, bounded producer/consumer pipelining, or avoiding provably redundant work; none is preselected by this item.
- Preserve useful progress/status reporting so a genuinely long scan remains understandable after optimization.
- Add focused correctness regressions and a repeatable performance guard appropriate to the chosen correction.
- Document the before/after measurements and the agreed archive-scale performance expectation.

## Safety and architecture constraints

- Continue revisiting all configured included coverage for new, changed, missing and newly available files as required by ADR-0007.
- Preserve SHA-256 immutable revision authority and the existing verified-baseline reuse rules. Do not trust a changed, reappearing, unverified or incomplete-baseline file merely to reduce runtime.
- Never hydrate online-only originals during synchronization.
- Preserve normalized parent/child coverage behavior, move reconciliation, missing-item handling, idempotency and duplicate prevention.
- Keep original photos read-only and do not emit source paths, filenames, hashes or other private archive details in diagnostics or committed evidence.
- PostgreSQL remains the sole writable production catalogue.
- Do not make concurrent scans mutate the same archive source. Any internal parallelism must be bounded, cancellation-safe and proven not to reorder or lose authoritative reconciliation state.
- Do not skip a complete coverage scan solely on directory timestamps or another signal that cannot reliably represent descendant changes.

## Acceptance criteria

- [ ] Current no-change and small-change archive-scale baselines identify the dominant synchronization phases using privacy-safe aggregate evidence.
- [ ] An explicit, maintainer-reviewed performance target is recorded before the correction is accepted.
- [ ] The implemented correction materially reduces end-to-end server-side synchronization time against the same representative baseline; merely running in the background does not satisfy this criterion.
- [ ] A no-change repeat avoids unnecessary hashing and catalogue writes while still checking all included coverage for availability, additions, changes, moves and removals.
- [x] A small-change run discovers and persists the intended changes without reprocessing unrelated immutable revisions.
- [x] New, metadata-changed, reappearing, unverified and incomplete-baseline local files still receive the required content verification.
- [x] Online-only items remain unhydrated, and availability changes remain visible after synchronization.
- [x] Parent-folder expansion, missing-item reconciliation, source-move reconciliation, idempotency and duplicate prevention retain focused regression coverage.
- [x] Background synchronization remains durable across browser navigation/disconnection and continues to exclude conflicting archive advancement or duplicate sync runs.
- [x] Progress/status remains truthful for queued, active, completed and failed synchronization.
- [x] A repeatable automated performance guard or query/operation-count guard covers the corrected hot path without making ordinary CI depend on the private archive.
- [ ] Before/after maintainer measurements use the same catalogue coverage and record aggregate counts, timings and the application commit/package tested.
- [x] `PhotoIdentity.Docs validate` and `generate --check` pass.

## Implementation progress — 2026-09-27

The current PostgreSQL trace found two catalogue-size-proportional database boundaries before content verification was considered:

1. The scanner called `RecordObservedIfExcludedAsync` for every enumerated item. The PostgreSQL implementation opened a connection and attempted one exclusion `UPDATE` per file, including the overwhelmingly common non-excluded case.
2. `PostgresArchiveSourceScanBatchRepository.RecordBatchAsync` used one transaction but still awaited separate asset, availability and observation commands for every write, plus revision insert/read commands for every hashed file.

For 17,892 enumerated photos, a metadata-stable no-change run therefore had an approximate lower bound of 71,568 per-file PostgreSQL command executions before the fixed baseline, source and missing-reconciliation operations: one exclusion command plus three persistence commands per item. Hashed files added two more commands each. This explains why moving the operation into a background worker did not make it finish quickly.

The correction keeps the existing scan and verification decisions but changes the database boundary:

- the scanner enumerates one included scope, performs one set-oriented exclusion observation/check, and never opens or persists returned excluded locators;
- PostgreSQL stages the entire included-folder write set once, snapshots current baseline/deletion state inside the serializable transaction, and uses set-oriented asset, availability, revision and observation upserts;
- result rows retain input order so new/unchanged revision counts and verification-state summaries keep their existing contract;
- metadata-stable verified files remain hash-free, while new, changed, reappearing, unverified and incomplete-baseline files retain the authoritative SHA-256 path; and
- privacy-safe `WI-0166 sync diagnostics` now report excluded items, exclusion batches/time and persistence batches/time alongside the existing enumeration, baseline, hashing, observation and missing-reconciliation values.

The corrected database-command shape is fixed per non-empty included folder rather than proportional to file count: one exclusion command and eight scan-persistence commands, plus the existing baseline and missing-reconciliation commands. PostgreSQL still updates authoritative last-seen, availability and observation timestamps for all observed non-excluded items, but it does so with set-oriented statements rather than client/server N+1 calls.

The non-private operation-count guard feeds 100 online-only items through the provider-neutral scanner and proves one exclusion batch, one persistence batch, 97 writes for three excluded locators, and zero per-item exclusion calls. Focused live PostgreSQL integration tests exercise parent expansion, stable reuse, new files, exclusions, missing paths, exact moves, ambiguous duplicates, online-only paths and idempotent repeats.

### Proposed maintainer performance target

For acceptance, repeat the same maintained-archive no-change and bounded small-change runs before and after this correction. The proposed target is both:

- at least a 75% reduction in end-to-end synchronization wall-clock time from the pre-change package; and
- no more than 30 seconds for a metadata-stable no-change synchronization of the current approximately 17,892-photo coverage on the same maintainer hardware.

The maintainer must confirm or adjust this target before WI-0166 is completed. Until those before/after measurements exist, the structural N+1 correction is implemented and verified but archive-scale wall-clock acceptance remains open.

### Automated evidence

- `dotnet build src/PhotoIdentity.Api/PhotoIdentity.Api.csproj --no-restore`: passed with zero warnings.
- `ArchiveSourceCatalogueScannerTests.Large_scan_batches_exclusion_lookup_and_persistence_once`: passed.
- Focused live PostgreSQL synchronization/exclusion/move suite: 9/9 passed.
- `verify-postgres.ps1`: Release solution build passed; PostgreSQL persistence acceptance 53/53 passed; runtime/composition acceptance 6/6 passed.

## Verification plan

1. Capture a current no-change synchronization baseline on the maintained archive, including total elapsed time and per-phase aggregate diagnostics.
2. Add or modify a small bounded source set, synchronize again and capture the same measurements.
3. Review the evidence, record the dominant cost and agree an explicit performance target before finalizing the implementation direction.
4. Add focused tests for the selected optimization and for unchanged, changed, reappearing, online-only, missing and moved assets.
5. Add a repeatable non-private performance or operation-count fixture that would fail if the corrected hot path regresses to avoidable per-file work.
6. Repeat the no-change and small-change maintainer runs on the same coverage and compare before/after wall-clock and phase timings.
7. Navigate away from the Archive page during synchronization, return, and confirm the durable run and truthful progress remain intact.
8. Run relevant build/integration tests, the live PostgreSQL verification where required, and the documentation validation/generation checks.

## Source finding

On 2026-09-27 the maintainer reported that synchronizing folders remains too slow even though it now runs in the background. This item follows WI-0079 rather than reopening it: the earlier work addressed the SQLite-era hot path and browser-request lifetime, while this work targets current PostgreSQL-era end-to-end throughput.
