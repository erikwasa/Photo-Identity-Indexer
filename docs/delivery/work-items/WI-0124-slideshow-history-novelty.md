---
id: WI-0124
title: Track slideshow exposure and novelty for Creative Collections
milestone: M26
status_source: ../status/work-items.yaml
depends_on: [WI-0120]
related_adrs: []
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Api, PhotoIdentity.Web, PhotoIdentity.Persistence.Sqlite, PhotoIdentity.Persistence.Postgres, PhotoIdentity.Cli, PhotoIdentity.Core.Tests, PhotoIdentity.Integration.Tests, PhotoIdentity.Persistence.Tests, docs]
---

# WI-0124: Track slideshow exposure and novelty for Creative Collections

## Objective

Record lightweight presentation history so repeated Creative Collections can favor useful photos that have not been shown recently.

## Why

A deterministic selector can otherwise converge on the same strongest photos every time. Presentation history can add freshness without redefining photo quality or canonical archive metadata.

## In scope

- Define presentation-history state such as last shown time and show count for immutable revisions.
- Record exposure only after a photo is actually presented under a defined slideshow/session rule rather than merely selected in a candidate set.
- Add an optional novelty preference that penalizes recently/frequently shown photos while respecting anchors, explicit presentation preferences and target count.
- Support discovery concepts such as rarely shown or not shown recently without changing ordinary Smart Collection membership.
- Bound history storage/query cost at archive scale.

## Out of scope

- Inferring dislike from navigation speed or accidental skips.
- Cross-user personalization/authentication.
- Treating show history as canonical photographic metadata.

## Acceptance criteria

- [x] Slideshow exposure is persisted as presentation state separate from archive/identity facts.
- [x] The application can report last-shown/show-count signals for eligible revisions without exposing private source paths.
- [x] An optional novelty policy can produce a materially different valid selection while preserving hard eligibility and presentation-preference rules.
- [x] History updates are idempotent/restart-safe under the chosen session semantics.
- [x] Automated tests cover repeated sessions, unseen photos and history-disabled behavior.

## Verification requirements

Automated persistence/selection tests plus maintainer comparison of repeated Creative Collection runs with novelty disabled and enabled.

## Completion notes

- Files changed: provider-neutral slideshow exposure contracts, SQLite/PostgreSQL exposure repositories and migrations, Creative recipe novelty setting, presentation callback/API recording, selector novelty scoring, Creative preview history evidence, workspace freshness control, migration verification and automated tests.
- Session semantics: each slideshow page load creates a new session identifier. A revision is recorded only after the presentation component reports it displayed successfully; repeated callbacks or revisits of that revision within the same page session are idempotent, while a later slideshow session may record it again.
- Novelty policy: disabled by default. `m26-slideshow-novelty-balanced-v1` rewards unseen photos by +100, applies bounded recency penalties of -100/-60/-25 for <=7/30/180 days, and a -5-per-session frequency penalty capped at -40. `Avoid` remains a hard exclusion and explicit `Prefer` remains stronger at +220.
- Privacy/state boundary: exposure rows store session ID, immutable revision ID, Smart Collection ID, Creative/classic flag and server timestamp only. No source paths, filenames or media URLs are persisted.
- Scale boundary: storage admits at most one row per revision per slideshow session; selection reads indexed aggregate history only for the current candidate revisions in a batch.
- Schema: SQLite advances to 19 and PostgreSQL to 26. Offline catalogue migration includes exposure history in critical count verification.
- Maintainer verification: completed on 2026-09-18. Show counts updated for actually displayed photos, freshness-disabled selection remained stable, and enabling freshness produced a sensible different selection with more unseen photos.
- Deferred work: future novelty tuning should be based on repeated-use evidence rather than exposing raw scoring controls.
- Commands run: PR 366 CI covered build, selector, integration, provider, documentation, launcher and package verification surfaces; follow-up PostgreSQL migration fixes in PRs 367 and 369 repaired replay-safe schema initialization and SQLite-to-PostgreSQL identity preservation found by live acceptance.
