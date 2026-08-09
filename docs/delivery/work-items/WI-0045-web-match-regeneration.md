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

- [ ] The application can detect and display stale suggestion state after identity evidence changes.
- [ ] Regeneration can be started and observed from the web UI.
- [ ] Long runs expose durable progress without holding one browser request open for the entire operation.
- [ ] A second conflicting regeneration cannot run concurrently for the same exact model/policy.
- [ ] Completion refreshes suggestion/group state and reports automatic assignments separately when enabled.
- [ ] Existing CLI regeneration remains valid for diagnostics/automation.

## Verification requirements

Automated service/restart/concurrency coverage plus human verification on a representative local catalogue while continuing to use the UI during regeneration where supported.

## Completion notes

- Files changed:
- Trade-offs:
- Deferred work:
- Commands run:
