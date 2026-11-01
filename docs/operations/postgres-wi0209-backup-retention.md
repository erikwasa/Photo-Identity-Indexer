# WI-0209: operator-managed PostgreSQL backup retention (Windows)

This procedure is explicit and local: no scheduled rotation, OneDrive API, cloud transfer, or backup deletion based on age or an isolated restore succeeding. WI-0208 manages temporary restored **databases** rather than backup files.

## Approved local root and read-only inventory

Run from PowerShell 7 at the repository root:

~~~powershell
$backupRoot = Join-Path $env:LOCALAPPDATA 'PhotoIdentity\backups\postgresql'
.\manage-postgres-backups.ps1 | Format-Table Name, Role, MiB, ManifestPresent, RestoreReportPresent, ReparsePoint
~~~

For an **individual already-existing backup**, inspect its *actual bytes* and restore report without changing its classification or retention state:

~~~powershell
$backupRoot = Join-Path $env:LOCALAPPDATA 'PhotoIdentity\backups\postgresql'
$candidate = Join-Path $backupRoot 'wi0208-acceptance-20261101-142600.dump'
.\manage-postgres-backups.ps1 -InspectBackup -BackupPath $candidate | Format-List
~~~

The inspection checks the dump hash/size, original manifest and any restore report. `EligibleForCheckpointPromotion=true` establishes only **local restore evidence**; it does not establish that an independent remote copy exists. `RemoteConfirmationRecorded=true` means an operator previously registered an attestation, **not** a live check of OneDrive availability. Promotion still requires independently retrieving and hashing the off-disk copy. A mismatched manifest or restore fingerprint causes inspection to fail.

The inventory does not change files. Previously existing dumps are **unclassified** until individually registered; unclassified backups are never eligible for cleanup. The tool requires direct children of the approved backup directory, rejecting nested paths, traversal, links/junctions and reparse files (including online-only placeholders). Explicit -BackupRoot is for intentionally approved alternate directories; review that scope before using it.

The only state file is local **retention-state.json** beside the backup files; it records pre-change, protected recovery checkpoint and superseded checkpoint roles. Never publish this private file, a dump, personal identifiers or remote evidence to the repository. Do not manually edit registry roles.

## A. Pre-change precaution and acceptance-controlled retirement

Identify the actual production catalogue before taking a precautionary backup. Stop the app if exact count-stable restore checks are required.

~~~powershell
$backupRoot = Join-Path $env:LOCALAPPDATA 'PhotoIdentity\backups\postgresql'
$backup = Join-Path $backupRoot 'prechange-maintenance.dump'
.\backup-postgres-catalogue.ps1 -DatabaseName '<confirmed-production-database>' -OutputPath $backup
$sha = (Get-Content -LiteralPath "$backup.json" -Raw | ConvertFrom-Json).backupSha256
.\manage-postgres-backups.ps1 -RegisterPreChange -BackupPath $backup -ExpectedSha256 $sha -ChangeReference 'your-specific-change'
~~~

A passing backup or isolated restore **does not establish that the subsequent database/application change succeeded**. Keep the pre-change backup for failures, interruptions and unverified changes. Only after the actual post-change tests have passed:

~~~powershell
.\manage-postgres-backups.ps1 -RemovePreChange -BackupPath $backup -ExpectedSha256 $sha -ConfirmChangeAccepted -ChangeAcceptanceEvidence 'passed-post-change-verification-reference' -ConfirmRemoval
~~~

Before removal, review a **read-only exact-file plan**; it validates the selected backup and enumerates only the files that would be targeted, without deleting anything:

~~~powershell
.\manage-postgres-backups.ps1 -PlanRemoval -BackupPath $backup -ExpectedSha256 $sha | Format-List
~~~

This verifies the exact dump SHA-256 and manifest before deleting its dump and only its related JSON manifest and optional restore-verification sidecar. No wildcard, automatic age-based deletion or implicit acceptance. Failures report an incomplete operation and retain the classification for investigation. If a sidecar was already deleted during a failed operation, manual recovery may be needed; never use a wildcard retry.

## B. One protected recovery checkpoint and replacement

A candidate recovery checkpoint must have a matching SHA-256 manifest and a **passing isolated restore verification** whose temporary database cleanup succeeded. The same verified pre-change dump may be promoted rather than making another dump.

Confirm that an **off-disk** copy exists and is usable, preferably through the signed-in **OneDrive web UI**. Inspect the actual remote item, retrieve it independently of the local sync folder, and calculate SHA-256 on those retrieved bytes. A OneDrive sync icon or local OneDrive-synced directory alone is **not** evidence of another physical copy. Record remote availability and hash-check details in a private operator log.

~~~powershell
$localSha = (Get-Content -LiteralPath "$backup.json" -Raw | ConvertFrom-Json).backupSha256
$remoteSha = '<SHA-256-of-independently-retrieved-remote-file>'
.\manage-postgres-backups.ps1 -SetRecoveryCheckpoint -BackupPath $backup -ExpectedSha256 $localSha -RemoteSha256 $remoteSha -RemoteConfirmation 'OneDrive web copy independently retrieved and checked; see private operator log' -ConfirmIndependentCopy
~~~

The tool verifies local hash, manifest, successful restore report, supplied remote SHA-256 and **explicit operator attestation**. It never accesses OneDrive APIs or claims to have verified off-device availability itself. If independent confirmation fails, do not promote or retire anything.

To replace a checkpoint, create and fully verify a separately named backup, confirm its remote copy, then repeat -SetRecoveryCheckpoint using the new file. The former checkpoint is **marked superseded but not deleted**. Only when the replacement remains verified and protected:

~~~powershell
.\manage-postgres-backups.ps1 | Format-Table Name, Role, ManifestPresent, RestoreReportPresent
.\manage-postgres-backups.ps1 -RemoveSuperseded -BackupPath $oldBackup -ExpectedSha256 $oldSha -ConfirmRemoval
~~~

Removal rechecks the **current** checkpoint's local hash, manifest and passing restore report. If any evidence is missing, the former checkpoint is retained. The active checkpoint can never be deleted using either cleanup mode. This tool deletes only local approved files, **not remote recovery copies**; do not manually delete the only independent cloud copy.

## Verification and remaining local acceptance

The helper rejects unknown files, hashes, missing manifests, failed restore reports, incomplete remote confirmation, reparse-point paths, missing post-change acceptance, and attempts to remove the sole recovery checkpoint. Synthetic test commands:

~~~powershell
pwsh -NoProfile -File .\tests\PowerShell\postgres-backup-retention.tests.ps1
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
~~~

**Maintainer verification, 2026-11-01:** The existing WI-0208 full-size accepted dump was uploaded to OneDrive, then downloaded independently to a separate local directory. Its SHA-256 matched the verified original; the operator registered it as the sole protected `recovery-checkpoint`, and subsequent read-only inspection reported `EligibleForCheckpointPromotion=True` and `RemoteConfirmationRecorded=True`. There is no need to re-upload or restore it merely to repeat this acceptance. The recorded attestation does not provide ongoing cloud monitoring.

**WI-0209 accepted on the maintained Windows host, 2026-11-01:** The maintainer executed `tests/PowerShell/postgres-backup-retention.tests.ps1` locally and **all 49 assertions passed**. This script uses a newly generated temporary backup root and actual backup-manager commands to inspect and register synthetic dumps, verify an exact-file preview, refuse deletion without post-change acceptance, remove a synthetic pre-change dump and its sidecars after explicit *simulated* change acceptance, retain the old checkpoint when its replacement evidence is missing, and retire the superseded synthetic checkpoint only after replacement checks pass. It also tests the locked Windows file, corrupt hashes, unclassified files and required confirmation switches. Because those filesystem operations already exercised the protected boundaries, a second manually built disposable-root rehearsal is unnecessary.

The **real** previously full-size restore-verified backup `wi0208-acceptance-20261101-142600.dump` was uploaded to OneDrive, separately retrieved, compared against the original SHA-256 and registered as the protected recovery checkpoint; follow-up inspection confirmed that state. This is operator-attested remote copy availability at that time, **not continuous cloud monitoring**. The other five local historical backups remain unclassified and untouched. Do not test cleanup against the sole real checkpoint or assume that a passing isolated restore authorizes deleting a pre-change backup before the actual associated change is accepted. The instructions above remain the procedure for future maintenance. Existing unclassified real backups remain untouched.

No PostgreSQL production data, archived legacy restore databases, OneDrive objects, Podman volumes or Windows VHDX are modified by the helper.
