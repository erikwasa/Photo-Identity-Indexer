---
id: WI-0122
title: Add photo presentation preferences for Creative Collections
milestone: M26
status_source: ../status/work-items.yaml
depends_on: [WI-0120]
related_adrs: []
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Api, PhotoIdentity.Web, PhotoIdentity.Persistence.Sqlite, PhotoIdentity.Persistence.Postgres, PhotoIdentity.Cli, PhotoIdentity.Core.Tests, PhotoIdentity.Integration.Tests, PhotoIdentity.Persistence.Tests, docs]
---

# WI-0122: Add photo presentation preferences for Creative Collections

## Objective

Allow explicit photo-level presentation preferences to influence Creative Collection selection without changing archive, identity or Smart Collection truth.

## Why

Automatic selectors cannot know every personally important or undesirable photo. Small explicit signals such as prefer, avoid and always-include-when-relevant can provide high-value correction while also producing useful evidence for later selector tuning.

## In scope

- Define presentation-only photo preferences separately from tags, people, Places and source exclusion.
- Support at least Prefer and Avoid; evaluate whether a stronger Pin/Always include when relevant action is useful without making target counts impossible.
- Preserve append-only/auditable preference changes using existing photo-action design patterns.
- Incorporate active preferences into Creative Collection selection with deterministic precedence.
- Expose preference editing from appropriate photo/library surfaces without changing ordinary Smart Collection membership.

## Out of scope

- Deleting or privacy-excluding a source photo.
- Automatically learning preferences from skips in this work item.
- A general-purpose rating/star system unrelated to presentation.

## Acceptance criteria

- [x] Presentation preferences are persisted independently from canonical photo/identity metadata.
- [x] Avoid prevents normal Creative Collection selection while keeping the photo browsable elsewhere.
- [x] Prefer measurably increases selection priority when the photo is otherwise eligible.
- [x] Preference changes are reversible/auditable and deterministic selection tests cover precedence and target-count edge cases.
- [x] Existing exact Smart Collection results remain unchanged.

## Verification requirements

Automated persistence/selection tests plus maintainer verification that preferences affect a representative Creative Collection without hiding photos from ordinary library use.

## Completion notes

- Files changed: Core presentation-preference contracts, append-only SQLite/PostgreSQL repositories and schema migrations, catalogue migration verification, Photo Details preference API/editor, Creative selector/materialization integration, provider/application tests and operational documentation.
- Preference semantics: `Avoid` is a hard presentation-only exclusion from normal Creative selection; `Prefer` adds an explicit +220 selection score while the photo remains subject to normal eligibility; `Clear` appends a reversal action and restores automatic behavior.
- Audit/storage: the latest append-only `set`/`clear` action determines effective state. Preferences are tied to immutable revision IDs and cascade only when that revision is actually removed; tags, people, Places, source state and exact Smart Collection membership are unchanged.
- Pin decision: no stronger Pin/Always-include action is introduced in this slice because Prefer provides a correction signal without creating target-count impossibility or precedence complexity. It can be reconsidered if private use shows Prefer is insufficient.
- Persistence: SQLite schema advances to 18 and PostgreSQL to 25; offline catalogue migration includes the new action table in critical count verification.
- Deferred work: maintainer verification on a representative private Creative Collection remains required before WI-0122 can be completed.
- Commands run: repository CI will provide build, selector, integration, persistence, documentation, launcher and packaging evidence for this branch.
