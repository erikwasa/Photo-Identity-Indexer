# WI-0151 — isolated HLG-to-SDR evaluation previews

This is **not** a durable poster or a production video library feature. The existing `wi0151-sdr-still-v1` Rec.709 pipeline and default-off video API are unchanged. This checkpoint builds a separate private, operator-only preview to evaluate HLG colour and highlight handling before any HDR output can be published to the catalogue.

## Scope and activation

Only `video pilot --enable-video --preview-hlg` may render HLG preview JPEGs. Without `--preview-hlg`, existing pilot behaviour is unchanged and HDR samples are reported as deferred. This command never creates assets/revisions, video poster SQL rows or automatic jobs, never serves the previews to browsers, never hydrates online-only OneDrive files, and never modifies the original. Its metadata/poster samples remain bounded by `--max-videos`, per-file size and process timeout.

Only already-local MP4/MOV HEVC **Main 10** with `yuv420p10le`, stream `color_transfer=arib-std-b67`, `color_primaries=bt2020` and `color_space=bt2020nc` qualify. The stream evidence is extracted explicitly by FFprobe; none of these values is inferred from the camera/device. PQ (`smpte2084`), other primaries or matrix, missing tags and unsupported codecs are **not** mapped. They continue to report `hdr_poster_needs_tone_mapping`.

## Isolated render recipe

The versioned private path is `video-posters/pilot-hlg-tone-map-v1/<pilot-key-hash>.jpg` below the independent derivative root. It cannot be selected by the current video library, the `wi0151-sdr-still-v1` durable poster APIs or the source-copy purge manifest. Do not mistake pilot previews for governed durable derivatives; delete private preview files yourself after inspection using normal Windows file management, not the production cleanup command.

The filter is intentionally fixed and requires FFmpeg with zscale and tonemap support:

1. `setparams` pins the explicitly validated BT.2020/HLG stream signaling.
2. `zscale` converts to linear light (`npl=100`) in floating-point RGB.
3. `tonemap=mobius:param=0.3:desat=0` compresses highlights.
4. `zscale` converts to limited-range Rec.709, 8-bit YUV 4:2:0; scale bounds the JPEG to a 1280-pixel edge.

This is a **candidate**, not a validated colour-mastering transform; appearance can depend on camera exposure, scene and FFmpeg/zimg build. An automated proof with a synthetic HEVC Main 10 HLG sample demonstrated an actual 1280×720 JPEG with FFmpeg 7.1.5; this does **not** establish the appearance of the maintained archive's iPhone HLG clips or verify the operator's FFmpeg 9.0.2 build.

## Deferred operator procedure (only at final consolidated WI-0151 review)

Select a small number of already-local HLG clips plus representative Rec.709 examples; never pin the whole OneDrive library. Use an app-independent derivative directory **outside** the archive, with no symlink or junction ancestors. Substitute your actual verified roots and executable paths:

```powershell
$sourceRoot = 'C:\YOUR_EXISTING_ARCHIVE_ROOT'
$derivatives = Join-Path $env:LOCALAPPDATA 'PhotoIdentity\private-hlg-review'
$ffprobe = (Get-Command ffprobe).Source
$ffmpeg = (Get-Command ffmpeg).Source
New-Item -ItemType Directory -Path $derivatives -Force | Out-Null

dotnet run --project src/PhotoIdentity.Cli -c Release -- video pilot --enable-video --preview-hlg --source $sourceRoot --derivatives $derivatives --ffprobe $ffprobe --ffmpeg $ffmpeg --max-videos 25 --timeout-seconds 30
```

Inspect the private JPEGs next to the originals in a colour-managed viewer for correct rotation, skin tones, shadows, highlights, clipping, saturation, and stretched dimensions. Make sure the pilot's `max-videos` selected the intended clips: it enumerates **at most 25 locally available candidates**, not 25 HLG clips. Record aggregate outcomes, FFmpeg build and whether each class looked acceptable **without uploading filenames, location/capture tags, camera metadata, posters or the JSON report**. `video-pilot-report.json` contains private sample identifiers and technical metadata, so keep it local.

## Remaining WI-0151 work

- Review representative real HLG quality once all code checkpoints are complete; adjust the fixed tone-map recipe only with concrete visual evidence.
- If accepted, integrate a **new separately versioned durable HDR-to-SDR profile** through atomic publication, path validation, source-copy exclusion/purge, stale cleanup, API authorization and mixed-media tests. Do not silently extend the existing SDR profile or use these private preview JPEGs as production evidence.
- Complete remaining photo-processing/read-path mixed-media regression and one consolidated maintained-photo/video acceptance.

WI-0151 remains **in_progress**. No local action is requested during this development checkpoint.
