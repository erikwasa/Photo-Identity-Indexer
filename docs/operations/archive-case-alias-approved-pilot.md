# Bounded reversible archive case-alias pilot — operator procedure

This is the first explicitly approved retire/restore command following
PostgreSQL migration v44, the transactional service and the private
preview in PR #846. It never deletes original files, database asset
rows, revision rows, face observations or review history.

## First: disposable verification (PowerShell 7.6 / Windows)

~~~powershell
.\verify-postgres.ps1 -SkipContainerStart
~~~

This verifier supplies a disposable PostgreSQL administrator
connection to its child integration tests, so the new CLI test
genuinely executes. It verifies synthetic single-pair apply/restore
with no lost automatic face review actions,
wrong approval refusal and refusal while another client is connected.

## Prerequisites for the FIRST production Apply

1. Stop the Photo Identity application normally (keep PostgreSQL on).
   Keep it stopped for backup, isolated restore verification, Apply,
   postcheck and any Restore. All other production SQL clients must also
   disconnect before Apply; the CLI checks pg_stat_activity.
2. Confirm the configured localhost PostgreSQL database is
   photoidentity_main at schema v44.
3. Make a new production backup and **actually verify its restore** in
   a disposable database. The backup can be large; preserve enough free
   storage for an isolated restore and do not delete the existing copy.
   See docs/operations/postgresql-operations.md.

~~~powershell
.\backup-postgres-catalogue.ps1 -DatabaseName photoidentity_main
.\verify-postgres-backup-restore.ps1 -BackupPath '<EXACT_NEW_BACKUP.dump>' -ApplicationStopped
~~~

The report <EXACT_NEW_BACKUP.dump>.restore-verification.json must have
passed=true, sourceDatabase=photoidentity_main,
cleanupStatus=removed and cleanupRequired=false. The report must be
no older than 24 hours. Keep the .dump file available and protected.
4. Regenerate both read-only audit CSVs and the five-pair preview using
   docs/operations/archive-case-alias-pilot-preview.md. The
   pilot-preview-metadata.json timestamp must be within four hours.
   Review the 5 selected pairs locally and verify the SHA-256.
5. Keep the private manifest, metadata, restore report and receipts
   OUT of Git and chat. Never type database passwords on the command
   line: use the existing launcher connection environment variable.

## 1. Read-only CLI preview (no application stop required)

~~~powershell
$envName = 'PHOTOIDENTITY_POSTGRES_CONNECTION_STRING'
$manifest = (Resolve-Path '.\artifacts\archive-case-pilot\pilot-preview-private.csv').Path
$metadata = (Resolve-Path '.\artifacts\archive-case-pilot\pilot-preview-metadata.json').Path
$pilotHash = (Get-FileHash -LiteralPath $manifest -Algorithm SHA256).Hash.ToLowerInvariant()

dotnet run --project .\src\PhotoIdentity.Cli\PhotoIdentity.Cli.csproj -- `
    archive alias-pilot `
    --manifest $manifest --metadata $metadata `
    --postgres-connection-env $envName `
    --database photoidentity_main --max-pairs 5
if ($LASTEXITCODE -ne 0) { throw 'Read-only preview failed.' }
~~~

Expected: PILOT PREVIEW with five pinned pairs, the reviewed SHA-256
and explicit no database writes. It does not call the retire service.

## 2. Apply: exactly the reviewed manifest (no delete or hydration)

Run ONLY after the independent restore, refreshed preview and explicit
operator decision. The app must remain stopped.

~~~powershell
$restoreReport = '<EXACT_NEW_BACKUP.dump>.restore-verification.json'
$receipts = Join-Path $PWD 'artifacts\archive-case-pilot\receipts'
New-Item -ItemType Directory -Path $receipts -Force | Out-Null

dotnet run --project .\src\PhotoIdentity.Cli\PhotoIdentity.Cli.csproj -- `
    archive alias-pilot `
    --operation apply `
    --manifest $manifest --metadata $metadata `
    --postgres-connection-env $envName `
    --database photoidentity_main --max-pairs 5 `
    --approve-manifest-sha256 $pilotHash `
    --restore-report $restoreReport `
    --receipt-directory $receipts `
    --actor 'archive-alias-operator:approved-pilot' `
    --application-stopped `
    --acknowledge-reversible-retirement
if ($LASTEXITCODE -ne 0) { throw 'Apply stopped; inspect private receipt and DB.' }
~~~

The command validates manifest SHA and fields, schema, stopped-app
status, source database, recent verified backup report and file
presence. It creates a private, flushed JSONL receipt before writing
and after each pair. Every pair goes through the existing
SERIALIZABLE transactional C# service, checking live protected history,
content and effective annotations. Failures stop the batch; a
partially applied batch can be restored.

## 3. Postcheck (read-only)

~~~powershell
$sql = Get-Content '.\tools\archive-case-alias-retirement-postcheck.sql' -Raw
$result = $sql | & podman exec -i $containerId sh -c 'PGPASSWORD="$POSTGRES_PASSWORD" exec psql -h 127.0.0.1 -U "$POSTGRES_USER" -d photoidentity_main -X -q -v ON_ERROR_STOP=1'
if ($LASTEXITCODE -ne 0) { throw 'Read-only postcheck failed.' }
$result | ConvertFrom-Csv | Format-List
~~~

After five NEW successful retirements, expect 5 active mappings,
eight zero conflict counts, nonzero retained secondary revisions
and preserved review history in the selected auto-action case.
Do not restart the app if any integrity conflict is reported.

## 4. Restore (same approved manifest; older preview is permitted)

Stop the app again if it was restarted. The same manifest, metadata
and SHA-256 can restore partially applied pairs, without a new
backup report. It does not undo historical review decisions.

~~~powershell
dotnet run --project .\src\PhotoIdentity.Cli\PhotoIdentity.Cli.csproj -- `
    archive alias-pilot `
    --operation restore `
    --manifest $manifest --metadata $metadata `
    --postgres-connection-env $envName `
    --database photoidentity_main --max-pairs 5 `
    --approve-manifest-sha256 $pilotHash `
    --receipt-directory $receipts `
    --actor 'archive-alias-operator:rollback' `
    --application-stopped `
    --acknowledge-reversible-retirement
if ($LASTEXITCODE -ne 0) { throw 'Restore stopped; inspect private receipt and DB.' }
~~~

Run the read-only postcheck again; active mappings should return
to baseline zero. The service preserves both assets and every
revision/annotation, and explicitly refuses a second retirement
after restoration without a separately reviewed process.

## Important boundaries

- Five pairs maximum; this PR is not an unattended 333-pair run.
- Pairs commit separately. Review the durable JSONL receipt after a
  partial failure and restore using the exact original manifest.
- The backup restore report must be genuine: the CLI checks report
  success, age and associated .dump presence, not a new full hash scan.
- The other 755 pairs with annotation differences remain held.
- **Never use direct DELETE, source exclusions or filesystem deletion
  as a shortcut.** This is reversible logical retirement only.
