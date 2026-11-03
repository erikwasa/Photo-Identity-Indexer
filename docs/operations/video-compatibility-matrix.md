# Video compatibility by stage (M30)

**Planning and diagnostics, not a universal support guarantee.**
A file can be catalogued without having a poster, an encoded playable
proxy or frame evidence for search. “Unsupported” at one stage does
not imply that the file is absent, excluded or unreadable.

| Stage | Current delivered behaviour | Main known boundaries | Next owner |
| --- | --- | --- | --- |
| **1. Discovered/catalogued** | Opt-in enumeration of MOV/MP4 videos, source/revision identity, known technical metadata and explicit unsupported/unknown inspection results | File attributes/OneDrive placeholders can be inventoried without opening bytes; actual FFprobe inspection requires already-local bytes or a separately approved managed hydration. The explicit ingest operation has a 1 GiB source ceiling; its supported codec inspection limits include H.264 Main/High, HEVC Main/Main 10 and up to 600 seconds. This is not a promise of derivative support. | WI-0210 for new compatibility classes; WI-0207 for online-only availability |
| **2. Still poster available** | Versioned durable JPEGs for explicitly admitted Rec.709 SDR videos, checked by revision, exclusions, integrity and derivative root | HLG/PQ, missing colour information and several unapproved formats are recorded without silently producing misleading SDR output. A private HLG preview exists but is **not** a production poster profile. Some indexed videos have no poster. | WI-0210 if the archive inventory selects this gap; two existing missing posters remain a separate deferred operator choice |
| **3. Playable local proxy available** | WI-0189 publishes seekable bounded H.264/AAC MP4 under an app-owned local cache (720p default, 540p optional) for admitted local videos | Shared worker currently selects supported Rec.709 SDR H.264/HEVC inputs, known rotations, selected pixel/audio formats, coded dimensions 16–4096, duration 0.2–600 s and at most **256 MiB source bytes**; retention and per-proxy caps are separate. A cached proxy may exist while the original is online-only. | WI-0210 for widened inputs; WI-0207 for managed hydration, WI-0152 completed for actual browser playback |
| **4. Searchable frame evidence** | WI-0190 stores timestamped caption/embedding samples for selected current eligible local proxies; WI-0191 reads them via opt-in Caption/Visual/Combined Video Search | Evidence is a separate deliberate processing step, not produced by cataloguing or playback. Search reads at most 256 eligible candidate revisions and does not check original/proxy bytes at query time. Frame samples do not establish full motion, sound or unsampled scenes. | WI-0210 if new examples warrant sampling evaluation; further ranking/scale follow-up only after meaningful growth |
| **5. Face evidence (future)** | Not delivered; photo identities are never inferred from video merely because frames have captions | Bounded prototype and human reviewed identity-governance decision come first; full temporal tracking and production integration require a later approved scope | WI-0155, staged experiment |

## Independent dimensions, not a single “supported video” switch

- **Source presence:** known local vs OneDrive-online-only is independent
  from codec. A retained, current app-owned playable proxy can be
  played without hydrating an online-only original because WI-0152 supplies
  the authorized browser player. A missing/stale proxy must not silently
  trigger hydration from a browser request.
- **Container and codec:** MOV/MP4 H.264/HEVC represent the initial
  subset; inspecting an input does not guarantee successful transcode.
- **Colour and levels:** currently approved production path is Rec.709
  SDR. HLG/PQ and Rec.709 full-range variants may require separate
  *tested* conversions and output-policy versions.
- **Geometry and duration:** metadata inspection, normal proxy
  generation and selected one-shot FrameEvidence commands have
  different limits. In particular the production worker uses a
  256 MiB source ceiling, while explicit video ingest permits up to
  1 GiB. Do not raise one cap and assume the others follow.
- **Playability:** a generated proxy is not the same as browser
  playback; WI-0152's controlled local/phone serving and seeking are delivered.
- **Search relevance:** a video remains visible in the Videos library
  even when not among search-eligible FrameEvidence revisions.

## Choosing the first compatibility expansion

WI-0210 starts from **counts of real rejected files** and their likely
benefit, then selects the smallest supported-input change that unlocks
the greatest useful archive coverage. Inventory uses existing metadata
first, optional bounded inspection of already-local sample files next.
Do not bulk-hydrate the 2026-10-14 survey's mostly online-only cohort.
Do not treat historical sample counts as the current archive count.

The workitem ends once its **first deliberately selected** expansion
is proven. Remaining formats, larger videos, HDR, rotation, audio and
other variants are deferred candidates—not automatic acceptance gates.

See [WI-0210](../delivery/work-items/WI-0210-archive-driven-video-compatibility-expansion.md),
[WI-0207](../delivery/work-items/WI-0207-opt-in-onedrive-video-hydration-and-release.md),
[ADR-0012](../decisions/ADR-0012-isolated-video-media-architecture.md).

## WI-0210 read-only census checkpoint (2026-11-03)

PR #721's metadata-only census passed all seven maintainer checks on
2026-11-03: 58 live registered videos, of which 26 fail transfer first,
13 pixel format first, 5 rotation first, 1 inspection first and 13
are metadata-eligible. Current registered receipts: 13 playable proxies,
3 frame batches and 1 still poster. The survey measures
**last-observed** availability, not live OneDrive hydration state.
The first proposed engineering class is specifically Rec.709
`yuvj420p` full-range input for playable-proxy output, pending
independently reviewed range conversion, subtype counts, an approved
local video and explicit production-profile approval. Read-only
single-gate projections will bound that opportunity without opening
source bytes or widening support. HLG transfer remains deferred.
See the [WI-0210 census runbook](video-compatibility-wi0210-census.md).
