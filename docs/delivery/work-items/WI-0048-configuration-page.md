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

- [x] The settings page shows current archive coverage and matching confidence policy.
- [x] Supported archive coverage changes preserve the permanent source identity and existing catalogue assets.
- [x] Confidence boundaries are validated and persisted.
- [x] Auto-assignment can be enabled and disabled explicitly.
- [x] The UI explains that policy changes affect future regeneration and do not retroactively rewrite canonical assignments.
- [x] Catalogue-path configuration is not introduced in this work item.

## Verification requirements

Automated configuration validation/persistence coverage and human verification against the permanent-archive workflow.

## Completion notes

- Files changed:
  - `src/PhotoIdentity.Web/Pages/Settings.razor` adds the consolidated operator settings surface.
  - `src/PhotoIdentity.Web/Layout/MainLayout.razor` exposes the additive Settings route while existing deep links remain stable for the later WI-0046 navigation reorganization.
  - `src/PhotoIdentity.Web/ArchiveContracts.cs` and `src/PhotoIdentity.Api/ArchiveEndpoints.cs` add root-relative coverage replacement without returning the source root.
  - `src/PhotoIdentity.Persistence.Sqlite/SqliteArchiveCoverageRepository.cs` preserves the configured permanent source while replacing only normalized coverage rows.
  - `tests/PhotoIdentity.Integration.Tests/ArchiveApplicationTests.cs` verifies narrowing/collapsing coverage, root privacy and preservation of already catalogued assets.
- Trade-offs: the existing exact-model suggestion-policy storage/API remains authoritative rather than adding a duplicate application-settings store; existing `/archive` and `/suggestion-policy` routes remain available until WI-0046 reorganizes the shell.
- Deferred work: catalogue path selection; primary-navigation simplification remains WI-0046.
- Commands run: local repository execution was unavailable in this environment; branch validation is delegated to the repository GitHub Actions gate before review.
