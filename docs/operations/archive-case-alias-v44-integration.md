# Case-only archive alias retirement: PostgreSQL v44 integration

This is the current-main integration of the previously tested but
intermediate-branch-only PR #836.

The earlier change used PostgreSQL schema version 43 for a dormant
archive_case_alias_retirements table. Since then main has introduced
**version 43 for lease-bound video-face publication receipts**. The
archive alias mapping is therefore allocated **version 44**, retaining
both migration histories in the correct order.

Version 44 adds an initially empty, reversible mapping table. It never
sets deleted_at_utc on existing assets and does not delete any records
or originals. Until the later service receives a reviewed operator
entrypoint, an existing production catalogue will have **zero active
retirements** and unchanged active-photo results.

When an active mapping exists (only in disposable tests at present):
- scanning a second-ingestion Windows path, including a third case
  variant, is routed to the surviving original asset's existing ID;
- a rescan will not re-activate the secondary asset, and mark-missing
  recognizes observations of the secondary spelling as observations
  of the survivor;
- original content is never hydrated merely to resolve the alias;
- source-move reconciliation and exact-duplicate inventories omit
  active retired aliases, without affecting distinct-path copies;
- restoration preserves both original and secondary revision-linked
  history and makes the secondary identity visible again.

The disposable test
PostgresArchiveCaseAliasRetirementMappingTests exercises the mapping,
missing-state behavior, history retention and restoration.

## Verify on Windows / PowerShell 7.6

~~~powershell
.\verify-postgres.ps1 -SkipContainerStart
~~~

**Not production apply-ready.** This PR is a correction of stacked
merge ancestry plus a schema-version conflict. Do not manually insert
retirement mappings or update assets.deleted_at_utc in
photoidentity_main. A guarded, tested service and complete active-face
admission checks are separate dependent PRs.
