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

**Development started 2026-11-08 on the isolated
`wi-0212-video-details-metadata-people` branch.** A cohesive
implementation candidate introduces PostgreSQL v39 video-only audit
histories, a current/exclusion-guarded database-only Video Details API,
manual recording-time/Place/person edits and a responsive Video Details
page linked from Videos. Explicit GeoNames GPS lookup uses the already
configured provider, with no network lookup from page load. No
automatic background video geocoding is enabled. Original-media and
photo/biometric/collection state remain untouched by ordinary edits.
All work is **in progress**; neither CI nor final maintainer acceptance
has been claimed. See [the combined acceptance procedure]
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

## Out of scope

- WI-0153 manual photo/video collection membership and WI-0154 slideshow.
- Automatic recognition, face tracks, generated video-person suggestions
  or canonical biometric assignments from WI-0155.
- Bulk unapproved downloads, universal metadata decoding, audio
  transcription, video-level generative captions or cloud inference.
- Writing EXIF/QuickTime metadata into source files, treating filename
  patterns as factual place/capture time, or requiring every video to
  contain full camera/GPS metadata.

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

- Status: in_progress on the isolated implementation branch.
- Final local verification is deferred until all development and CI pass; schema v39 requires backup and restored-database rehearsal before production startup.
- Separate from WI-0153 collection membership and WI-0155 experimental
  face recognition.
