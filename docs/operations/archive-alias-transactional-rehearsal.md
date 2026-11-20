# Transactional archive alias retire/restore rehearsal

This is the migration-v44 replacement for the earlier stacked PR #838.
It depends on the current-main-compatible v44 retirement mapping.

PostgresArchiveCaseAliasRetirementService contains an internal
single-pair retirement and restore method, deliberately **not registered
in the API, CLI, UI or background services**.

Retirement:
- locks the archive source and the two selected assets within a
  SERIALIZABLE PostgreSQL transaction;
- re-reads and pins both exact source keys, older/newer asset IDs,
  creation timestamps, current verified revision IDs, SHA-256 and size;
- requires exactly one case-normalized pair, no active source exclusion,
  no human/unknown secondary face review or person labels, and no
  protected secondary manual/collection/featured-face references;
- requires effective current tag IDs, person IDs, Place tag/provenance,
  and confirmed face identities to agree;
- marks the later asset logically missing and inserts a durable
  retirement mapping without deleting any asset, revision, annotation,
  face or source original.

A repeated identical retirement is idempotent. Restore stamps the
restoration event and restores active visibility transactionally.
Re-retirement after a completed restore is refused so the historical
mapping is not silently overwritten.

The disposable PostgreSQL regression test covers stale evidence,
ambiguous third-capitalization aliases, manual face-review blockers,
current tag differences, retained original tag history and secondary
automatic reviews, idempotence, and restoration.

Verification (Windows PowerShell 7.6):

~~~powershell
.\verify-postgres.ps1 -SkipContainerStart
~~~

This service is a rehearsal only. No production retirement operations
are authorized. All active photo, face and derivative admission paths,
restoration procedures and an explicit bounded operator approval
workflow must be verified before exposing an Apply operation.
