---
id: WI-0045
title: Regenerate identity matches from the web application
milestone: M17
status_source: ../status/work-items.yaml
depends_on: [WI-0016, WI-0043]
related_adrs: []
affected_modules: [PhotoIdentity.Persistence.Sqlite, PhotoIdentity.Api, PhotoIdentity.Web]
---

# WI-0045: Regenerate identity matches from the web application

## Objective

Make suggestion regeneration a normal browser operation with visible state and progress rather than a CLI step the operator must remember and run separately.

## Why

Adding people and confirming or correcting faces changes exemplar evidence. The normal review loop should make it obvious when suggestions are stale and make regeneration easy without leaving the application.

## In scope

- Expose the existing exact-model regeneration capability through an application service/API rather than spawning the CLI.
- Show when current suggestions predate identity-affecting review changes and should be regenerated.
- Provide an explicit Regenerate action from the review workflow.
- Refactor long regeneration work so the browser can display durable progress/completion and avoid one opaque long-running request/transaction.
- Prevent concurrent duplicate regeneration for the same exact model/policy.
- Report target, suggested, automatically assigned and error counts.
- Apply WI-0043 canonical High auto-assignment policy when enabled.
- Refresh the review queue and confidence-group counts after completion.

## Out of scope

- Automatically regenerating after every individual review click.
- Scheduling unattended periodic regeneration.

## Acceptance criteria

- [x] The application can detect and display stale suggestion state after identity evidence changes.
- [x] Regeneration can be started and observed from the web UI.
- [x] Long runs expose durable progress without holding one browser request open for the entire operation.
- [x] A second conflicting regeneration cannot run concurrently for the same exact model/policy.
- [x] Completion refreshes suggestion/group state and reports automatic assignments separately when enabled.
- [x] Existing CLI regeneration remains valid for diagnostics/automation.

## Verification requirements

Automated service/restart/concurrency coverage plus human verification on a representative local catalogue while continuing to use the UI during regeneration where supported. GitHub Actions run `31444575754` passed restore, Release build, the full automated test suite, living/generated documentation checks, review-application smoke verification and Windows mixed-media verification. Human verification completed on 2026-08-11 as part of the milestone-wide M17 local review.

## Completion notes

- Files changed: durable exact-model regeneration run/target persistence; evidence-version and model-revision queries; short-transaction per-target scoring; API start/state endpoints; background hosted worker; browser Regenerate matches workspace and navigation; architecture documentation; and focused integration/application regression coverage.
- Behavior: the browser enqueues regeneration and polls durable state rather than holding one request open. A run snapshots the exact model revision, policy version and identity evidence, rejects a duplicate active run for that exact model, reclaims interrupted running work after restart, and becomes stale/failed rather than silently continuing if identity evidence or policy changes underneath it.
- Auto-assignment: all targets are scored against one fixed exemplar snapshot before WI-0043 automatic assignment is applied. The run accounts for its own expected automatic audit writes so successful automatic assignments do not immediately make the completed run appear stale.
- CLI compatibility: the existing CLI matcher remains unchanged as a diagnostic/automation path.
- Verification: PR #123 merged into `m17`; final code-head GitHub Actions run `31444575754` completed successfully, and the maintainer accepted browser regeneration, progress/current-stale feedback and normal UI use during regeneration during milestone-wide M17 verification on 2026-08-11.
- Deferred work: unattended scheduled regeneration remains outside this work item by design.
