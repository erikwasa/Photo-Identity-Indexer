# Case-only alias retirement: staged batches after first production pilot

**Preview and staging only.** Each batch is a separate, operator-approved
invocation of the already proven five-pair CLI from PR #848. Nothing
runs automatically. No database rows, annotations or source originals
are removed by the staging command.

The first five production retirements passed their transactional
postcheck (five active mappings, zero conflicts, five preserved revisions
and one preserved automatic face-review action), plus successful app
restart/photo-access verification on schema v45.

## Boundaries and expected counts

- The same authoritative read-only candidate planner recomputes every
  eligibility decision from fresh paired PostgreSQL audits. A retired
  secondary has marked_missing=true and is therefore NOT eligible again.
- If nothing else changes, the initial 333 eligible, 755 held becomes
  approximately **328 remaining eligible, 760 held**, while all 1,088
  historic case-only pairs remain discoverable. Actual counts are
  determined by the fresh audit, not hardcoded.
- Up to **four independent manifests** are staged, each 1–5 eligible
  pairs, maximum 20 per planning session. Existing CLI Apply still has
  a hard limit of five, SHA approval, stopped-app gate, backup proof and
  one private receipt file per invocation.
- Distinct secondary assets and normalized source keys are checked
  across all staged batches; manually protected and already-retired
  secondaries remain held.
- Zero-automatic-review-history pairs are scheduled first. Automatic
  history remains protected by the existing SERIALIZABLE service.
- All batch directories are NEW and immutable by default. Reuse would
  risk overwriting needed rollback receipts, and is prohibited.
- Retained source originals, asset rows, face review, annotations and
  previously held 755 groups remain out of deletion scope.

## 1. Verify the new offline staging tool

Windows PowerShell 7.6, from the repository root:

~~~powershell
pwsh -NoProfile -File '.\tests\PowerShell\archive-case-alias-batch-previews.tests.ps1'
~~~

CI also parses the script and executes a synthetic case: 12 eligible
records split 5/5/2, one human-protected and one already retired pair
held; no overlaps, hashes and schema metadata checked, refusal to
overwrite manifests, stale audits rejected. No production access is
needed.

## 2. Re-export the read-only production audits

The app can stay running for this **read-only** stage.

~~~powershell
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force '.\artifacts' | Out-Null
function Export-ArchiveAudit {
    param([string]$SqlPath, [string]$CsvPath)
    $sql = Get-Content -LiteralPath $SqlPath -Raw
    $result = $sql | & podman exec -i $containerId sh -c 'PGPASSWORD="$POSTGRES_PASSWORD" exec psql -h 127.0.0.1 -U "$POSTGRES_USER" -d photoidentity_main -X -q -v ON_ERROR_STOP=1'
    if ($LASTEXITCODE -ne 0) { throw "Read-only audit failed: $SqlPath" }
    $result | Set-Content -LiteralPath $CsvPath -Encoding utf8
}
Export-ArchiveAudit '.\tools\archive-case-alias-retirement-evidence.sql' '.\artifacts\archive-case-retirement-evidence-fresh.csv'
Export-ArchiveAudit '.\tools\archive-case-alias-effective-state-audit.sql' '.\artifacts\archive-case-effective-state-fresh.csv'
~~~

The two source CSVs must be fresh (<=4h) and exported within 15
minutes of one another. They contain PRIVATE source keys and IDs.

## 3. Prepare distinct preview-only batches

~~~powershell
$session = .\tools\new-archive-case-alias-batch-previews.ps1 `
    -RetirementEvidenceCsv '.\artifacts\archive-case-retirement-evidence-fresh.csv' `
    -EffectiveStateCsv '.\artifacts\archive-case-effective-state-fresh.csv' `
    -ExpectedDatabaseSchemaVersion 45 `
    -MaxBatches 4 -PairsPerBatch 5

$session | Select-Object CollisionGroupsRevalidated, EligibleUnretiredCandidates, HeldForReview, PairsStaged, BatchesStaged, AutomaticallyExecuted
$session.Batches | Select-Object Batch, Pairs, WithAutomaticReviewHistory, ManifestSha256
~~~

The output includes a new private batch-session summary and
batch-01 through batch-04 directories (or fewer if fewer remain).
Each contains a CLI-compatible pilot-preview-private.csv and
pilot-preview-metadata.json, pinned to schema v45. Each batch has
a distinct manifest SHA-256. Do not share these private files.

## 4. Verify the first staged batch against the live database

~~~powershell
$batch = $session.Batches[0]
$manifest = (Resolve-Path -LiteralPath $batch.PrivateManifestPath).Path
$metadata = (Resolve-Path -LiteralPath $batch.PrivateMetadataPath).Path
$hash = (Get-FileHash -LiteralPath $manifest -Algorithm SHA256).Hash.ToLowerInvariant()
if ($hash -cne $batch.ManifestSha256) { throw 'Batch manifest hash changed.' }

dotnet run --project '.\src\PhotoIdentity.Cli\PhotoIdentity.Cli.csproj' --configuration Release -- `
    archive alias-pilot --manifest $manifest --metadata $metadata `
    --postgres-connection-env PHOTOIDENTITY_POSTGRES_CONNECTION_STRING `
    --database photoidentity_main --max-pairs 5
if ($LASTEXITCODE -ne 0) { throw 'Batch CLI preview failed.' }
~~~

This step only checks the manifest and does not alter production.

## Future approved production Apply — one batch at a time

Only after reviewing the PR, merging, and approving each specific
batch, follow the EXISTING operator runbook
docs/operations/archive-case-alias-approved-pilot.md:

1. Stop Photo Identity (leave PostgreSQL running). Make a NEW full
   production backup after the first successful pilot and prove it
   restores into an isolated database with schema 45, passed=true,
   cleanupStatus=removed, cleanupRequired=false. This preserves the
   already-retired first five as the new recovery baseline.
2. Verify the chosen batch's generatedAtUtc is <=4h old, its pinned
   schema is 45, and its SHA is the one explicitly being approved.
   If stale, export two NEW audits and create a NEW session.
3. Execute only that batch via the existing CLI --operation apply,
   --max-pairs 5, --approve-manifest-sha256, --restore-report,
   --receipt-directory, --actor, --application-stopped and
   --acknowledge-reversible-retirement. Each invocation requires
   explicit operator confirmation and produces its own durable receipt.
4. Immediately run the existing read-only
   tools/archive-case-alias-retirement-postcheck.sql and verify that
   active mappings increased by exactly the newly retired count;
   no survivor/source/path conflict; all secondary revision and
   automatic-review evidence remains attached. Stop on first anomaly.
5. If a batch fails or partially applies, keep the app stopped and
   preserve all receipt files. Use the same batch's manifest and
   --operation restore if recovery is appropriate. Never restore the
   older five-pair pilot merely because a new batch failed.
6. Review results and grant separate approval before the next batch.
   Do NOT chain all four Apply operations unattended. The four-batch
   generator never calls the CLI's Apply mode.

The 760 held groups (expected after the first five) require separate
handling; this work does not assert they can be safely retired.
