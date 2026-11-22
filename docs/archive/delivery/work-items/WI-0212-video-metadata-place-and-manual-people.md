---
id: WI-0212
title: Expose video metadata, Places and manual person presence
milestone: M30
status_source: ../status/work-items.yaml
depends_on: [WI-0151, WI-0152, WI-0062, WI-0063, WI-0064]
related_adrs: [ADR-0012]
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Api, PhotoIdentity.Persistence.Postgres, PhotoIdentity.Web, docs]
---

# WI-0212: Expose video metadata, Places and manual person presence

**WI-0212 completed with maintainer acceptance on 2026-11-10.** Original
delivery PRs #765 and #766 passed the isolated v38 → v39 rehearsal
and 2026-11-08 acceptance. Follow-up PRs #774, #776, #777 and #782
delivered FFprobe bitrate/audio, the guarded v41 metadata refresh,
Samsung ExifTool fallback and an off-by-default persisted setting for
automatic Samsung enrichment during an operator-approved Archive
first inspection. The maintained PostgreSQL catalogue migrated to v41
only after a successful isolated v39 → v41 rehearsal. The maintainer
has accepted the new automated path and confirmed original-byte,
OneDrive, Places, People, playback and photo regression safeguards.
GeoNames is explicitly triggered, and browsing details never reads
originals. See the [acceptance runbook](../../operations/video-details-wi0212-acceptance.md).

## Priority and relationship (maintainer 2026-11-08)

**Near-term, independently deliverable video usability work.** Split from
WI-0153. Videos should present the ordinary file/capture facts familiar
from Photo Details, expose a useful named Place when source GPS is
available, and let the operator manually say which existing canonical
people appear in the *video*. This work neither requires nor implements
collection membership or face recognition from frames.

WI-0151 already stores a first technical/capture metadata projection;
this item must first inventory that actual PostgreSQL/API data and
identify missing fields before adding extraction. It must not claim
every source embeds reliable GPS, camera make/model or capture time.

## Metadata and location

- Show a friendly Video Details view for available **recording date and
  time of day**, GPS coordinates, first-class named **Place**, camera/
  device make and model, correctly oriented display resolution, duration
  and optionally frame rate, and clear unknown/not-provided states.
  Technical codec/rotation details may remain diagnostics.
- Reuse existing captured metadata/revision provenance from WI-0151.
  For MOV/MP4 source fields that are actually available but not yet
  projected, implement *bounded approved inspection* with documented
  metadata precedence, time-zone/UTC handling and source/confidence.
  Do not invent recording times from file modification or import time;
  any fallback must be explicitly distinguished from capture time.
- When valid embedded GPS exists, offer the same recognizable named
  Place behavior as photos: reuse the existing canonical Places
  vocabulary, GeoNames/reverse-geocoding configuration, cached results,
  rate limits, privacy safeguards and manual correction precedence.
  Missing coordinates do not authorize guessed Places. External lookup
  remains subject to the already approved/configured provider policy.
- Allow an operator to correct/set/clear applicable catalogue-level
  recording date/time, Place or other useful editable metadata as with
  photos, without writing back to the original. Make original metadata
  vs manual override provenance clear. Scope the exact editable fields
  to the maintained UI after inspecting current photo behavior.
- Metadata browsing, editing and Place lookup must not silently recall
  an online-only source. Any source-byte inspection is an independent
  explicit operator-approved path using existing size/revision/
  exclusion and OneDrive ownership guards.

## Manual people in a video

- Add video-level **manual presence** using the existing canonical
  People picker: choose a known person who appears somewhere in the
  clip, view the person, and remove that assignment. One person may be
  present even if no face is detectable.
- Reuse the *semantics* of WI-0062: revision-aware, audited add/remove
  presence, canonical active-person/merge safeguards and no duplicate
  effective assignment. Show these as **manually tagged people**, not
  confirmed face matches.
- Video-level manual presence records do not fabricate a frame/face,
  bounding box, embedding, face-review action or suggestion; do not
  train, calibrate or populate WI-0155's recognition gallery from this
  signal without separately authorized evidence policy.
- Presence is a statement about the **whole video**, not a timestamped
  interval. Time-localized person appearances, automated discovery and
  identity suggestions are separate future decisions after WI-0155.
- No new collection memberships, Smart Collection People filters or
  media-neutral search behavior are implied by saving a person tag.

## Suggested delivery order

1. Read-only audit of existing video capture data and photo-details
   metadata/People/Places contracts, including supported and absent
   source fields.
2. Deliver a single useful Video Details read/display path with
   correct date/time, resolution, device and GPS/Place states.
3. Add guarded video metadata corrections and canonical manual-person
   add/remove with audit and merge semantics.
4. Verify one or a few approved, representative real videos on the
   maintained Windows host, including missing metadata and an online-only
   source whose original is never recalled just for details.

## Maintainer follow-up observations (2026-11-08)

The accepted implementation exposed several presentation/metadata gaps on
real MOV and MP4 videos. These are tracked here without claiming a renewed
schema v39 migration or re-reading online-only originals:

- Show **recording time in the browser's local time zone** and edit it with
  `YYYY-MM-DD HH:MM` (browser-local date/time control). Convert the supplied
  local instant to an explicit offset before calling the existing UTC-safe API;
  reject ambiguous/nonexistent local DST times. When manually corrected,
  display **both effective and originally extracted recording times**. Hide
  raw `format:creation_time` provenance from the ordinary details grid.
- Render rational frame rates as human-readable **fps** (e.g. a ratio near
  29.92 becomes 29.92 fps) and identify portrait/landscape based on
  orientation-correct display dimensions.
- Expose the already persisted video and audio codec names (e.g. H.264,
  H.265/HEVC, AAC), pixel bit depth and HDR transfer signalling when known.
  Bit depth/HDR are source characteristics, **not objective image-quality
  scores**. For additional useful quality context, assess **video bitrate
  (Mb/s)**, with codec context; bitrate is not comparable across codecs or
  scenes on its own. Do not fabricate bitrate from resolution.
- Improve Place control: a manual clear is intentionally an audited override,
  **not** removal of that override. Provide a guarded explicit action to
  reinstate the most recent GPS-resolved Place from append-only history
  without a new network lookup or source-media read. Keep exclusion/current
  revision protections.
- A Samsung sample had `SamsungModel` (vendor-specific) visible to a
  metadata inspector but no projected camera make/model. An Apple sample
  correctly showed Apple and iPhone 15 Pro, so the issue is not universal.
  Assess whether the approved FFprobe metadata output exposes Samsung's tag;
  ExifTool's proprietary tag groups are not guaranteed FFprobe fields.
  Never infer make/model from filenames, codec, or device stereotypes.
  Existing catalogued video metadata will not change just because a parser
  gains a tag: selected, opt-in and guarded reinspection may be necessary.
- Audio channel layout (e.g. mono) and sample rate (e.g. 44.1 kHz) and video
  bitrate are **not currently persisted** in `video_revision_metadata`.
  Any durable new projection requires a separately reviewed schema migration,
  bounded source inspection on explicitly selected already-local videos,
  and new acceptance/rehearsal gates. Do not silently hydrate OneDrive files.

A focused follow-up branch implements the UI/local-time, formatting and
reversible GPS-Place changes using existing v39 tables. The metadata-field
extension and Samsung inspection remain pending separate approval/testing.

### PR #771 local verification defect and focused fix (2026-11-09)

The maintainer verified all PR #771 follow-up checks **except local manual
recording-time edit**. The browser localized a native `datetime-local`
input to a day-first display and the event value no longer matched the
string parser expecting `yyyy-MM-ddTHH:mm`, so saving a legitimate time
failed. The two-time metadata display and restoring the extracted value
could not be confirmed. The maintainer also requests the **nearest integer**
frame rate rather than fractional fps.

A focused no-schema-change follow-up replaces the native date picker with
an explicit `YYYY-MM-DD HH:MM` plain text field, converts a valid local
clock time to an offset-bearing timestamp, keeps DST ambiguity rejection,
rounds fps to the nearest integer and adds regression tests. Run targeted
local acceptance once merged; the production v39 migration was previously
accepted and must not be rerun for this UI-only fix.


- WI-0153 manual photo/video collection membership and WI-0154 slideshow.
- Automatic recognition, face tracks, generated video-person suggestions
  or canonical biometric assignments from WI-0155.
- Bulk unapproved downloads, universal metadata decoding, audio
  transcription, video-level generative captions or cloud inference.
- Writing EXIF/QuickTime metadata into source files, treating filename
  patterns as factual place/capture time, or requiring every video to
  contain full camera/GPS metadata.

## v40 technical metadata development (2026-11-09)

The next video-only metadata projection includes source **video-stream
bitrate (Mb/s)**, audio stream **channel count/layout and sample rate**
(e.g. `AAC · Mono · 44.1 kHz`). FFprobe's `bit_rate` is not an
objective picture-quality score; absent measurements remain Unknown and
are never inferred from container size or resolution.

Samsung make/model is accepted only from an explicit **FFprobe-visible**
`SamsungModel` or `com.samsung.model` tag. ExifTool's proprietary
Samsung metadata is not guaranteed to be exposed by FFprobe; the cited
Samsung sample may therefore still lack an approved value. No device
identity is inferred from codec, name or file modification time.

PostgreSQL v40 adds four **nullable** columns to
`video_revision_metadata` with bounded checks. The change does not
rewrite old metadata, original GPS/capture, manual video annotations,
frame evidence, photo data or face recognition. A guarded
`catalogue rehearse-v40` command requires a verified retained v39
restore and confirms every existing table row count, correct new
columns and idempotent migration.

**Outstanding dependency:** existing v2 inspections are immutable. A
separately authorized, selected-local-only revision/hash-verified
metadata refresh path must be implemented before old catalogue clips
show these values. The maintainer defers local review until the
end-to-end metadata workflow is complete. The v40 app must not be
started against production v39 before the isolated rehearsal is
accepted.

## Controlled historical metadata completion (development, 2026-11-09)

PR #774 merged with green CI, introducing schema v40 and FFprobe stream-
specific bitrate, audio channels/layout/sample rate and explicit Samsung
tag support. Existing v2 evidence remains immutable, and the maintained
catalogue subsequently migrated to verified v41 after an isolated
v39 → v41 rehearsal passed.

Merged PR #776 extends this with **v41 append-only refresh provenance** and
an explicitly selected, read-only-first `video metadata refresh` command.
It operates on exactly one current, already-local MOV/MP4 revision and
requires a named registered source root, revision UUID, approved FFprobe
path and SHA-256, localhost database selection and bounded tool execution.
Files must remain local and match the immutable size and SHA-256; current
revision/source exclusion guards are checked again under a source-row lock
before commit. `--apply --acknowledge-metadata-only` fills only missing
camera/bitrate/audio fields and transactionally records an audit action.
It never overwrites existing camera or capture fields, changes manual
time/Place/people, rescans arbitrary videos, or hydrates online-only files.

The production v39 catalogue was **not** opened by the v41
application until `catalogue rehearse-v41` passed on a verified
isolated v39 restore. The rehearsal checked row counts, default-null
new columns, the empty provenance table and idempotent schema
initialization; the maintainer then verified healthy v41 startup.

**Samsung vendor metadata follow-up (2026-11-09):** ExifTool documents
`SamsungModel` in Samsung MP4 `smta` metadata and the maintainer observed
this exact tag on their example. The opt-in Samsung reader in merged PR #777 requires a separately approved and SHA-256-pinned ExifTool,
reads only `Samsung:SamsungModel`, and uses a separate v41 audit action
for its tool/profile/changed fields. FFprobe-only remains the default.
The parser never infers missing tags or device identity; past camera
values remain untouched.

**Final maintained-PC review passed:** isolated v39 → v41
rehearsal, healthy v41 startup, real MOV/MP4 audio and video
metadata, rounded frame rate, manual capture-time correction/reset,
approved ExifTool Samsung fallback, two-field fill-only apply and
zero-field idempotent repeat, and photo/video/Places/People/online-only
regression checks. Historical refreshes remain explicitly scoped to
one locally verified, current revision.

## Follow-up: automatic Samsung camera metadata (accepted 2026-11-10)

Because many archive videos were recorded with a Samsung device, PR
#782 adds **Extract Samsung camera metadata when FFprobe does not
provide it** to Video Processing Settings, off by default and
persisted across restarts. The private launcher pins the physical
ExifTool executable by SHA-256. During an independently approved,
photo-first Archive video inspection, FFprobe runs first. Only
missing camera make/model values may be filled by the bounded
Samsung-only ExifTool reader, without overwriting conflicting fields.
An explicit CLI ingest can opt in separately; ordinary browsing and
OneDrive file arrival do not trigger extraction.

**Maintainer acceptance passed:** PR #782 merged with all six CI jobs
successful. Focused tests, launcher validation, ExifTool version 13.59
and its approved SHA-256, and healthy v41 app all passed. The new
setting was recognized, saved, survived browser refresh/restart,
and the one-video-step Archive option worked. A previously unindexed
Samsung video was automatically processed by Archive **without
manual metadata refresh**. Make/model, video/audio/fps, separate
ExifTool vendor audit, original-source SHA-256 preservation and
no unexpected OneDrive hydration passed. The option was successfully
disabled and re-enabled for normal use.

The 41-second clip displayed **Samsung / SM-G736B**, 2160 × 3840
portrait, 30 fps, H.264, 47.98 Mb/s, AAC stereo 48 kHz, 8-bit,
SDR (BT.709), recorded local 2024-09-04 09:58, with GPS Not provided
and Named Place Unknown. These are observed source facts, not a
general claim that every recording embeds GPS or Samsung model tags.

No background backfill, new database schema, extra OneDrive
hydration, automatically launched inspection on browsing, or
archive-wide scan was introduced. Previously indexed videos still
require the deliberate selected-video refresh for missing fields.
The acceptance procedure is retained in
`docs/operations/video-details-wi0212-acceptance.md`.

## Acceptance criteria

- [x] Maintainer-approved current-video metadata inventory identifies
  existing projected fields, missing fields, extraction precedence and
  explicit unknown states; no unnecessary extraction is duplicated.
- [x] Video Details accurately displays available date **and time**,
  GPS and resolved Place, make/model, orientation-correct resolution and
  duration; unavailable fields remain visibly unknown, not fabricated.
- [x] Approved GPS-to-Place enrichment uses existing canonical hierarchy
  and configured provider/privacy/caching rules; a manual Place
  correction takes precedence, and non-GPS clips remain unguessed.
- [x] Catalogue-only manual correction/clear for the selected metadata
  fields persists with provenance and leaves the original unchanged.
- [x] A canonical person can be manually tagged and untagged as
  appearing in a video, including when no video face detection exists;
  merge/revision, duplicate and audit behavior is correct.
- [x] The manual person tag creates **no** face observations,
  embeddings, review decisions, automatic identity matches,
  collection memberships or photo Smart Collection changes.
- [x] Metadata view, manual edits and person tagging work with a
  retained proxy or online-only original **without hydration**; photo
  features, video playback/search and existing source safeguards
  regressions pass.

## Verification requirements

Synthetic and disposable PostgreSQL API/storage tests plus maintainer
desktop/phone Video Details review on a few explicitly approved clips
(with/without GPS and embedded camera/capture fields). Verify current
revision fencing, exclusions, manual-place priority, canonical-person
merge and zero original bytes read during ordinary view/edit paths.
No private GPS/people/video identifiers in Git/CI or reports.

## Completion notes

- **Status: completed; verified by the maintainer on 2026-11-10.**
- PRs #765/#766 passed original v39 delivery, migration and maintained-host
  acceptance. PRs #774/#776/#777 passed the v40/v41 metadata extension,
  backup/restore rehearsal, read-only-first Samsung extraction, idempotency
  and photo/video regression verification.
- PR #782 merged with green CI (six jobs). On the maintained PC, the
  default-off, saved Samsung metadata setting automatically populated
  camera make/model on a previously unindexed Samsung video during
  ordinary Archive processing. The separate ExifTool audit, original
  SHA-256, metadata presentation, source protections and opt-out/re-enable
  all passed; no per-video refresh CLI was invoked.
- No new migration is needed; PostgreSQL remains at verified v41. No
  automatic backfill, additional online-only recall or ordinary browsing
  media reads are introduced.
- Collection membership (WI-0153), video slideshow (WI-0154), video
  face recognition (WI-0155) and video descriptions/search (WI-0213)
  remain separate future work.
