# Case-only archive alias: bounded pilot preview (no Apply)

This operation prepares **one to five proposed logical retirements**, not
database mutations. It is the next stage after PostgreSQL v44 mapping,
single-pair transactional retirement, and active-face safeguards.

The earlier review identified 1,088 case-only path pairs and 333 provisional
retirement candidates. **Do not assume the 333 remain eligible**. Archives,
annotations and hashes can change. Re-run the two read-only production
audits against the intended catalogue before assembling a pilot.

## Preflight requirements

- Verify the current database is photoidentity_main and the installed
  schema is v44 (or separately review newer schema changes).
- Make and **restore-test** a current production backup in an isolated
  database before a later Apply. A backup file alone is not proof of a
  usable restore. Consult docs/operations/postgresql-operations.md and
  docs/operations/postgres-wi0209-backup-retention.md for supported
  backup/restore procedures.
- Confirm the review/identity workflows are not processing the same
  source while the eventual bounded write takes place.
- Keep private path/asset/revision identifiers in the ignored artifacts/
  directory. Share only aggregate console output.
- No service, API, CLI or operator command in this PR can retire anything.

## 1. Re-export the private production evidence (read-only)

PowerShell 7.6 on Windows, from the repository root. Use the previously
verified $containerId for the PostgreSQL service and authenticate as
you did for the earlier audit.

~~~powershell
$schema = & podman exec $containerId sh -c 'PGPASSWORD="$POSTGRES_PASSWORD" exec psql -h 127.0.0.1 -U "$POSTGRES_USER" -d photoidentity_main -X -q -tAc "SELECT max(version) FROM photo_identity_schema_migrations"'
if ($LASTEXITCODE -ne 0 -or ($schema -join '').Trim() -ne '44') {
    throw 'Expected the v44 production catalogue. Investigate before continuing.'
}

New-Item -ItemType Directory -Force '.\artifacts' | Out-Null
$sql = Get-Content '.\tools\archive-case-alias-retirement-evidence.sql' -Raw
$result = $sql | & podman exec -i $containerId sh -c 'PGPASSWORD="$POSTGRES_PASSWORD" exec psql -h 127.0.0.1 -U "$POSTGRES_USER" -d photoidentity_main -X -q -v ON_ERROR_STOP=1'
if ($LASTEXITCODE -ne 0) { throw 'Read-only retirement evidence audit failed.' }
$result | Set-Content '.\artifacts\archive-case-retirement-evidence-fresh.csv' -Encoding utf8

$sql = Get-Content '.\tools\archive-case-alias-effective-state-audit.sql' -Raw
$result = $sql | & podman exec -i $containerId sh -c 'PGPASSWORD="$POSTGRES_PASSWORD" exec psql -h 127.0.0.1 -U "$POSTGRES_USER" -d photoidentity_main -X -q -v ON_ERROR_STOP=1'
if ($LASTEXITCODE -ne 0) { throw 'Read-only effective-state audit failed.' }
$result | Set-Content '.\artifacts\archive-case-effective-state-fresh.csv' -Encoding utf8
~~~

Each SQL file begins a READ ONLY transaction. Neither imports data nor
opens/hydrates archive originals.

## 2. Test the offline pilot preview and generate at most five proposals

~~~powershell
pwsh -NoProfile -File '.\tests\PowerShell\archive-case-alias-pilot-preview.tests.ps1'

.\tools\new-archive-case-alias-pilot-preview.ps1 -RetirementEvidenceCsv '.\artifacts\archive-case-retirement-evidence-fresh.csv' -EffectiveStateCsv '.\artifacts\archive-case-effective-state-fresh.csv' -MaxPairs 5 -OutputDirectory '.\artifacts\archive-case-pilot'
~~~

The pilot tool runs the authoritative candidate planner on the **entire
fresh audit**, then selects up to five candidates deterministically.
When possible it includes exactly one secondary copy that has automatic
face-review history, to exercise its preservation in a later pilot.
All remaining proposals preferentially have no automatic face reviews.
A candidate with any protected secondary human/history reference,
effective annotation conflict, inconsistent source identity or
verification evidence remains held.

The generated artifacts are private:
- artifacts/archive-case-pilot/revalidated-input/retirement-candidates-private.csv
- artifacts/archive-case-pilot/pilot-preview-private.csv
- artifacts/archive-case-pilot/pilot-preview-metadata.json

The metadata includes SHA-256 hashes of both audit inputs and the
private preview manifest. The preview file itself contains pinned
asset IDs, revision IDs, source paths, creation timestamps, hash and
size for a later *transactional recheck*. File hashes detect changes
to the proposed preview; they do **not** prove that the database has
not changed.

The preview tool refuses CSVs older than four hours by default (file
timestamps are only a freshness hint, not an authority), and it
never accepts a prior candidate CSV as an input. It has NO Apply option.
Every selected row is tagged ApplyEnabled=false, ApprovalRequired=true,
DeleteSourceOriginal=false, DeleteDatabaseRows=false.

## 3. Read-only post-check baseline

~~~powershell
$sql = Get-Content '.\tools\archive-case-alias-retirement-postcheck.sql' -Raw
$result = $sql | & podman exec -i $containerId sh -c 'PGPASSWORD="$POSTGRES_PASSWORD" exec psql -h 127.0.0.1 -U "$POSTGRES_USER" -d photoidentity_main -X -q -v ON_ERROR_STOP=1'
if ($LASTEXITCODE -ne 0) { throw 'Read-only retirement postcheck failed.' }
$result | ConvertFrom-Csv | Format-List
~~~

It reports aggregate active mapping counts, invalid survivors/secondaries,
source-key inconsistencies and retained secondary revision/review counts.
Because it does not mutate anything, the baseline should show zero new
retirements before an approved pilot.

## Not yet approved for production Apply

After reviewing the aggregate preview and demonstrating an isolated
restore of the production backup, a separate PR must implement a bounded
operator-approved runner. It must re-verify the pinning fields, source,
both assets and protected revision-linked history inside the database
transaction for **every** pair, save a durable action/restore receipt,
prove pre/post conditions, and stop on the first unexpected result.
No CSV alone authorizes RetireAsync, and this preview deliberately
does not call that method.

Never use source-copy exclusion, purge or filesystem deletion to remove
these aliases. Retiring the secondary identity should retain all of its
historical face observations and automatic decisions for restoration.
