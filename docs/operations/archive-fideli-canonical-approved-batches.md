# Fideli lowercase-canonical archive retirement — guarded batches

Status: implementation and disposable tests, NOT authorization for production Apply.

## Exact user-selected rule

For two photo assets under the same archive source where the relative
file keys match **exactly except** for the initial directory spelling:

- Keep the `fideli/` source-key asset, regardless of which asset was
  ingested first or which accumulated more automatic metadata.
- Soft-retire the `Fideli/` asset, keeping its row, verified revisions,
  face crops, automatic labels/actions and any other historical rows
  for audit and selective rollback.
- Both records must still be present and verified, have the SAME
  current SHA-256 and byte size, have exactly two records for their
  casefolded source key and no source privacy exclusion.
- Unique **explicit/human/unknown** history or protected references
  on the uppercase record still block Apply, so reconciliation can be
  designed separately. The latest assessment found ONE such exception.
- Differences in effective tags, places, manual people on the keeper
  or automatic face review on the uppercase record are NOT a reason
  to leave both copies visible.
- Other folders and duplicate groups spanning genuinely different
  relative filenames are OUT OF SCOPE.

## Intent cannot be confused with the older policy

The CLI preserves its old strict behavior by default. To use the new
method the manifest metadata must contain the exact string:

```json
"retirementPolicy": "fideli-lowercase-canonical-v1"
```

The operator must also pass the same explicit CLI argument:

```text
--retirement-policy fideli-lowercase-canonical-v1
```

No policy flag or mismatched metadata => the special method cannot
be selected. Legacy manifests remain legacy; the old 5-pair pilot
and its rollback receipts stay valid.

## 1. Tests without real media or production writes

Run on Windows PowerShell 7.6, with the PR branch checked out:

```powershell
pwsh -NoProfile -File '.\tests\PowerShell\archive-fideli-canonical-preview.tests.ps1'
.\verify-postgres.ps1 -SkipContainerStart
```

The synthetic test checks lowercase canonical selection, verified
hash/size evidence, explicit upper-action blocks, and up to four
separate 1..5 pair sealed batch previews. Disposable PostgreSQL
tests exercise the new serializable service with a keeper that is
newer than the secondary and carries distinct manual tags, plus
protected uppercase review rejection, preservation and restore.
The CLI integration test checks that a mismatched policy is rejected.

## 2. Re-export and stage read-only

After refreshing the TWO archive-case audit CSVs with the existing
`Export-ArchiveAudit` helper (no backup needed for read-only SQL):

```powershell
$session = .\tools\new-archive-fideli-canonical-batches.ps1 `
    -RetirementEvidenceCsv '.\artifacts\archive-case-retirement-evidence-fresh.csv' `
    -EffectiveStateCsv '.\artifacts\archive-case-effective-state-fresh.csv' `
    -ExpectedDatabaseSchemaVersion 45 -MaxBatches 4 -PairsPerBatch 5

$session | Select-Object CanonicalEligibleUnretired,
    UppercaseExplicitHistoryHeld, PairsStaged, BatchesStaged,
    RetirementPolicy, AutomaticallyApplied
$session.Batches | Select-Object Batch, Pairs, ManifestSha256
```

Use the actual DB schema version rather than hardcoding 45 if the
installed app has since migrated. The generator rejects stale audit
exports or an existing output directory. It creates a fresh private
eligibility assessment, then up to four disjoint manifests. It does
not open originals or write to PostgreSQL.

Preview batch-01:

```powershell
$b = $session.Batches[0]
$manifest = (Resolve-Path $b.PrivateManifestPath).Path
$metadata = (Resolve-Path $b.PrivateMetadataPath).Path
$sha = (Get-FileHash $manifest -Algorithm SHA256).Hash.ToLowerInvariant()
if ($sha -cne $b.ManifestSha256) { throw 'Manifest changed.' }

dotnet run --project '.\src\PhotoIdentity.Cli\PhotoIdentity.Cli.csproj' `
    --configuration Release -- archive alias-pilot --operation preview `
    --retirement-policy fideli-lowercase-canonical-v1 `
    --manifest $manifest --metadata $metadata `
    --postgres-connection-env PHOTOIDENTITY_POSTGRES_CONNECTION_STRING `
    --database photoidentity_main --max-pairs 5
if ($LASTEXITCODE -ne 0) { throw 'Read-only canonical preview failed.' }
```

## 3. New: single all-eligible atomic operation (preferred)

This is the requested ONE bulk-retirement operation. It does NOT change
production merely by merging the PR. Perform a separate local acceptance
and explicit operator authorization. No original OneDrive files, photo
revisions, or review rows are deleted; uppercase assets are soft-retired.

### A. Prepare the full manifest, read-only

After refreshing both evidence CSVs (the existing `Export-ArchiveAudit`
commands above), use the **current** installed schema version, currently 46:

```powershell
$session = .\tools\new-archive-fideli-canonical-batches.ps1 `
    -RetirementEvidenceCsv '.\artifacts\archive-case-retirement-evidence-fresh.csv' `
    -EffectiveStateCsv '.\artifacts\archive-case-effective-state-fresh.csv' `
    -ExpectedDatabaseSchemaVersion 46 -AllEligible

if ($session.BatchesStaged -ne 1 -or
    $session.PairsStaged -ne $session.CanonicalEligibleUnretired -or
    $session.UppercaseExplicitHistoryHeld -lt 1) {
    throw 'Full-set inventory is incomplete or missing its known protected exception'
}
$b = $session.Batches[0]
$manifest = (Resolve-Path $b.PrivateManifestPath).Path
$metadata = (Resolve-Path $b.PrivateMetadataPath).Path
$hash = (Get-FileHash -LiteralPath $manifest -Algorithm SHA256).Hash.ToLowerInvariant()
if ($hash -cne $b.ManifestSha256) { throw 'Pinned manifest has changed' }

$cli = '.\src\PhotoIdentity.Cli\PhotoIdentity.Cli.csproj'
$common = @('archive','alias-bulk',
    '--retirement-policy','fideli-lowercase-canonical-v1',
    '--manifest',$manifest,'--metadata',$metadata,
    '--postgres-connection-env','PHOTOIDENTITY_POSTGRES_CONNECTION_STRING',
    '--database','photoidentity_main','--max-pairs','5000')

dotnet run --project $cli --configuration Release -- @common --operation preview
if ($LASTEXITCODE -ne 0) { throw 'Bulk preview failed' }
dotnet run --project $cli --configuration Release -- @common --operation verify-restored
if ($LASTEXITCODE -ne 0) { throw 'Expected all selected assets to be initially active' }
```

Inspect `$session.PairsStaged` and the policy assessment classifications.
The known single protected upper-history exception remains active and OUT
of the approved manifest. Other non-eligible collisions must be counted
and reported, not silently included in an eligible count. The number
1,082 is an *earlier* observation, not an immutable target.

### B. Approve and execute only after verifying a fresh backup

Stop Photo Identity and all other database clients; leave PostgreSQL up.
Create and verify a fresh backup of `photoidentity_main` with the procedures
in `docs/operations/postgresql-operations.md`. Keep the app stopped.
The report must be successful, be within 24h, match the database/schema
and verify cleanup of its isolated restore.

Because the APPLY manifest must be under four hours old, refresh the two
read-only audits and repeat section A **after** the backup if necessary.

```powershell
$restoreReport = '<EXACT_NEW_BACKUP.dump>.restore-verification.json'
$receipts = Join-Path (Split-Path $manifest -Parent) 'receipts'
New-Item -ItemType Directory -Path $receipts -Force | Out-Null
$approval = Read-Host "Type APPLY ALL Fideli pairs after confirming stopped app and restore-tested backup"
if ($approval -cne 'APPLY ALL Fideli pairs') { throw 'Not authorized' }

dotnet run --project $cli --configuration Release -- @common `
    --operation apply `
    --approve-manifest-sha256 $hash `
    --restore-report $restoreReport `
    --receipt-directory $receipts `
    --actor 'archive-alias-operator:bulk-approved' `
    --application-stopped --acknowledge-reversible-retirement
if ($LASTEXITCODE -ne 0) {
    throw 'Apply failed or status uncertain. Inspect receipt and verify-retired before any retry.'
}
```

The service checks every selected pair against live PostgreSQL state
(hash, size, revisions, exact two-record group and protected-history
safeguards), marks upper records retired, then checks **every selected
pair** for one remaining active lowercase asset, zero active uppercase
assets, correct mappings, and preserved revision/automatic review
history **before a single transaction commit**. A failure before commit
rolls back the entire set. Neither the generator nor preview writes.

### C. Verify the committed result

```powershell
dotnet run --project $cli --configuration Release -- @common --operation verify-retired
if ($LASTEXITCODE -ne 0) { throw 'Pinned post-commit verification failed' }
```

Also run `tools/archive-case-alias-retirement-postcheck.sql` and
re-export the paired read-only inventory. Confirm that the active mapping
count increased by **exactly the approved pair count** and all selected
secondaries are hidden. The legacy SQL field `incorrect_survivor_order`
is *not* a conflict for canonical Fideli: the lowercase keeper can be
newer. Inspect the count of remaining active exact `fideli/` /
`Fideli/` groups: the known protected exception may remain, but no
selected eligible group should remain active twice. Restart the *same*
known-good application package only after postchecks pass, and inspect
representative images.

A rejected transaction may safely be retried after correcting its
cause and preparing **fresh** audit/manifest approval. If the process
fails *after commit* or application-level checks fail, do NOT assume
rollback: read the private receipt and run read-only `verify-retired`.
This command intentionally does not implement bulk post-commit restore;
retain the verified whole-database backup for disaster recovery.
Avoid running a partial retry against an uncertain full-set result.

## 4. Previous bounded-batch fallback (not needed for full-set run)

Do NOT Apply from this development stage alone. Once merged and
approved, stop Photo Identity, keep PostgreSQL online, make ONE fresh
post-pilot production backup and verify its isolated restore; keep
the backup until the entire controlled cleanup session is complete.

The existing operator CLI enforces five pairs per Apply, an approved
exact manifest SHA, the app stopped/no other writers, a durable
per-pair receipt, and a restore-verified backup report (<=24h).
Each preview must be newer than four hours. The same genuine
restore-tested backup can be reused across independently approved
batches during its validity window; do not repeat the 19 GiB restore
for every batch.

For each explicitly approved batch, use the established Apply/Restore
commands in `docs/operations/archive-case-alias-approved-pilot.md`,
adding the exact canonical policy switch and that batch's separate
manifest, metadata, hash and private receipt directory. After Apply:

1. Run `archive alias-pilot --operation verify-retired` with the
   same policy and exact pinned files.
2. Run `tools/archive-case-alias-retirement-postcheck.sql`: mapping
   count increases by five, zero conflicts, revisions and automatic
   face actions remain preserved.
3. Stop immediately if anything differs; inspect private receipts and
   restore ONLY the affected batch if necessary.
4. At the end of the session restart the SAME known-good app package
   and visually inspect the surviving image names in the private
   batch-review checklist. Do not trigger a schema migration during
   the session.
5. Re-run the read-only `Fideli` path inventory to confirm the number
   of active casefolded path duplicates declined.

No file deletion, schema migration, batch-size increase, or automatic
multi-hundred-image retirement is part of this PR. The one uppercase
explicit-history exception remains held for separate reconciliation.
