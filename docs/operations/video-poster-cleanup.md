# WI-0151 — bounded video poster cleanup (operator only)

This is a separate, **default dry-run** cleanup command for the first revision-bound
SDR JPEG profile, not a general-purpose deletion tool. It never touches video
originals, photo proxies, face crops, or other Photo Identity derivatives.

## Safety scope

- Command: video posters-cleanup --enable-video. **No write occurs without --apply**.
  The operator must also select the exact registered local-folder source root,
  app-owned derivative root, named local PostgreSQL credentials environment variable,
  and explicit database name. Remote PostgreSQL targets are refused.
- The derivative root must be independent of the source archive, and must have
  no reparse-point ancestor. Only canonical revision/tool filenames under
  video-posters/wi0151-sdr-still-v1/ are considered.
- At most 25 items may be retired/deleted in one invocation; --max-files can lower
  this. An age threshold of **at least 24 hours** applies to both poster records
  and existing files (to protect recent and interrupted atomic publishing).
- The source owns each poster through PostgreSQL's asset/revision identity, not
  merely through a directory name. A visible current revision is always retained.
  Stale means superseded, excluded, or soft-deleted.
- For stale recorded posters, only an exact database-backed path and SHA-256 match
  permits deletion. The database row is conditionally retired under the source
  lock before physical file deletion. If the file does not exist, stale rows may
  still be retired; missing-file evidence is counted, not recreated.
- For unreferenced JPEGs, deletion requires a **still-existing video revision**
  owned by the selected source and independently reconfirmed noncurrent/excluded
  in PostgreSQL. Any other orphan is **unknown ownership** and left untouched.
- No directory trees, arbitrary filenames, symlinks/junctions, or non-JPEG files
  are recursively deleted. Candidate filenames are not echoed to console output.
- Repeated runs are idempotent. If interrupted after retiring a DB row but before
  removing its JPEG, a subsequent run can recognize an aged orphan if its
  corresponding revision still exists. Failed checks are reported conservatively.

## Final acceptance procedure (not for intermediate PR review)

The maintainer requested no manual review until all WI-0151 development is ready.
After the full workflow is implemented, replace all placeholders with the correct
existing values, back up the authoritative database, and run the preview first.

    $sourceRoot = 'C:\YOUR_EXISTING_ARCHIVE_ROOT'
    $derivatives = Join-Path $env:LOCALAPPDATA 'PhotoIdentity\video-derivatives'
    $connectionVariableName = 'YOUR_EXISTING_POSTGRES_CONNECTION_VARIABLE'
    $databaseName = 'YOUR_VERIFIED_AUTHORITATIVE_DATABASE'

    dotnet run --project src/PhotoIdentity.Cli -c Release -- video posters-cleanup --enable-video --source $sourceRoot --derivatives $derivatives --postgres-connection-env $connectionVariableName --database $databaseName --max-files 3 --min-age-hours 24

Only after checking the plan and confirming the **source root, database and
derivative root** should a limited write be considered:

    dotnet run --project src/PhotoIdentity.Cli -c Release -- video posters-cleanup --enable-video --apply --source $sourceRoot --derivatives $derivatives --postgres-connection-env $connectionVariableName --database $databaseName --max-files 3 --min-age-hours 24

No cleanup is automatically launched by the API, normal photo scans or workers.

## Remaining exclusion/privacy gate before browsing

**Do not enable serving yet.** Under ADR-0008, the existing source-copy purge
may cascade-delete its asset/revision rows. If that happened before this cleanup
ran, poster files remain on disk without a provable revision/source owner and this
command intentionally refuses to delete them.

The next checkpoint must integrate video poster paths into the source-copy
purge's durable pre-deletion artifact manifest (or another equally auditable
privacy-safe mechanism). This must be done **before** any video poster URL becomes
accessible to the browser. The current video evidence read API already denies
excluded assets, but physical disk deletion is also required for complete privacy.

Unknown/foreign directories, untracked revisions, suspicious checksums, or
mismatched files are never assumed to be safe deletion targets.

Status: WI-0151 in progress; final real-media and photo-only regression review pending.
