---
id: WI-0204
title: Reversible opt-in omission of reviewed alternatives from one Creative Collection
milestone: M39
status_source: ../status/work-items.yaml
depends_on: [WI-0203]
related_adrs: []
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Api, PhotoIdentity.Web, PhotoIdentity.Persistence.Postgres, PhotoIdentity.Core.Tests, PhotoIdentity.Integration.Tests, docs]
---

# WI-0204: Reversible opt-in omission of reviewed alternatives from one Creative Collection

## Objective

Allow the operator to choose which visually similar candidate photos should be omitted **from one saved Creative Collection/slideshow**, without excluding those photos from the archive or another collection. Include an obvious restoration mechanism and opt-in behavior.

## Why

The first manual suggestion phase ([WI-0203](WI-0203-creative-collection-similarity-review.md)) works but **Prefer** and **Keep all** are annotations only and currently do not affect collection/slideshow selection. This item retains the remaining product goal previously captured outside the repository. Some v3 burst sets may combine visually different subgroups, so a set-wide automatic suppression decision would be unsafe.

## In scope

- One collection/recipe-specific, explicit **omit from this collection** choice for individually identified exact photo revisions; optionally choose a kept representative after inspecting the full review set.
- Default keeps all eligible photos. A separate operator action enables any omission. Do **not** reinterpret a stored `prefer-one` annotation as an omission.
- Persist locally or with bounded PostgreSQL metadata under stable recipe/revision identities, with versioning and migration/restore strategy. Restore omitted revisions from the UI at any time.
- Apply the omission only at Creative materialization/snapshot creation for that recipe, not to Smart Collection membership, Archive, Search, another recipe or an existing immutable slideshow snapshot.
- Correct stale-group, catalog-revision, recipe-delete/recreate and exclusion-aware behavior; clear provenance when current eligibility changes.
- Verify photo counts, selector diversity, insufficient non-omitted candidate fallbacks, cancellation, selection snapshots, persistence, and full rollback/revert.
- Pilot operator opt-in on realistic burst collections before any broader automation. Report aggregate usefulness, missed redundancy, false-positive grouping and restoration experience.

## Out of scope

- Automatic omission from all review-set members, Archive suppression, source deletion or local derivative purge.
- Automatic production suppression under M37's rejected thresholds; model-score-driven representative choice; unrelated quality rating.
- Recomputing/rewriting previously created immutable slideshow snapshots.

## Acceptance criteria

- [ ] Omission is explicitly enabled per saved Creative Collection, disabled by default.
- [ ] Only exact operator-selected alternatives are omitted from **future snapshots for that collection**; all other Photo Identity experiences remain unchanged.
- [ ] Restoring omitted photos is immediate, intuitive and durable; a reviewed group is never treated as mutual duplicate truth.
- [ ] Old annotations never silently become omissions; stale group/revision selections are rejected or safely reconciled.
- [ ] Deterministic tests cover two recipes with opposing preferences, changed membership, restarts and rollback.
- [ ] Maintainer A/B verifies actual snapshot impact and full reversibility on a private representative archive without original hydration.
- [ ] Any optional unattended suggestion application is separately gated by measured false-omission risk and maintainer authorization, not inferred from the original human-review pilot.

## Verification requirements

Core selector tests, API/persistence integration tests and maintained Windows side-by-side collection/slideshow comparison. Stop before automating any omission without separate policy acceptance.

## Completion notes

- Status: **proposed**; no implementation or automatic omission approval exists.
- Dependency: WI-0203 manual review acceptance.
- Original stakeholder contract originated 2026-10-21. Source of truth is now this repository document and its canonical status shard, not GitHub issues.
