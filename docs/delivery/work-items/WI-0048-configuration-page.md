---
id: WI-0048
title: Add an operator configuration page
milestone: M18
status_source: ../status/work-items.yaml
depends_on: [WI-0041, WI-0043]
related_adrs: []
affected_modules: [PhotoIdentity.Persistence.Sqlite, PhotoIdentity.Api, PhotoIdentity.Web]
---

# WI-0048: Add an operator configuration page

## Objective

Consolidate the settings that affect normal archive processing and identity matching into a clear web configuration page.

## Why

Archive coverage and matching policy should not require remembering command syntax or searching across unrelated pages as the application becomes the primary operator surface.

## In scope

- Show the configured permanent archive source and root-relative included folder coverage without exposing unnecessary full source paths to ordinary browser responses.
- Allow supported archive folders/coverage to be added or adjusted using the existing source-identity and normalization rules.
- Configure High, Medium and Low suggestion score boundaries from WI-0043.
- Toggle canonical High auto-assignment on/off.
- Validate settings before saving and show which changes affect only future matching/processing runs.
- Provide settings APIs/storage suitable for the simplified application shell.

## Out of scope

- Selecting or changing the SQLite catalogue database path from the running application.
- Silently deleting catalogue data when archive coverage is changed.
- General model evaluation/comparison controls beyond linking to advanced tooling.

## Acceptance criteria

- [ ] The settings page shows current archive coverage and matching confidence policy.
- [ ] Supported archive coverage changes preserve the permanent source identity and existing catalogue assets.
- [ ] Confidence boundaries are validated and persisted.
- [ ] Auto-assignment can be enabled and disabled explicitly.
- [ ] The UI explains that policy changes affect future regeneration and do not retroactively rewrite canonical assignments.
- [ ] Catalogue-path configuration is not introduced in this work item.

## Verification requirements

Automated configuration validation/persistence coverage and human verification against the permanent-archive workflow.

## Completion notes

- Files changed:
- Trade-offs:
- Deferred work: catalogue path selection
- Commands run:
