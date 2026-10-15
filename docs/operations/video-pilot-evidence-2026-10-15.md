# WI-0151 local video pilot — sanitized verification, 2026-10-15

## Provenance and privacy

Maintainer's local PowerShell run and private `video-pilot-report.json`, generated 2026-10-15 20:19 UTC, were reviewed. This is an anonymized summary of **25 already-local videos**, not a random census. Neither the raw JSON, source key hashes, full paths, filenames, capture dates, GPS values, original video files nor JPEG posters are committed to Git.

The earlier (8-file) pilot found 1,309 videos, 18 local, 1,291 online-only; it produced 6 posters and intentionally deferred 2 HDR. The second (25-file) run is the **authoritative local verification checkpoint** below; availability changed because seven more videos were local, not because the pilot requested hydration.

## Latest pilot findings

| Measure | Maintainer verification |
|---|---:|
| MOV + MP4 discovered | 1,309 |
| Already available locally | 25 |
| Online-only and not processed | 1,284 |
| Downloading / unavailable / probe or poster failures | 0 / 0 / 0 |
| Skipped by file-size or pilot-count cap | 0 / 0 |
| H.264 High / BT.709 | 9; all 9 SDR JPEG posters created |
| HEVC Main / BT.709 | 7; all 7 SDR JPEG posters created |
| HEVC Main 10 / HLG (`arib-std-b67`) | 9; metadata succeeded, all 9 HDR posters **deferred by design** |
| Generated still posters | 16; each has a matching relative poster reference |
| QuickTime MOV / MP4 in the sample | 10 / 15 |
| AAC audio streams | 25 |
| Rotation tags: -90° / +90° / -180° / absent | 19 / 1 / 1 / 4 |
| 4K (3840x2160) observed | 2 SDR H.264 clips |
| Duration span | about 2.51 seconds to 152.01 seconds |
| Creation tag presence / location tag presence | 25 / 16 (values not stored here) |

The maintainer manually opened the 16 JPEGs and confirmed appropriate orientation, no stretching, and no visible colour problems. They also reported **no unexpected OneDrive downloads**. The successful 4K and multi-rotation poster sample supports the SDR pilot approach. The 152-second clip was HLG and only metadata-probed, so **long-video poster rendering is not yet demonstrated**.

## Gate assessment and implementation consequences

- **Passed (locally):** opt-in, bounded video-only discovery, no accidental OneDrive hydration, H.264 and HEVC Main SDR metadata/thumbnail pilot, rotated SDR still-image orientation and 4K first-frame output, no reported processing failures. These observations do not guarantee all codecs, longer renders or other archive folders.
- **Still open:** HDR HLG/PQ-to-BT.709 tone mapping, frame colour-range and camera metadata/UTC provenance confirmation, long-video poster generation, source-copy exclusion under real catalogue indexing, durable revision-bound technical metadata and poster references, app/library view and photo-only query filtering.
- **Priority:** 9/25 sampled videos use HEVC Main 10 HLG. Do not silently treat them as unsupported or generate washed-out SDR thumbnails; retain explicit pending-HDR state until a tested transform is available.
- **Persistence sequencing:** new schema foundations may safely default all pre-existing photos to media_kind=photo. Video asset/revision insertion and API/gallery exposure must remain **disabled** until source-key, exclusion, revision invalidation and legacy photo-query checks are integrated and verified.

**WI-0151 remains `in_progress`**. Evidence establishes permission to continue the next implementation checkpoint, not acceptance of the full work item.
