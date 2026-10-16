# WI-0151 — Operator-only local video metadata ingestion

This command connects the accepted PostgreSQL schema v33, video asset/revision registration and video inspection repositories to the existing OneDrive-safe MOV/MP4 discovery and FFprobe parser.

**There is no API/UI entry point, background worker, automatic queue or poster renderer.** The existing photo scanner and all photo-only processing stay independent.

## Safety contract

- Explicit opt-in is required: --enable-video. Without --apply, the command is a **dry run** and writes nothing to PostgreSQL. --apply additionally requires --postgres-connection-env NAME and --database NAME, naming the exact intended localhost PostgreSQL catalogue. The launcher connection string can omit Database; the operator's explicit target overrides it only for this CLI process.
- The --source directory is the existing stable archive root, not a single file. The command reuses its canonical local-folder source ID and original source-relative keys.
- Enumeration reads only filesystem attributes. OneDrive online-only, downloading, unavailable or error states are never probed, hashed or hydrated. In apply mode, source-copy exclusions are checked before reading bytes and again during transactional registration.
- Processing is sequential, limited to 8 local files by default, at most 25 with --max-videos, and 1 GiB per video. The separately installed FFprobe executable must be selected by an absolute trusted path, has a bounded execution timeout, and is invoked using a file-only protocol whitelist. The executable SHA-256 is recorded with inspection evidence.
- Supported codec/profile is determined by the previous real-file pilot: H.264 Main/High or HEVC Main/Main 10, with valid dimensions and duration under its pilot cap. Unsupported variants are stored as unsupported video inspection evidence; there is no photo fallback.
- The original source stream is SHA-256 hashed. Local availability, size and last-write time are rechecked before/after probing and hashing; changed observations are rejected. The command never modifies video originals.
- Metadata captures codec, profile, dimensions, duration, rotation, pixel format, frame-rate and transfer where FFprobe provides them. Capture time, GPS and camera tags remain NULL: the current pilot parser reports tag *presence*, not verified values/provenance. Never use filesystem modification time as a fabricated camera capture time.
- No video posters are generated or served yet. HDR/HLG tone mapping, revision-bound poster cleanup, safe gallery browsing and wider video functionality remain later WI-0151 development checkpoints.

## Commands for the final end-to-end acceptance

The maintainer asked to defer hands-on testing until WI-0151 is developed completely. These commands document the subsequent controlled acceptance path. Do not import the full archive during the intermediate PR review.

Read-only database dry-run (local video bytes for the small selected set are still probed and hashed):

~~~powershell
$sourceRoot = 'C:\YOUR_EXISTING_ARCHIVE_ROOT'
$ffprobe = (Get-Command ffprobe).Source
dotnet run --project src/PhotoIdentity.Cli -c Release -- video ingest --enable-video --source $sourceRoot --ffprobe $ffprobe --max-videos 3 --timeout-seconds 20
~~~

After reviewing this report and taking an ordinary PostgreSQL backup, an explicitly authorized bounded write is:

~~~powershell
$launcher = Get-Content "$env:LOCALAPPDATA\PhotoIdentity\launcher.json" -Raw | ConvertFrom-Json
$connectionVariableName = [string]$launcher.postgresConnectionEnvironmentVariable
dotnet run --project src/PhotoIdentity.Cli -c Release -- video ingest --enable-video --apply --postgres-connection-env $connectionVariableName --database photoidentity_rehearsal_20260910_214641_a808bde --source $sourceRoot --ffprobe $ffprobe --max-videos 3 --timeout-seconds 20
~~~

Replace the example database name with the **confirmed authoritative database**, never assume a dated rehearsal name is production. Check the launcher and database activity before running --apply.

The command outputs aggregate counts only, never source paths, capture metadata, tool stderr or connection strings. Explicit --apply writes locally verified revisions and metadata, not photo analysis, poster images or slideshow records.

**Failure and retries:** Each video registration and its inspection save are separate operations. If inspection persistence fails after revision registration, rerunning on unchanged bytes is idempotent and can complete the metadata. Conflicting evidence fails closed for review; no automatic cleanup, transcode or hydration is performed. An exit code of 1 signals that at least one selected local observation failed or changed during inspection.

## Acceptance boundary

Unit/integration tests exercise the argument gates, mapping and local file observation checks. Disposable PostgreSQL persistence tests separately verify stable revisions, exclusions and photo-only isolation. Full WI-0151 acceptance requires real-file metadata and poster verification plus complete photo and video gallery regression checks; this checkpoint alone does not satisfy those requirements.
