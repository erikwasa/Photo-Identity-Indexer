# Reproducing the private case-alias comparison audits


## Restored main-branch read-only comparison tools

The earlier stacked audit PRs #830/#832 were merged into intermediate
development branches, not carried through to main. The local private CSVs
remain valid, but a fresh checkout could not regenerate their inputs.
This follow-up restores the tested source files and CI checks **without**
changing the production database or revising any manual annotations.

The restored tools are:
- tools/preview-archive-case-reconciliation.ps1 — first read-only preview
  from tools/archive-case-alias-audit.sql;
- tools/archive-case-alias-effective-state-audit.sql — read-only PostgreSQL
  export of effective tag IDs, manual person IDs, Place tag/provenance,
  confirmed face person IDs, and reviewed-face counts;
- tools/summarize-archive-case-effective-state.ps1 — strict offline
  comparison that verifies both assets in all pairs match the history audit.

Source-free synthetic checks can be run directly:

~~~powershell
pwsh -NoProfile -File .\tests\PowerShell\archive-case-reconciliation-preview.tests.ps1
pwsh -NoProfile -File .\tests\PowerShell\archive-case-effective-state.tests.ps1
~~~

To reproduce the private effective-state CSV on the previously verified
Podman PostgreSQL service, from PowerShell 7.6:

~~~powershell
$sql = Get-Content '.\tools\archive-case-alias-effective-state-audit.sql' -Raw
$result = $sql | & podman exec -i $containerId sh -c 'PGPASSWORD="$POSTGRES_PASSWORD" exec psql -h 127.0.0.1 -U "$POSTGRES_USER" -d photoidentity_main -X -q -v ON_ERROR_STOP=1'
if ($LASTEXITCODE -ne 0) { throw 'Read-only effective-state audit failed.' }
$result | Set-Content '.\artifacts\archive-case-effective-state.csv' -Encoding utf8

.\tools\summarize-archive-case-effective-state.ps1 -AuditCsv '.\artifacts\archive-case-alias-audit.csv' -EffectiveStateCsv '.\artifacts\archive-case-effective-state.csv' -OutputDirectory '.\artifacts\archive-case-effective-state'
~~~

Keep the resulting CSVs private: they contain archive paths and asset
identifiers. The scripts perform no database modifications or source reads.
