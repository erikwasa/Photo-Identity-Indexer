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
- Metadata captures codec, profile, dimensions, duration, rotation, pixel format, frame-rate and transfer where FFprobe provides them. **Capture/GPS/camera values are accepted only when strictly validated** using the rules below. Unknown or ambiguous values remain NULL, never inferred from filesystem modification time.
- No video posters are generated or served yet. HDR/HLG tone mapping, revision-bound poster cleanup, safe gallery browsing and wider video functionality remain later WI-0151 development checkpoints.

## Embedded capture time, camera and GPS (metadata parser v2)

Only already-local video input is probed. FFprobe `format.tags` and video `stream.tags` are inspected case-insensitively:

- Capture precedence: **format `com.apple.quicktime.creationdate`** → video-stream `com.apple.quicktime.creationdate` → format `creation_time` → video-stream `creation_time`. Each candidate must contain an explicit timezone (`Z`, `+HH:MM` or `+HHMM`); valid values convert to UTC. Timezone-free QuickTime local clock text is ambiguous and **not** accepted. If none qualifies, both capture timestamp and provenance remain NULL. No fallback to file modified time or machine-local timezone is permitted.
- Capture provenance is the exact selected scope and tag (for example `format:com.apple.quicktime.creationdate`); the metadata inspection profile is now `wi0151-ffprobe-metadata-v2`.
- GPS precedence: `com.apple.quicktime.location.ISO6709` then `location` on format tags, then the same tags on video stream. Only strictly formatted signed decimal-degree ISO-6709 latitude/longitude pairs with terminal `/` are accepted, with optional altitude. Ranges must be latitude -90..90 and longitude -180..180; absent, partial, malformed, DMS or non-ISO pairs remain NULL.
- Camera make/model: `com.apple.quicktime.make` / `com.apple.quicktime.model` (or generic `make` / `model`), bounded and stripped of control characters; missing values remain NULL.
- Capture time, coordinates and camera details are **sensitive local catalogue fields**. They are purposely omitted from the pilot's JSON diagnostic report and are never printed to the CLI's aggregate console summary.
- Existing revision metadata is immutable. If a video revision already has v1 inspection evidence, a v2 re-inspection that changes fields **fails closed** rather than silently overwriting the row. Explicit administrative invalidation/re-inspection is a later controlled step. This checkpoint does not backfill old rows automatically.
- Full end-to-end verification still requires representative QuickTime files with correct timezone, device tag variants and missing/ambiguous fields. These parser tests are deterministic synthetic evidence, not a claim of validated camera-wide coverage.

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
