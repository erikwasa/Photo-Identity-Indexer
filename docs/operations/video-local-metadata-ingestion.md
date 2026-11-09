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

## WI-0212 v40 technical-stream metadata extension (development)

The opt-in FFprobe parser now also extracts **video-stream bit rate** (bps),
**audio channel count/layout**, and **audio sample rate** (Hz). The new
`video_revision_metadata` fields are nullable, revision-bound and range
validated. Video Details shows video bitrate in Mb/s, plus audio codec,
channels and sample rate (e.g. `AAC · Mono · 44.1 kHz`). Bitrate is only
one compression characteristic and **not** an objective image-quality score.
Missing stream facts stay Unknown; no value is inferred from file size,
resolution or duration. FFprobe's container-level format bitrate is *not*
used as a substitute for video-stream bitrate (it includes other streams).

Camera-tag support is extended for explicit FFprobe `SamsungModel` and
`com.samsung.model` tags, plus `manufacturer`. Samsung is assigned as make
only when a Samsung-specific model tag is actually present. ExifTool may
recognize proprietary device metadata that the approved FFprobe inspection
does not expose; there is **no claim** that every Samsung file will now have
make/model. If the input yields no approved model tag, camera model remains
Unknown. The metadata profile is `wi0212-ffprobe-metadata-v3`.

**Existing inspections remain immutable.** Running the new application and
migrating schema v39 -> v40 will not fill these four new fields for clips
previously inspected with v2, and retrying a v3 ingestion against the same
revision fails closed if newly extracted evidence differs. A separately
authorized, bounded, revision- and exclusion-guarded refresh workflow is
required before the maintainer's existing samples can show the new stream
facts. That workflow must leave original capture/GPS, manual annotations,
photo and face evidence unchanged, and must not hydrate online-only video.

The v40 migration must be independently restore-rehearsed on a verified v39
copy before launching v40 against production. Do not perform this on the
maintained host until the complete metadata-refresh work is ready.

## WI-0212 v41 — selected local metadata refresh (development)

An existing, registered video revision is not reinspected by ordinary Video
Details, Search, ingestion or app startup. Existing v2 evidence is immutable;
v41 adds a **separate** operator-only, one-revision refresh transaction.

The CLI `video metadata refresh` defaults to a **read-only dry run**. Required
arguments: `--enable-video`, existing `--revision UUID`,
`--source-root ABS_REGISTERED_ROOT`, allowlisted `--ffprobe ABS_EXE`,
`--ffprobe-sha256 APPROVED_SHA256`, `--postgres-connection-env NAME`,
and explicit `--database DB`. A write requires both `--apply` and
`--acknowledge-metadata-only`; no implicit fallback or bulk mode exists.

The refresh verifies the catalogue is at v41 without calling the schema
initializer, then selects exactly the **current** video revision under the
explicitly named registered local-folder source. The source-copy exclusion
and deleted-asset guards apply. It reads only an already-local MOV/MP4
(<= 1 GiB), verifies its size, stable file modification time and complete
SHA-256 against the immutable revision, checks the physical FFprobe tool
against the operator-approved digest, and repeats local-availability,
revision and exclusion checks before committing. Root-relative traversal
and symbolic link escapes are rejected; online-only originals remain
online-only. The CLI does not enqueue, play, hydrate, transcode, or analyze
faces and does not write camera data into the original.

The transaction **fills only NULL** camera make/model and v40 bitrate/audio
fields, records which fields changed alongside tool SHA, profile and
inspection time in `video_metadata_refresh_actions`, and leaves original
`inspected_at_utc`, `tool_sha256`, `inspection_profile_id`, capture time,
GPS, Place, people and photo/identity records unchanged. A second identical
refresh writes nothing, and a stale revision or excluded copy fails closed.
If the new parser supplies no new fields, no audit action is recorded.

**This command never auto-migrates the database**; the separate guarded
`catalogue rehearse-v41` requires a successful retained v39 restored
database to rehearse v40/v41 on an isolated database before production
startup. The precise safe operator script is maintained in
`video-details-wi0212-acceptance.md`. Until final acceptance, run neither
the new application against v39 production nor a refresh against it.

ExifTool may decode proprietary Samsung tags that the approved FFprobe
profile does not expose; do not infer a camera make/model when the
verified source tag is absent. An additional trusted vendor adapter may
be needed for real-file acceptance.

## Optional Samsung smta tag adapter (WI-0212, development)

Some Samsung MP4 files store a camera model in the `SamsungModel` field of
the `smta` atom. ExifTool documents that tag within its `Samsung`
group. The FFprobe metadata projection can omit it, which previously
left real-device camera make/model Unknown even though ExifTool showed a
value such as `SM-G736B`.

The selected, **already-local** `video metadata refresh` command now
accepts an optional independently approved ExifTool executable with
`--enable-samsung-vendor-camera --exiftool ABS_EXE --exiftool-sha256 SHA256`.
All three options are required together; omission keeps the existing
FFprobe-only behavior. ExifTool is a separate operator-provided tool
and is neither bundled nor downloaded automatically. The CLI checks
physical executable SHA-256 before and after the scan and rejects linked
binary files and linked parent directories.

The adapter uses `-config "" -json -G1 -s -SamsungModel` with an absolute
single video path and no write options. Only the exact
`Samsung:SamsungModel` tag is accepted, with bounded length, JSON,
control-character and ambiguity checks. The make is explicitly
**Samsung** only when that exact tag is present; no device identity is
inferred from an unrelated field, file name or video codec.
Output and diagnostics never disclose the private model or file path.

After FFprobe has populated its own previously NULL technical fields,
the optional ExifTool pass may **only fill still-NULL camera make/model**.
It records its own v41 `video_metadata_refresh_actions` event with
`wi0212-exiftool-samsung-camera-v1` and the *ExifTool* SHA-256.
The FFprobe refresh has its own separate tool hash/profile/action.
Both use the same current revision, content SHA-256 and source-exclusion
locks; neither rewrites original inspection provenance, capture dates,
GPS, Places, people or photo/face records. Retrying with no new fields
is idempotent; a missing Samsung tag produces no vendor audit action.

The real Samsung example has only been inspected using a separate
metadata viewer; a final controlled local test with the pinned adapter
remains required. Do not claim all Samsung camera models are supported.
See `video-details-wi0212-acceptance.md` for the operator-only
PowerShell 7.6 steps after the complete local-acceptance gate.

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
