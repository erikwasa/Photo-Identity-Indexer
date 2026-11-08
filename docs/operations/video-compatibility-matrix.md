# Video compatibility by stage (M30)

**Planning and diagnostics, not a universal support guarantee.**
A file can be catalogued without having a poster, an encoded playable
proxy or frame evidence for search. “Unsupported” at one stage does
not imply that the file is absent, excluded or unreadable.

**Current WI-0207 state (2026-11-08):** Opted-in, protected
managed hydration/release has passed maintained-host acceptance
for both an already-inspected video and a never-inspected online-only
video. The first-time path hydrates an eligible placeholder for
verified inspection before approved proxy processing; it does **not**
make unsupported formats playable or authorize browsing-triggered
hydration. Earlier inspection-prerequisite text below is historical.

| Stage | Current delivered behaviour | Main known boundaries | Next owner |
| --- | --- | --- | --- |
| **1. Discovered/catalogued** | Opt-in enumeration of MOV/MP4 videos, source/revision identity, known technical metadata and explicit unsupported/unknown inspection results | File attributes/OneDrive placeholders can be inventoried without opening bytes; actual FFprobe inspection requires already-local bytes or a separately approved managed hydration. The explicit ingest operation has a 1 GiB source ceiling; its supported codec inspection limits include H.264 Main/High, HEVC Main/Main 10 and up to 600 seconds. This is not a promise of derivative support. | WI-0210 for new compatibility classes; WI-0207 for online-only availability |
| **2. Still poster available** | Versioned durable JPEGs for explicitly admitted Rec.709 SDR videos, checked by revision, exclusions, integrity and derivative root | HLG/PQ, missing colour information and several unapproved formats are recorded without silently producing misleading SDR output. A private HLG preview exists but is **not** a production poster profile. Some indexed videos have no poster. | WI-0210 if the archive inventory selects this gap; two existing missing posters remain a separate deferred operator choice |
| **3. Playable local proxy available** | WI-0189 publishes seekable H.264/AAC MP4 (Standard 720p / Compact 540p). WI-0210 accepted one real full-range Rec.709 `yuvj420p` → limited-range 720p proxy. Under WI-0207, both output qualities select a source-specific conversion for either approved Rec.709 input class; no third input-format quality option is needed. | Approved input admission is independent of output resolution, while source-specific durable conversions retain distinct versions. Existing 256 MiB source, 0.2–600 s, geometry 16–4096, codec/audio/rotation, privacy/exclusion and photo-first bounds remain. Full-range requires actual BT.709 and full-range input evidence and metadata-stripped limited-range output. No HLG/PQ or unknown colour/rotation; online-only hydration is separately opt-in and includes guarded first-time inspection for eligible online-only MOV/MP4 placeholders; processing remains subject to the verified approved input policy. The remaining 11 historical metadata candidates are **not** independently proven. | WI-0210 completed for the first full-range playable-proxy class; WI-0207 separately owns managed hydration/release; WI-0152 completed browser playback |
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
- **Colour and levels:** current playable-proxy inputs include standard
  Rec.709 SDR and independently verified full-range Rec.709
  `yuvj420p` → limited-range proxy. Both Standard 720p and Compact
  540p output choices accept these source classes; staged full-range
  checks and source-specific conversion remain independent. HLG/PQ and unspecified transfer remain unsupported
  for automatic playable-proxy production.
- **Geometry and duration:** metadata inspection, normal proxy
  generation and selected one-shot FrameEvidence commands have
  different limits. In particular the production worker uses a
  256 MiB source ceiling, while explicit video ingest permits up to
  1 GiB. Do not raise one cap and assume the others follow.
- **Playability:** a generated proxy is not the same as browser
  playback; WI-0152's controlled local/phone serving and seeking are delivered.
- **Search relevance:** a video remains visible in the Videos library
  even when not among search-eligible FrameEvidence revisions.

## WI-0207 source/quality separation (2026-11-07)

Standard 720p and Compact 540p now choose **output quality only**.
The worker determines which approved conversion applies from the
current source revision. The historic Rec.709 full-range 720p
source-specific encoding version remains available for existing jobs
but is no longer an operator quality option; saved legacy selections
migrate to Standard 720p. The strict staged full-range `pc`/BT.709
proof remains mandatory. Online-only processing requires independent
consent and an approved input policy; since PR #761 it may first hydrate
an eligible never-inspected placeholder for bounded inspection.
The one-video-step throttle is optional for both output qualities.
Real online-only verification was accepted under WI-0207 on 2026-11-08.

## WI-0210 delivered compatibility expansion (2026-11-04)

The maintainer accepted the first scoped `yuvj420p`
Rec.709 full-range **playable proxy**, using the
`rec709-fullrange-720p` profile. With local-only,
single-video-step processing, one new derivative was
published and played successfully. Sanitized job counts:
Succeeded 27→28, Queued 0→0, Failed 0→0, LastResult
`published`, without unexpected OneDrive hydration.
The 12 archive metadata candidates remain only potential
coverage; **1/12 has confirmed new production playback**
and no inference is made about the other 11.
No poster/frame/search, HLG or online-only support was
added by this work item. See the
[accepted one-step production runbook](video-fullrange-wi0210-production-acceptance.md).

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
single-gate projections isolated **12** Rec.709 `yuvj420p`
metadata candidates from the maintainer's production catalogue,
versus **22** HLG-transfer-only and **5** missing-rotation-only.
All 58 registered videos have **unknown** last-recorded source
availability, so no new locally playable clip count is known.
Checkpoint 3 adds only an **unregistered** Rec.709 full-to-limited
encoding evaluation contract with strict input/output colour checks.
It does not widen normal worker eligibility or create derivatives.
The next synthetic-only checkpoint adds `--full-range-evaluation` to
the pre-existing bounded, Windows Job Object-controlled synthetic pilot:
fixed full-range BT.709 test fixtures, independent source and output
FFprobe checks and scratch cleanup. This is **not** production video
admission and cannot establish real archive colour quality or local
availability. HLG transfer remains deferred. The procedure starts
on **main after merge**, with no duplicate local documentation checks.
The maintained-host synthetic trial **passed** on 2026-11-03:
720×406 with audio and 406×720 without, both 3-second encoded
H.264/AAC/Rec.709 outputs with no original access, no database and
no retained scratch. A live attribute-only availability check now found **12 of 12**
Rec.709 `yuvj420p` pixel-only candidates `local-metadata-match`
on maintained-host `photoidentity_main`, with no content opened
or OneDrive hydration. The earlier 58-video `unknown` values
were **last-recorded database observations**, not the current
file-attribute check. The next explicitly selected private UUID
shortlist still performs no media reading. This does **not**
prove source SHA, explicit full-range signalling or real colour.
The maintainer's checkpoint-6 private UUID shortlist also
passed with **12 candidates, 12 local matches**, no original
bytes opened and no OneDrive hydration. The next scoped proof
adds an **optional** exact `yuvj420p` pixel-only class
guard to the already established Windows no-recall SHA
source-proof command. Neither this metadata gate nor its
dry-run authorizes reading an original; the read requires
explicit separate `--read-local-bytes` approval. Even a
successful SHA-256 proof will not establish independent
actual `color_range=pc` or camera colour quality.
See [the one-revision source-proof runbook](video-fullrange-wi0210-source-proof.md).
**Checkpoint 7 real-original SHA passed** on one deliberately
selected, already-local exact Rec.709 `yuvj420p` candidate,
without hydration, jobs, worker changes or derivatives.
The current next step is a **single complete guarded real-media
evaluation**, including actual independent input range/colour
FFprobe, unregistered bounded conversion, limited-range
output validation, post-trial original SHA and operator visual
review; not further isolated preparatory commands.
See [the real-video evaluation runbook](video-fullrange-wi0210-real-evaluation.md).
**Real-camera source-probe update (2026-11-04):**
a maintained-host trial returned the precise code
`source-stream-layout-unsupported`, with no conversion or
published derivative. The experimental evaluation is being
narrowly revised to recognize only tagged `tmcd` timecode
and `gpmd` telemetry *auxiliary data* tracks, at most
one each and four total streams, while the existing fixed
FFmpeg map excludes all such data from output. This is
a hypothesis about the selected source, not a verified
stream inventory or a claim of working full-range colour;
real input colour/geometry, conversion and visual acceptance
are still pending. See
[the real-video evaluation runbook](video-fullrange-wi0210-real-evaluation.md).
**Most recent live source gate (2026-11-04):**
The third authorized real-file input probe failed safely with
`source-ancillary-stream-unapproved`. A `data` stream is present
but did not satisfy the vendor-code allowlist from PR #732.
This is **not** a proven Rec.709 colour defect, and no proxy
was produced. The next correction admits at most two declared
but unselected data streams, never maps their payloads,
suppresses global and stream-level metadata, independently
verifies the output, and keeps the exact Rec.709/full-range
and single-video/optional-audio requirements. No production
cohort or stage-delivered counts change until real visual
acceptance.
**Maintainer real-media acceptance (2026-11-04):**
after PR #733, a deliberately selected, already-local
`yuvj420p` Rec.709 source passed independent actual
full-range input FFprobe, limited-range H.264/AAC
720p output, original integrity recheck and human
visual/playback/colour/orientation/audio review.
One private review proxy was retained, then operator
cleanup; the experimental command wrote no database
receipt and published no durable derivative. This
is one accepted real example, **not** 12 production
playable proxies.

**Production slice now under review:** expose a
distinct `rec709-fullrange-720p` preset under
explicit local-only, one-video-step operator settings.
The photo-first queue selects only the exact
`yuvj420p` candidate class for this preset;
the normal SDR presets are unchanged. Before
production conversion, a pinned FFprobe independently
validates the lease/SHA-staged source and the exact
full-range BT.709, geometry, rotation, audio, container,
duration and ancillary-data bounds from the accepted
experiment. FFmpeg maps only validated A/V, strips
all metadata and data, and independent output probing
checks limited-range Rec.709 before lease-fenced
publication. Existing browser playback consumes the
registered derivative. No rollout/actual-playable count
is claimed before the [one-step operator acceptance](video-fullrange-wi0210-production-acceptance.md).

Historical note: the restrictions above applied before the final merged production integration. **After PR #734, this exact local-only Rec.709 full-range class is approved through its distinct opt-in profile; one published proxy has passed playback.**
See [the synthetic verification runbook](video-fullrange-wi0210-synthetic-acceptance.md)
and [the post-merge local availability check](video-compatibility-wi0210-local-check.md).
See the [WI-0210 census runbook](video-compatibility-wi0210-census.md).

## WI-0207 live acceptance and first-time OneDrive discovery (2026-11-08)

**Already-inspected online-only SDR video path: accepted on the maintained Windows
host** after PR #759. A supported Standard 720p proxy was published,
played and retained while its OneDrive original returned to online-only
unpinned (successful jobs 31→32). Shared CI, disposable PostgreSQL and
documentation checks passed. No changes to input-format acceptance were
needed for this demonstration.

**Pending follow-up (draft PR #761):** The first-visit workflow originally
could inspect only locally available MOV/MP4. An unknown online-only
placeholder had no verified content revision, and revision-locked
hydration therefore skipped it despite opt-in. The follow-up introduces
provisional source identity and the same shared managed hydration
budget, creates a real revision only *after* reading/hashing the
hydrated source, and uses the existing guarded output recipes when
metadata is supported. Unsupported or non-BT.709 files must remain
excluded from playable proxy production, and output quality remains
only Standard 720p or Compact 540p. Never-inspected online-only
acceptance is **not yet claimed**; defer new live-media verification
until the development PR's CI passes.

**Internal eligibility consolidation (draft PR #761):** Source eligibility
for queued and newly inspected video is routed through one
`VideoApprovedInputPolicy`, while independent Standard/Compact proxy
recipes remain unchanged. Format extensions must define an approved
source conversion and prove output before the shared eligibility
policy is widened; the UI never acquires a format-specific output
preset. The real first-inspection OneDrive outcome remains pending.

## WI-0207 completed: online-only first-inspection acceptance (2026-11-08)

The pending references to draft PR #761 above are historical.
**PR #761 merged** (commit `f91df89a9524ff4ace0e51da39e8f3f6ef5fa6a5`)
and all six final CI checks passed. On the maintained Windows PC,
the operator confirmed that a **never-inspected, online-only** original
using the **newly approved input-format class** was discovered,
hydrated, inspected and processed by Archive, then returned to
online-only. The published video played correctly in Photo Identity
with audio, pause, seek and acceptable colours. The previously
accepted SDR source test already established the current-revision,
pin-protected, proxy-after-release path. Together these close
WI-0207. The exact new input metadata was not included in this
operator report; no blanket source-format compatibility is claimed.

The shared `VideoApprovedInputPolicy` prevents per-quality selector
drift. **Standard 720p** and **Compact 540p** remain quality choices,
not source-format approval toggles. Further formats still require
an independently validated source-to-output conversion recipe and
explicit policy admission. HDR is not implicitly authorized.

