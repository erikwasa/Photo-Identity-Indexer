---
name: db-backup
description: Back up the production PostgreSQL catalogue and verify the backup by an isolated restore. Maintainer-invoked only, before any schema migration or bulk catalogue write.
disable-model-invocation: true
argument-hint: <production-database-name>
---

# Back up and verify the PostgreSQL catalogue

Runbook: docs/operations/postgresql-operations.md ("Routine logical backup").

1. Ask the maintainer to confirm the production database name (argument: `$ARGUMENTS`) and that
   Photo Identity is **stopped**. The restore verifier refuses to run otherwise. Wait for a yes.
2. Back up (podman prompts for approval):
   `./backup-postgres-catalogue.ps1 -DatabaseName <name>`
   Never pass `-CopyToOneDrive` or a OneDrive path. Note the dump path the script prints, but
   don't repeat it in your report.
3. Verify by isolated restore:
   `./verify-postgres-backup-restore.ps1 -BackupPath <dump path> -ApplicationStopped`
4. Report only: backup PASS/FAIL with exit code, verify PASS/FAIL with exit code, and the last
   error line on failure. No paths, database contents or row-level data.
5. If either step fails, stop: the migration or bulk write must not proceed.
   Never retry with other flags, delete dumps or drop databases yourself.
