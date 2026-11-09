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

**Core implementation merged in PR #765, and the isolated v38 -> v39
rehearsal gate merged in PR #766.** On 2026-11-08, the maintainer verified
streaming-backup integrity, isolated restore, v38 -> v39 rehearsal,
production v39 startup, video details/people/GeoNames, online-only safety,
photo and video regressions. All reported gates passed. The work item remains
in progress for follow-up usability observations below rather than because
schema v39 acceptance is pending. GeoNames is explicitly triggered; browsing
metadata never reads video originals.
See [the combined acceptance procedure]
(../../operations/video-details-wi0212-acceptance.md).

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
catalogue remains at verified v39 pending the full metadata upgrade.

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

Production v39 must not open the new v41 application until
`catalogue rehearse-v41` passes on a **verified isolated v39 restore**:
the command applies both v40 and v41 in isolation, checks all preexisting
table row counts, empty new fields and empty new audit table, and confirms
idempotent initialization. The earlier v39 host acceptance remains valid.

**Samsung vendor metadata follow-up (2026-11-09):** ExifTool documents
`SamsungModel` in Samsung MP4 `smta` metadata and the maintainer observed
this exact tag on their example. The opt-in Samsung reader in the new
development PR requires a separately approved and SHA-256-pinned ExifTool,
reads only `Samsung:SamsungModel`, and uses a separate v41 audit action
for its tool/profile/changed fields. FFprobe-only remains the default.
The parser never infers missing tags or device identity; past camera
values remain untouched.

**Still pending final maintained-PC review:** restore a verified v39
snapshot to an isolated database and rehearse v40/v41 (no production
migration before the gate), verify a real local MOV's codec/audio/bitrate
fields, verify the pinned ExifTool fallback on a real Samsung MP4,
correct local recording-time edit/restore after PR #773, a file with
unknown metadata, and online-only/photo/video regressions. All metadata
refreshes remain one selected, current, hash-verified local revision at
a time. The user requested one consolidated local verification cycle
when metadata development is complete.

## Acceptance criteria

- [ ] Maintainer-approved current-video metadata inventory identifies
  existing projected fields, missing fields, extraction precedence and
  explicit unknown states; no unnecessary extraction is duplicated.
- [ ] Video Details accurately displays available date **and time**,
  GPS and resolved Place, make/model, orientation-correct resolution and
  duration; unavailable fields remain visibly unknown, not fabricated.
- [ ] Approved GPS-to-Place enrichment uses existing canonical hierarchy
  and configured provider/privacy/caching rules; a manual Place
  correction takes precedence, and non-GPS clips remain unguessed.
- [ ] Catalogue-only manual correction/clear for the selected metadata
  fields persists with provenance and leaves the original unchanged.
- [ ] A canonical person can be manually tagged and untagged as
  appearing in a video, including when no video face detection exists;
  merge/revision, duplicate and audit behavior is correct.
- [ ] The manual person tag creates **no** face observations,
  embeddings, review decisions, automatic identity matches,
  collection memberships or photo Smart Collection changes.
- [ ] Metadata view, manual edits and person tagging work with a
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

- Status: in_progress for the November 8 follow-up usability improvements.
- Maintainer v39 acceptance **passed**: isolated restore and schema rehearsal,
  production startup, real video metadata, manual time/Place/person edits,
  GeoNames lookup, online-only safety and regression checks.
- PR #774 (nullable bitrate/audio metadata v40) and #776 (targeted,
  audited v41 refresh) merged with green CI; optional Samsung ExifTool
  camera fallback in a separate draft PR, not yet locally accepted.
- Production v39 stays unchanged until the verified v39 -> v41 restore
  rehearsal and consolidated real-file Video Details acceptance pass.
- Separate from WI-0153 collection membership and WI-0155 experimental
  face recognition.
