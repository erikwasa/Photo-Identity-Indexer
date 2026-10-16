# WI-0151 — opt-in video source/metadata/poster pilot

This is the **first implementation checkpoint** of WI-0151 under accepted [ADR-0012](../decisions/ADR-0012-isolated-video-media-architecture.md). It is an operator-invoked pilot to validate the codec and poster adapter **before** videos enter the PostgreSQL asset/revision catalogue or normal library UI.

**The pilot is not a completed WI-0151.** Persisted media-kind, revision-bound metadata, durable poster rows, exclusion/missing-source cleanup, API/gallery browsing and broader regression coverage are subsequent work in WI-0151. No video data is introduced into existing photo lists or photo-processing jobs by this pilot.

## This checkpoint

- Separate OneDriveVideoAssetSource enumerates MOV/MP4 using the caller's source-ID contract. The existing OneDriveSyncAssetSource stays photo-only by default.
- Explicit operator-only CLI command, video pilot --enable-video, classifies OneDrive availability without reading bytes or requesting hydration. Online-only videos are counted but not opened.
- Bounded local external FFprobe/FFmpeg executables; absolute paths, argument-array invocation, local/file-only input, cancellation, timeouts, no stdin and no raw error messages in output.
- Technical metadata from FFprobe: stream/container codecs, profile, colour/HDR transfer, dimensions, display rotation, frame rate, duration and capture/GPS tag presence. A later WI-0151 parser checkpoint also extracts **strictly validated capture/GPS/camera values** for local catalogue writes; these private fields are excluded from the pilot's JSON report.
- Supported pilot candidates: H.264 Main/High and HEVC Main/Main 10, at most 10-minute duration, maximum 8K reported dimensions and a 1 GiB per-video file-size cap. HLG/PQ HDR is probed but **poster rendering deferred** until tone mapping is verified.
- JPEG still poster generation under an app-owned derivative root separate from OneDrive, with bounded 1280px scaling, size/signature checks and atomic publishing. Disposable pilot keys use source path + size + last-write observations; these are NOT immutable content-hashed revision IDs.
- A private video-pilot-report.json manifest with anonymized key hashes, technical metadata and aggregated counters (no original paths, GPS values or capture timestamps).

## Run on Windows

Requires .NET SDK and the separately installed Winget FFmpeg/FFprobe binaries approved in WI-0150. From the repository root, use PowerShell:

    $source = 'C:\Path\To\OneDrive\CameraRoll'
    $ffprobe = (Get-Command ffprobe).Source
    $ffmpeg = (Get-Command ffmpeg).Source
    $derivatives = Join-Path $env:LOCALAPPDATA 'PhotoIdentity\video-pilot'
    dotnet run --project src/PhotoIdentity.Cli -- video pilot --enable-video --source $source --derivatives $derivatives --ffprobe $ffprobe --ffmpeg $ffmpeg --max-videos 8 --timeout-seconds 20

Select a few already-local test clips with **Always keep on this device** in File Explorer. Do not pin the entire camera folder. Do not direct derivative output into OneDrive. Input discovery enumerates the local sync directory without reading contents; the pilot opens no more than eight selected local candidates for bounded inspection.

Inspect video-pilot-report.json and the video-posters/pilot-poster-v1 directory under the derivative root. No PostgreSQL changes or library cards appear yet.

## Verification and remaining WI-0151 acceptance gates

- Parser tests cover absent/empty metadata tags and real-survey-inspired rotated 10-bit HLG and AAC examples.
- Source tests ensure photo-only enumeration stays unchanged, online-only video is never opened, and path traversal is refused.
- Local real-video poster results, rotation, HDR tonemapping, colour range, 4K workload and timed-out/invalid inputs require further verification.
- Video assets must be integrated with existing SourceId/AssetId/AssetRevisionId and PostgreSQL without introducing video into photo-only queries, analysis or slideshows. Persist media kind and revision-bound metadata/poster identity with a migration; enforce exclusion, revision invalidation and managed local derivative purge before gallery exposure.
- This command does not provide automatic video indexing, persistent catalogue identity, gallery/API browsing, content verification or production-ready HDR conversion. Those are unfinished aspects of WI-0151.

WI-0151 stays **in_progress** until these are implemented and verified. The successful [2026-10-15 local pilot evidence](video-pilot-evidence-2026-10-15.md) now verifies SDR posters and confirms nine deferred HLG clips. Schema checkpoint 2 introduces PostgreSQL v33 photo-default media kinds and revision-bound metadata/poster tables, but it **does not** wire the CLI pilot to those tables or display videos. No uncontrolled OneDrive hydration, mass transcoding, cloud execution or changed photo behavior is permitted.
