# WI-0150 maintained-archive codec survey — 2026-10-14

## Source, scope and privacy

Maintainer-provided, locally generated schema-v2 `VideoArchiveCodecSurvey.ps1` JSON report (2026-10-14 18:41 UTC), evaluated only as **anonymous aggregate evidence**. The raw report, media files, filenames, full paths, GPS and capture dates are **not** in Git. The survey used `ffprobe` on already-local OneDrive content; it did not request mass hydration.

The enumerated population is 74 MOV + 1,235 MP4 = **1,309** videos in the surveyed camera folder, not necessarily the entire archive. **18** were locally available and probed successfully: 3 MOV and 15 MP4. Remaining 71 MOV + 1,220 MP4 = **1,291** were flagged online-only. Every locally available sample was a reparse point; no downloading, evicted, failed, or timed-out probes were reported.

These **18 intentionally/local-availability-selected videos are not a random or representative census** of the 1,309. Thus 9/18 HEVC does not imply 50% of the entire archive. The survey establishes concrete formats to support, not global format distribution or actual decoding success.

## Observed technical coverage

| Characteristic | Evidence among 18 probed videos |
|---|---|
| Container family | QuickTime/ISO BMFF (`ffprobe format_name: mov,mp4,m4a,3gp,3g2,mj2`); file extensions 3 MOV, 15 MP4 |
| Video codec | **9 H.264/AVC** (8 MP4, 1 MOV) and **9 H.265/HEVC** (7 MP4, 2 MOV) |
| H.264 profiles | 8 High, 1 Main |
| HEVC profiles | 8 Main, 1 Main 10 |
| HDR/colour | 1 HEVC Main 10 with `yuv420p10le` and HLG/`arib-std-b67`; 15 report `bt709`; 2 have unspecified transfer |
| Pixel formats | `yuv420p` 9; `yuvj420p` 8; `yuv420p10le` 1; honour reported colour range/transfer, do not assume all are SDR |
| Audio | AAC 17; one MOV with `pcm_s16le` |
| Dimensions (coded; rotation separate) | 1920x1080: 11; 1280x720: 5; 1920x1440: 1; 3840x2160: 1 |
| Rotation | -90 degrees reported for 15; null/not reported for 3; **null does not mean confirmed landscape** |
| Duration | 15 under 15 seconds; 3 between 15 seconds and 2 minutes; **nothing longer was sampled** |
| Embedded metadata | 15 creation-time tag present; 6 location tag present. Values were not shared or stored. |
| Frame rate | Values around 30 fps; the survey records average frame rate only, not enough to classify variable frame rate. |
| Failures | 0 probe failures, 0 timeouts, 0 last-minute availability changes |

## Proposed WI-0151 pilot scope from this evidence

1. Recognize **MOV and MP4 with H.264 Main/High and HEVC Main/Main 10** as candidate inputs. Admit each exact stream/codec combination through bounded tool probing; report other containers/codecs/invalid media as unsupported, not silently ignored. Do **not** infer successful video-frame decoding from successful metadata probing.
2. Persist codec/profile, pixel format and colour transfer, duration, audio codec, width/height, display rotation and tag-presence/provenance; leave missing metadata unknown. Treat a video as portrait/landscape only after applying its display transform.
3. Extract still posters only in the opt-in first slice, with explicit visual checks for portrait rotation, 4K downscaling, HDR-to-SDR tone mapping and `yuvj420p` range correctness. Use sample files owned by the maintainer; do not commit media.
4. A `pcm_s16le` MOV is valid inventory input even if the later browser proxy must transcode audio to AAC. Browser-playable H.264/AAC 720p MP4 proxy is **WI-0189**, not assumed for originals and not a prerequisite for WI-0151.
5. OneDrive scope remains strict: 1,291 online-only source videos should not be opened or hydrated by discovery. Any further sampling should individually pin only selected files, ideally one longer clip and another codec/device cohort, not the whole folder.
6. Keep photo-only UI/query/background processing unchanged by default. Isolate heavy video work under WI-0188; visual search and face tracks remain separate later items.

## Remaining acceptance gates

- **FFmpeg binary origin, integrity and license not verified.** On the maintained Windows computer, record the actual `ffprobe -version`, `ffprobe -buildconf`, `ffprobe -L` and `ffmpeg -version/-buildconf` output (or a private SHA-256 plus a redacted configuration/license summary). Presence of `--enable-gpl`/`--enable-nonfree` and external libraries such as `--enable-libx264` influences deployment/redistribution. Decide external user-installed binaries vs bundled copies, patch-update strategy and executable allowlist before WI-0151 ships. The current design proposes **external, operator-installed binaries**, not redistributed binaries. Legal review is independent from codec probing.
- **Maintainer architecture approval** required before accepting [ADR-0012](../decisions/ADR-0012-isolated-video-media-architecture.md) and transitioning WI-0150 out of `in_progress`.
- **Real poster decoding proof** for HEVC Main 10/HLG, ordinary HEVC/H.264 and rotated clips is a test requirement under WI-0151 (not yet performed), with measured time/memory/OneDrive behaviour.

This is a successful *metadata probe* of a narrow sample, not a video transcoding, playback, content-analysis or full-catalogue coverage test.
