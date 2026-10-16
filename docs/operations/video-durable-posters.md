# WI-0151 — durable SDR video still posters

This is a bounded, explicitly enabled still-poster checkpoint. It does not provide playable video, HDR tone mapping, photo thumbnails, search embeddings or face-frame processing. All photo-only functionality remains unchanged.

## Preconditions and operation

The earlier video ingest --enable-video --apply command must have registered a video revision with a supported inspection under the existing local-folder source. The operator must select an application-owned derivative directory that neither contains nor is contained by the archive, a separately installed FFmpeg executable, a named PostgreSQL credentials environment variable and an explicit database. The derivative directory must exist and may not be a reparse-point ancestor. The app never bundles FFmpeg.

The maintainer requested that manual review be deferred until the full work item is developed. At that point, replace ALL sample values with real, verified names:

    $sourceRoot = 'C:\YOUR_EXISTING_ARCHIVE_ROOT'
    $derivatives = Join-Path $env:LOCALAPPDATA 'PhotoIdentity\video-derivatives'
    New-Item -ItemType Directory -Force -Path $derivatives | Out-Null
    $ffmpeg = (Get-Command ffmpeg).Source
    $connectionVariableName = 'YOUR_EXISTING_POSTGRES_CONNECTION_VARIABLE'
    $databaseName = 'YOUR_VERIFIED_CATALOGUE_DATABASE'

Read-only preview; poster files and poster rows are not created:

    dotnet run --project src/PhotoIdentity.Cli -c Release -- video posters --enable-video --source $sourceRoot --derivatives $derivatives --ffmpeg $ffmpeg --postgres-connection-env $connectionVariableName --database $databaseName --max-videos 3 --timeout-seconds 20

After reviewing the dry run and taking a normal PostgreSQL backup, the explicit write is:

    dotnet run --project src/PhotoIdentity.Cli -c Release -- video posters --enable-video --apply --source $sourceRoot --derivatives $derivatives --ffmpeg $ffmpeg --postgres-connection-env $connectionVariableName --database $databaseName --max-videos 3 --timeout-seconds 20

The command never requests OneDrive hydration. It checks the CURRENT video revision's content hash against the SHA-256 of the locally available source; excluded, deleted and superseded rows are not eligible. The initial SDR profile is deliberately fail-closed: only an explicitly reported Rec.709 transfer (bt709) is eligible. Missing, unknown, other unverified SDR transfers, HLG (arib-std-b67), and PQ (smpte2084) are deferred until an explicitly validated rendering profile is provided. No implicit HDR conversion or assumption that missing metadata means SDR is permitted.

The 1280-pixel JPEG profile is bounded to 8 MiB, and requires JPEG signature, decoded header dimensions and SHA-256 validation. Each poster's deterministic relative location is:

    video-posters/wi0151-sdr-still-v1/<revision-guid>/<ffmpeg-sha256>.jpg

The file is created in the derivative root with an unpredictable temporary basename and atomically moved without overwriting existing contents. An immutable video_still_posters row stores checksum, encoded bytes, measured dimensions, profile ID and FFmpeg tool checksum. Repeated runs verify the actual stored file contents and fail closed on mismatches.

## File/database transaction and cleanup

PostgreSQL and the filesystem cannot commit atomically. A crash between file creation and poster record insertion may leave an unreferenced JPEG. An idempotent retry on the same CURRENT revision can recover it after validation. If a source is excluded, deleted, or superseded, its old poster must not be selected by the revision-bound read interface even if the file remains on disk.

**Physical deletion is handled by later WI-0151 checkpoints.** The next WI-0151 slice must add explicit bounded dry-run/audited cleanup of unreferenced, excluded, deleted and superseded poster files within the app-owned derivative root, and test it before any browser-serving API is enabled. Never delete based only on a source file name.

Poster serving, safe video browsing and representative maintained-file quality verification are also still pending. Previous isolated-pilot evidence for SDR appearance is not a substitute for end-to-end maintainer review.

WI-0151 remains in progress until the requested final consolidated acceptance.


For future source-copy exclusions, configure `PhotoIdentity:VideoPosterRoot` to the **same absolute derivative root** used by this command (default: `LocalApplicationData/PhotoIdentity/video-derivatives`). The exclusion purge manifests revision-owned JPEGs before deleting catalogue identity. An inaccurate configured root can leave unrelated files untouched; verify configuration before enabling video browsing.
