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

## 3. Future approved production batches

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
