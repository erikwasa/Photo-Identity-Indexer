---
id: M30
title: Video media support
status_source: ../status/milestones.yaml
depends_on: [M19, M22, M24, M27]
---

# M30: Video media support

**WI-0212 development handoff — 2026-11-08:** The video-only
metadata, Place and manual canonical-person vertical slice is being
developed on `wi-0212-video-details-metadata-people`. It adds v39
revision-bound PostgreSQL action history, guarded video-details APIs
and a responsive Details editor without original-byte access.
Configured GeoNames lookup is operator-triggered rather than an
unreviewed background video GPS worker. Final CI/disposable PostgreSQL
and maintained-PC UI/online-only acceptance are pending. Verify a
recoverable backup and rehearsal before running production migration.
See [WI-0212 acceptance](../../operations/video-details-wi0212-acceptance.md).
WI-0155 remains isolated and independent.

**Scope clarification — 2026-11-08 (after merged PR #763):** Split the
formerly combined metadata/collection WI-0153. [WI-0212](../work-items/WI-0212-video-metadata-place-and-manual-people.md)
is a new **in-progress / near-term** video-details item for useful file/capture
metadata, GPS-to-existing-Place resolution, manual corrections and
**manual video-level canonical-person presence**. It is independent of
collection membership and of WI-0155 biometric recognition. Video
metadata inspection already exists in WI-0151, so WI-0212 begins with
a current catalogue/UI field audit rather than a second extraction pipeline.

[WI-0153](../work-items/WI-0153-video-collection-membership.md) now means
**only explicit manual photo/video collection membership**; WI-0154
remains the separately deferred mixed-media slideshow. Both are
proposed, not automatically authorized by implementing WI-0212.

[WI-0213](../work-items/WI-0213-video-level-description-and-search-review.md)
is a **proposed/deferred follow-up** to revisit video-level descriptions,
captions and Video Search when WI-0155 multi-video face evidence is mature.
Completed WI-0190/0191 deliver *sampled still-frame evidence*, not
confirmed whole-video understanding. A future experiment should
compare temporal/clip summaries against frame captions and evaluate
more frames, costs, uncertain action claims, scene coverage and
per-video ranking without changing current photo-search defaults.
No production 180-frame requirement or new recognition policy is implied.

M30 stays `in_progress` while WI-0155 runs in its isolated
`wi-0155` branch. WI-0212 is being developed separately;
WI-0213 intentionally waits. Other deferred membership/slideshow
items will still require a deliberate M30 closeout decision. The
dated checkpoints below preserve historical decisions.

**Current M30 handoff — 2026-11-08, after merged PR #762:** The basic
video foundation is accepted: discovery/posters (WI-0150/0151/0211),
bounded opt-in processing/proxies (WI-0188/0189), caption/embedding
evidence and Video Search (WI-0190/0191), desktop/phone playback
(WI-0152), one archive-driven format expansion (WI-0210) and safe
opt-in real OneDrive hydration/release (WI-0207). The original
online-only file returned online-only in the accepted local examples;
this does **not** claim universal format or workload coverage.

**The active M30 item is WI-0155**, a bounded read-only video face
feasibility experiment on the long-lived `wi-0155` branch. It is
`in_progress` but neither the prototype nor its evolving experimental
documents are merged into `main`. It requires additional expressly
approved local videos, person-level human evaluation against eligible
existing catalogue evidence, unknown-person cases and a late
integration-versus-separate-component decision. No automatic identity
assignment is authorized.

**WI-0153 (explicit mixed-media membership) and WI-0154
(mixed-media slideshow) remain intentionally deferred**, not completed.
Their scopes are distinct from new near-term metadata/manual-person
WI-0212; dynamic video Smart Collection queries still need a
separate authorization. M30 remains `in_progress`; deferred items
must be explicitly implemented or rescheduled at a future closeout.
Dated checkpoint notes below are historical.

**WI-0207 full-local-review candidate — 2026-11-07:** The maintainer
accepted checkpoint 4 (35 focused integration tests, 1 disposable
PostgreSQL test, docs validation, PR #754 merged). The next consolidated
PR makes uninspected online-only metadata discovery local-only
to avoid earlier unsafe pre-revision +p/-p operations, and
replaces supported-revision video-only +p hydration with the Windows Cloud Files
full-range transient hydration API, without changing photo hydration;
adds a source-locked/no-pending-job release gate that blocks new
enqueue/restart while a release is outstanding; and requests +u
only for existing app-owned, currently unpinned video originals.
Release is attempted after work or during bounded operator-started
Archive cleanup/retry. Known shortcuts are explicitly documented
(Windows-only Cloud Files support, no background cleanup daemon,
small pin-state race, preservation of old ambiguous +p receipts).
**The next required checkpoint is the maintained user's one
explicitly approved real online-only video local review.**
WI-0207/M30 remain in progress until that test passes; no real
OneDrive file is altered by CI.
See docs/operations/video-onedrive-wi0207-full-local-acceptance.md.



**WI-0207 acceptance update — 2026-11-07:** PR #753 merged;
the maintainer confirmed 35 focused integration tests passed,
one disposable PostgreSQL release-receipt test actually executed
and passed, plus documentation `validate` and
`generate --check`. The separate PowerShell `else`
parsing error after the successful test run is not a test
failure. Checkpoint 4 is accepted, without any new media
transfer authorization. Safe race-free dependent-job release
coordination, app/user pin ownership distinction and the
operator-approved real online-only video acceptance remain
open. M30/WI-0207 are still in progress; keep online-only
videos OFF.


**WI-0207 update — 2026-11-06:** PR #750's managed photo/video
admission was accepted locally (30/30 synthetic integration
tests, 1/1 disposable PostgreSQL lease test); PR #752's
documentation acceptance then passed both documentation checks
and all five canonical status assertions. Checkpoint 4 is
under development to restore managed-local video lease
ownership across restart/retries, and to reconcile at most
16 **already requested** video release receipts only when
Files On-Demand reports online-only/unpinned. No OneDrive pin,
unpin, hydration or media read is part of this checkpoint.

WI-0207 is **not completed**: safe eventual app-pinned release,
race-free dependent-job coordination and one explicitly authorized
real online-only video acceptance remain open. Keep online-only
video processing OFF until those gates pass.


**Current M30 checkpoint — 2026-11-05: WI-0207 IN PROGRESS.**
WI-0211's complete video posters and default-off automatic poster
setting are maintainer-accepted and archived (PR #746 merged).
The next selected unblocked priority is
[WI-0207](../work-items/WI-0207-opt-in-onedrive-video-hydration-and-release.md)
before deliberately deferred metadata/collection WI-0153 and slideshow
WI-0154.

The first WI-0207 development slice is a bounded **read-only
OneDrive Files On-Demand preflight**, checking at most 64 current
supported, nonexcluded video revisions without published proxies.
It reports only aggregate online-only/pinned/local/error counts,
queued/running dependent-job evidence and existing managed hydration
receipts; no original bytes, files, source IDs, FFmpeg, OneDrive pin or
release command, processing job or PostgreSQL migration are touched.
See [the local Windows preflight](../../operations/video-onedrive-wi0207-readonly-preflight.md).

The existing video worker's unconditional per-attempt release of
just-hydrated originals has **not yet** been proven compatible with
retention for queued/active dependent video jobs or user pinning.
This is the next safety implementation gate, followed by one
separately authorized real OneDrive video test. Do not enable
online-only video work merely for checkpoint 1. M30 remains open.


**Historical 2026-11-04 scoped follow-up — WI-0211 proposed at the time:** The
maintainer's Videos screenshot shows widespread indistinguishable
dark `Video` placeholders despite a successful new
production playable proxy under completed WI-0210.
WI-0151's completed manual, durable, original-backed
poster path does not automatically provide adequate
poster coverage for newly registered playable proxies.
[WI-0211](../work-items/WI-0211-recognizable-video-posters-and-library-browsing.md)
will deliver a separately authorized, bounded and
versioned still-poster path preferably from **already
verified local playable proxies**, together with useful
Videos browse controls and clear fallbacks. Opening the
Videos page must never itself process, hydrate or
download an original. The work item is proposed, not
permission for bulk media reads.

A separate targeted Settings UI correction addresses
oversized/misaligned checkboxes, preserves the status
panel during refresh to avoid scroll jumps, keeps Save
and its feedback in view, and collapses the advanced
manual proxy-retention action. It does not change
processing permissions, original access or cache
deletion semantics.

**2026-11-04 WI-0210 production expansion — maintainer ACCEPTED and completed:**
PR #734 merged after six passing CI jobs. One deliberately
selected locally available Rec.709 full-range `yuvj420p`
video was converted, registered and actually played through
Photo Identity using the explicit local-only, one-video-step
720p profile. The maintainer confirmed visual playback PASS
and no unexpected hydration; sanitized job results:
Succeeded **27→28 (+1)**, Queued **0→0**, Failed **0→0**,
LastResult `published`. The earlier independent full-range
BT.709 source probe, limited-range output, integrity and
colour/orientation/audio review also passed. This
**completes WI-0210**, not all of milestone M30.

The archive survey identified **12** pixel-only metadata
candidates locally matched; only **one additional production
playable proxy is now verified**. The other 11 are potential,
not production success claims. Standard SDR/compact encoding,
HDR/HLG, posters, searchable frames, absent rotation and
online-only release/hydration remain separate boundaries.
M30 remains open and is now `ready` under the canonical status rules (other M30 work items remain proposed), including WI-0207. See the
[final acceptance](../../operations/video-fullrange-wi0210-production-acceptance.md)
and [completed WI-0210](../work-items/WI-0210-archive-driven-video-compatibility-expansion.md).

## Scheduling status

**Current handoff, 2026-11-03:** PR #720 merged; WI-0152's
maintainer-accepted desktop/phone playback has been completed and archived.
All five scoped criteria passed on the maintained Windows host; the
native-player clipping regression was fixed and separately verified.
No repeat playback or online-only original hydration check is requested
merely to close that item.

**WI-0210 in progress:** PR #721's read-only PostgreSQL census has
merged and all seven maintainer-host checkpoint-1 checks passed, including
disposable PostgreSQL. The actual catalogue has 58 nonexcluded current
videos; 26 fail the transfer gate first, 13 pixel format, 5 rotation,
1 inspection, and 13 are metadata-eligible. Receipts: 13 retained
proxies, 3 frame batches, 1 still poster. These are not a census
of the historical 1,309 source-enumerated archive files.

**Selected next engineering target, not yet authorized production
admission:** Rec.709 full-range `yuvj420p` input for a playable-proxy
profile, bounded by read-only isolated-gate and availability projections.
PR #722's production-catalogue survey now confirms **12** isolated
Rec.709 full-range pixel-only candidates, **22** HLG-transfer-only
and **5** missing-rotation-only. All **58** have unknown
last-recorded source availability. The candidate comparison selects
the small Rec.709 full-range playable-proxy **evaluation** first;
the next checkpoint adds a versioned unregistered encoding contract.
Maintain the existing Rec.709 SDR production proxy gate until a
representative already-local original demonstrates correct colour
conversion and the maintainer explicitly approves that class/stage.
HLG, unspecified transfer and missing rotation are not silently admitted.
PR #723's isolated format-contract work is merged. The next
bounded development checkpoint is a real FFmpeg/FFprobe roundtrip on
two **generated** full-range synthetic clips under existing Windows
tool containment, without database or original-media access.
PR #724 merged and the maintained-host synthetic verification
**PASSED**: the Windows pinned-tool trial produced 720×406 H.264/AAC
landscape with audio and 406×720 portrait without audio, both
3 seconds, and removed the entire marked scratch after reporting
no original, database or derivative access. CI owns documentation
validation; local review starts **after PR merge on main**.
This establishes synthetic format safety, not real camera colour
quality or local availability. The maintainer's actual live **attribute-only** availability
check on production `photoidentity_main` now shows
**12 of 12** isolated Rec.709 `yuvj420p` pixel-only candidates
`local-metadata-match`, with no original content access or hydration.
A new explicitly opted-in private UUID-only selection command
is the next bounded step; SHA-256 proof and real-camera colour
conversion still require separate operator authorization.
See [the post-merge selection runbook](../../operations/video-compatibility-wi0210-local-check.md).
**Checkpoint 6 accepted (2026-11-03):** After PR #727 merged,
the private read-only candidate shortlist returned 12 candidates
and 12 locally matched, with no source content opened or hydration.
The new checkpoint adds only an optional exact-class precondition
to an existing no-recall source-integrity command. A real original
read still requires separate operator authorization; no FFprobe,
transcoding, proxy publication or worker gate changes are approved.
See [the guarded one-revision proof](../../operations/video-fullrange-wi0210-source-proof.md).
**Checkpoint 7 accepted (2026-11-04):** The maintainer
separately authorized and verified a no-recall SHA-256 read
of **one** exact-class real original; it matched its
registered revision, stayed metadata-stable and requested no
hydration or processing. The next implementation is a **single
guarded real-video evaluation vertical slice**, with independent
input/output probes, bounded conversion, original recheck and
one post-merge visual acceptance; then production integration
only upon approval. This follows the
[agent vertical-slice guide](../../operations/agent-vertical-slice-delivery.md)
and [single real-video acceptance runbook](../../operations/video-fullrange-wi0210-real-evaluation.md).
Full-range production admission still requires approved already-local
real source integrity, visual conversion and playback checks.
See [the synthetic acceptance runbook](../../operations/video-fullrange-wi0210-synthetic-acceptance.md).

See [the census runbook](../../operations/video-compatibility-wi0210-census.md).

M30 remains in progress. WI-0207 opt-in OneDrive hydration/release is
separate and later. WI-0153/0154 metadata/mixed collections/slideshow
work is deferred. WI-0155 face feasibility remains an experimental
prototype; WI-0156 Live Photo pairing is retired. Earlier WI-0150/0151,
WI-0188/0189, WI-0190/0191 acceptance remains valid.

The dedicated opt-in Video Search view and API are accepted for the
existing three current, already-local supported SDR videos. The
maintainer verified useful frame descriptions and timestamps, all
three retrieval modes, photo-search isolation, and an unmatched
read-only Caption search with no processing counter changes.
Catalogue eligibility and exclusion controls were also tested in
disposable PostgreSQL. A physical original-file existence check on
each search and representative larger-video relevance benchmarking
were explicitly **excluded from the accepted scope**, not passed.
Poster generation for two missing stills remains deferred. Photo-first,
local-first, media privacy and exclusion rules stay in force.

## Current product priorities and scope review — 2026-11-02

**Current delivery gate:** locally accepted WI-0152 basic browser
playback of already retained H.264/AAC proxies needs PR #720 merge
and work-item archival. It reuses guarded APIs and trusted-LAN
HTTPS: an online-only original stays online-only, missing proxies
are unavailable, and playback/seek does not enqueue processing.

**Then:** WI-0210 first archive-value-driven compatibility expansion,
selecting one supported input class from a read-only catalogue survey;
follow with WI-0207 real-OneDrive opt-in hydration/reuse/release
acceptance. A processing download remains local while approved
pending/active video work still needs it and may be released under
the existing app-owned cache policy after the last use; originally
local or user-pinned content remains local. The existing video path
already has opt-in staging, but cross-job retention must be checked,
not assumed complete.

**Later / conditional:** WI-0153 simple metadata and explicit
mixed-media collections (both deferred for now; video-enabled Smart
Collection filters require a separate future workitem); WI-0154
mixed slideshows after playback **and** a collection-selection
contract; WI-0155 initially bounded video-face recognition prototype
(possibly separate experimental repo, no canonical DB writes), with
full tracking/review production work scoped only after the prototype.
Video face recognition is a desired product capability, but not a
prerequisite for basic playback. WI-0156 Live Photo pairing is
**retired without implementation** because it is not wanted now.

**Do not silently revive rabbit holes:** automatic Azure video
offload, mandatory AppContainer/kernel isolation, extensive mixed
load or cost benchmarks, compulsory full-archive hydration,
destructive recovery experiments, universal formats, or unrequested
Smart Collection redesign. Keep the important protections for
immutable originals, exclusion/purge, private derivative access,
managed hydration ownership, source/revision fencing, limited
resources and vetted operator-controlled Azure inference.
[ADR-0012](../../decisions/ADR-0012-isolated-video-media-architecture.md)
and [ADR-0013](../../decisions/ADR-0013-optional-operator-controlled-remote-compute.md)
reflect this proportionate interpretation.

**Video compatibility is staged**, not a single global support bit.
See [the supported-by-stage matrix](../../operations/video-compatibility-matrix.md):
catalogued → poster → locally playable proxy → searchable frame
evidence → optional video face evidence. The present normal shared
video worker accepts a narrower Rec.709 SDR subset (including a
256 MiB source cap) than metadata inspection. WI-0210 will pick the
next expansion from actual archive counts, rather than guessing
formats or assuming all 1,291 historically online-only samples
need processing.

This section supersedes *scheduling and remaining-requirement*
statements in the chronological checkpoints below. Historical
“pending” wording records the state of the earlier PR, not current
completion status.

## WI-0191 scoped maintainer closeout (2026-11-02)

WI-0191 is **completed by explicitly revised acceptance B**, with
five **scoped** criteria accounted for, not five original aspirational
criteria universally tested. The maintainer's final harmless unmatched
Caption search returned no unrelated matches, unchanged video job
counters (queued, running, succeeded, failed, cancelled and expired
leases), 3 candidate videos and no candidate truncation. The maintainer
judged current descriptions/timestamps useful. Prior local
photo/video isolation and disposable PostgreSQL eligibility tests
passed. No source files were read, restored or hydrated by this
closeout verification.

**Deferred triggers:** Reassess physical media availability if a
stale catalogue entry or user-visible access issue occurs, and revisit
rank relevance and the 256-candidate limit when a useful larger,
more varied supported video cohort exists. The absent posters are
not part of search acceptance. M30 is **ready** for separate future
video work, not finished. See
[WI-0191](../work-items/WI-0191-video-semantic-caption-search.md).

## WI-0191 video-search backend checkpoint (2026-11-02)

A separate `POST /api/video-search/query` opt-in endpoint now has a
bounded read-only current-frame-evidence PostgreSQL reader, local CLIP
text matching when model provenance is compatible, simple caption matching
and per-revision reciprocal-rank fusion. Existing photo search and saved
photo workflows are unchanged. This is an initial backend checkpoint,
**not** completed video search: browser UI, larger corpus and maintained-host
acceptance remain. No source decoding, hydration or background work is
authorized by a search request. See
[WI-0191](../work-items/WI-0191-video-semantic-caption-search.md) and
[operator verification](../../operations/video-search-backend-checkpoint.md).

## WI-0191 dedicated video-search browser checkpoint (2026-11-02)

A separate `/video-search` page is implemented for already published
FrameEvidence with Visual/Caption/Combined modes, matching frame
timestamp, caption and evidence provenance, and validated existing
poster routes with a missing-poster fallback. The Video Library opt-in
gates the controls and the backend still requires that flag. Navigation
is separate from photo Search, which retains its existing contracts,
scope and saved selection workflows. This is a browser-development
checkpoint **pending Windows CI and human UI review**, not WI-0191
completion; no video playback/Smart Collections, source hydration or
automatic search-time processing is introduced. See
[WI-0191](../work-items/WI-0191-video-semantic-caption-search.md).

## WI-0191 search eligibility checkpoint (2026-11-02)

A disposable PostgreSQL regression matrix is being added to the
existing frame-publication test: current supported FrameEvidence remains
searchable; unsupported/HDR, soft-deleted, superseded, non-local-source
and inconsistent frame-count evidence is suppressed, with the existing
source-exclusion purge test retained. No maintained database, live video
source or poster workflow is touched. The maintainer has deferred
creation of two missing posters. This is an automated safety check
pending Windows CI, **not** proof of actual source-file availability
or the remaining photo-ranking/large-library criteria.

## WI-0191 photo-search preservation accepted (2026-11-02)

PR #716 merged after full Windows CI passed, including disposable
PostgreSQL mixed-media search and saved-photo-collection isolation.
The maintained-host read-only Photo/Video/Photo smoke independently
returned 10/10 photo hits, 3 distinct video hits, identical photo
ordering/scores and counts, unchanged membership of one saved photo
collection and disjoint photo/video revisions. Photo elapsed values
were 451.9 and 410.6 ms; these are not historical latency baselines.
WI-0191 is now **3/5 scoped criteria verified** and remains
in_progress. Physical original availability and broad realistic
video-set relevance are not demonstrated; poster creation is deferred.

## WI-0191 photo/video coexistence checkpoint (2026-11-02)

Extend an existing disposable PostgreSQL photo search fixture with
published synthetic video FrameEvidence, then assert photo result
membership, rank/component scores, photo catalogue counts and saved
photo-list collection order are unchanged while the video remains
searchable only through the opt-in Video Search boundary. A
read-only maintained-host Photo / Video / Photo query check is also
available. This is a bounded photo-preservation regression awaiting CI
and optional human smoke, not proof of pre-upgrade latency equality.
Poster generation remains explicitly deferred.

**WI-0190 current maintainer verification (2026-11-02):**
Two different supported short/rotated published SDR proxies have
each produced **four durable timestamped frame captions and
512-dimensional CLIP embeddings**, with successful read-only
current-v38 audits, normal bounded scratch cleanup and disabled
video controls after execution. The combined acceptance report
found 8 frames, 16,996 caption/vector payload bytes,
266,321 ms total measured execution wall time
(1.802 observed frames/minute). Human review judged 7 of 8
captions accurate (87.5%); one dark frame caption invented
a held object. The operator then ran local current-v38
**video frames evaluate-live** with two manually judged
queries, one different relevant video per query. All three
CLIP ranking policies returned Hit@2, MRR@2, Recall@2,
MAP@2 and nDCG@2 of 1.00, with exit 0 and no external
AI service requests. This establishes a **limited two-video
functional retrieval baseline**, not general search quality.

**WI-0190 scoped maintainer acceptance (2026-11-02):**
The maintainer accepted the **currently functional local SDR
FrameEvidence slice** and explicitly rejected extra acceptance
work that would make completion disproportionate: full scene-diversity
verification until wider video formats are admitted, detailed
CPU/GPU/Azure cost accounting while monthly credit remains sufficient,
performance investigation while the workflow is acceptably fast,
and a destructive hard-interruption rehearsal. These are not
claims that the postponed cases passed. Existing admission, privacy,
atomicity, cancellation and temporary-file safeguards stay intact;
the existing PostgreSQL backup/restore system remains a recovery
fallback, not proof of an actual forced-kill scenario.

The current evidence supports completion of **WI-0190's revised,
maintainer-approved scope**: durable 4/4 frame evidence on two
distinct current proxies, independent v38 audits, 7/8 human
caption judgments, 2-query/2-video CLIP first-rank relevance,
normal cleanup, previously passing Windows tests and
documentation validation. Revisit costs if the monthly Azure
credit is insufficient, performance if it becomes slow, and
scene quality when new supported video types/real user reports
warrant further work. Broader positive HDR/long/full-range/
multi-scene/low-light coverage remains deferred, not approved.
See [WI-0190 maintainer-scoped closeout](../work-items/WI-0190-video-frame-caption-embedding.md#maintainer-closeout-decision--pragmatic-scope-2026-11-02).
**M30 remains open with lifecycle status `ready`** because
other independent video work items remain proposed or ready.

## WI-0190 maintained-host video-coverage deferral (2026-11-01)

The maintainer confirmed the legacy 720p queued jobs were runnable with
the approved matching FFmpeg digest and preset: the video queue cleared
without failures, and the user-visible completed count increased to 23.
A subsequent read-only PostgreSQL query found 24 published proxy receipts
across the 540p/720p profiles (different query scopes/snapshots; do not
assume these counts are identical). Longest published source: 49.4 seconds.
Metadata inspection includes 25 HLG videos and several longer Rec.709
sources rejected by the current proxy pixel-format/rotation policy.

For WI-0190, **do not increase video input support merely to satisfy the
full representative-clip list**. The supported short/rotated SDR proxy
cohort remains available for current frame-caption/embedding, scene,
retrieval, throughput and cost testing. Positive evaluation on HLG,
`yuvj420p`, NULL-rotation, longer/larger or other unsupported inputs
is **postponed, not accepted** until the respective admission/format
capabilities expand and are verified. Missing genuine landscape, low-light
or multi-scene footage is similarly postponed individually if no usable
supported clip is found. Synthetic long/scene sampling, explicit
unsupported-input rejection and all privacy/atomic-publication/worker
guards remain mandatory.

[WI-0190's maintained-host coverage matrix](../work-items/WI-0190-video-frame-caption-embedding.md#maintainer-decision--postponed-representative-video-coverage-2026-11-01)
records the reopen triggers. WI-0190 is accepted for the **narrow current SDR scope**;
M30 remains **open (`ready`)**, and postponed checks are not
claimed to have passed. Any broader proxy/
video-input work must be separately scoped in a repository workitem,
not activated as an implicit change to WI-0190.

## WI-0190 registered-proxy model pilot (2026-10-31)

A third checkpoint offers an **explicit opt-in, read-only** evaluation
of one previously registered and SHA-verified WI-0189 *already-local*
SDR proxy. It executes the existing CLIP image encoder and local Ollama
caption client for at most four timestamped frames, captures model and
policy provenance, and can compare max/top-k/pooled CLIP scores without
printing caption, vectors, private query, source paths or media identifiers.
No originals/OneDrive, PostgreSQL updates, new job or background processing
are involved. **WI-0190 0/4 acceptance boxes** remain open; local model
compatibility/cost evidence is pending a bounded maintainer pilot after CI.
Permanent FrameEvidence job publication and invalidation are not delivered.
See [pilot procedure](../../operations/video-registered-proxy-frame-pilot.md).

## WI-0190 synthetic frame-decoding and evidence contract (2026-10-31)

WI-0190 remains `in_progress`. Its second checkpoint adds a fixed local
SDR JPEG extraction profile and a disposable two-clip synthetic pilot using
the existing hash-pinned, Windows-constrained FFmpeg runner. The output
is validated through the existing photo JPEG/BGR24 decoder. An in-memory
revision/model/policy/timestamp evidence validator rejects partial and stale
batches. None of these operations activates video processing, accesses family
videos, writes catalogue evidence, runs CLIP/caption models or serves search
results. Four WI-0190 acceptance criteria stay open. The planned guarded
real-frame worker, model compatibility, PostgreSQL atomic provenance and
private quality/cost verification remain next. See
[the operator runbook](../../operations/video-frame-evidence-sampling.md).

## WI-0189 final maintainer acceptance (2026-10-30)

**Completed scope, 4/4 work-item acceptance criteria checked** under
[WI-0189](../work-items/WI-0189-video-local-low-resolution-proxies.md).

- Maintainer reported PASS: focused .NET tests and docs generation/check;
  Standard 720p and Compact 540p synthetic landscape/portrait runs with
  optional audio and clean scratch; Settings restart persistence; one-video-step
  Archive limit; supported video seeking, aspect/rotation and audio; original
  SHA-256 immutability; revision/preset reuse; PostgreSQL quota/retention tests;
  **zero online-only hydrations**.
- Three real already-local MOV/MP4 proxies completed. Anonymous sizes:
  540p **14.36 → 0.07 MiB (99.5% smaller)**; 720p
  **2.36 → 1.53 MiB (35.3%)** and **20.68 → 0.74 MiB (96.4%)**.
  The maintainer judged processing time satisfactory and 540p visually
  sufficient. The three inputs differ, so these figures do **not** support a
  paired-source profile comparison. Numeric real encode latency was not
  captured.
- Keep **Standard 720p as the shipped default**. Compact 540p is an accepted
  opt-in choice. This avoids treating unmatched size data as proof that 540p
  should become the universal default. Both browser-oriented H.264/AAC MP4
  presets passed the approved synthetic FFmpeg/FFprobe checks; in-app
  video streaming/range playback remains WI-0152.
- The maintainer accepts **manual preview-and-confirm eviction** in lieu of
  unattended deletion. No actual removal was performed against the personal
  cache; isolated PostgreSQL revocation/selection/integrity tests passed.
  Registered-byte quota enforcement is not a hard physical filesystem quota
  for unregistered/orphan files, which remains an explicitly documented
  limitation rather than an asserted guarantee.
- Actual OneDrive-online-only video hydration/release was never exercised
  and is excluded here; WI-0207 owns those opt-in tests separately.

The subsequent checkpoint sections document historical implementation states.
Earlier references to pending PR #671 CI or open WI-0189 acceptance are
superseded by this final maintained-host acceptance.

## WI-0189 maintainer-selected retention closure and review handoff (2026-10-30)

The maintainer confirms that **manual, explicit, preview-and-confirm
proxy eviction is sufficient**, so WI-0189 does **not** require an
unattended eviction scheduler. The existing private registered-byte
quota and per-file publication fences, configurable protection age
(48 hours / 7 days / 30 days), and stopped-lane, database-first
recovery remain the required scope. The quota is registered-byte
accounting, not a physical filesystem limit; no hard disk-quota
guarantee is claimed. The maintainer's decision pertains only
to *manual versus unattended deletion*, not to accepting physical
space accounting as equivalent; the known capacity limitation
remains an explicit review item.

PR #670 merged with successful GitHub Windows CI. PR #671 adds a default-off **Stop after one video step per
Archive advance** option (one inspection or one queued attempt,
after ordinary photo-first work). This preserves old behavior
unless explicitly configured, and avoids uncontrolled multi-video
acceptance runs. The PR also adds
`--profile compact-540p` to the existing synthetic,
source-free/temporary-only Windows FFmpeg+FFprobe pilot so both
presets can be preflighted before any personal media is used.
At this checkpoint WI-0189 entered `in_review`; the subsequent
maintainer evidence summarized above completed its bounded local
scope. WI-0207 independently owns OneDrive-only hydration/release,
and does not block the accepted already-local proxy requirements.

## WI-0189 versioned bounded video proxy profiles (PR #670, 2026-10-30)

PR #670 introduces the second, strictly whitelisted compact **540p**
H.264/AAC SDR software encode profile, retaining the existing exact
720p preset and behavior as default. The same Settings checkpoint adds
user-controlled **48-hour, 7-day or 30-day minimum retention age**
for the already manually confirmed stopped-lane cleanup; legacy
preferences retain 48 hours and no eviction runs automatically.
The browser preview/confirm retention endpoints are also restricted
to direct loopback sockets on both sides, independent of optional
trusted-LAN app exposure. Video Settings persists the
profile and blocks changes while an attempt is active. Job identity,
pending work and queued work now use the selected preset version
and approved FFmpeg hash: old queued jobs are not processed under
the wrong profile, and old completed proxies are never silently
rewritten or deleted. Independent FFprobe validation bounds output
dimensions to the selected profile; all archive/photo priority,
revision/exclusion/lease and original immutability boundaries remain.

PR #670 passed GitHub CI; maintained-host representative
media acceptance is still pending. The compact profile is a candidate only; no
claims are made that it achieves better quality/size/performance.
Automatic retention and hard physical disk quota are still absent.

## WI-0189 confirmed, operator-only retention checkpoint 5 (2026-10-30)

PR #669 adds a **manually confirmed**, small-batch proxy cleanup workflow
under Video Settings. It reuses the existing stopped-lane DB-first
registered-derivative maintenance, with independent checks for
disabled+paused controls, no running leases, private cache ownership,
recorded SHA-256 and canonical file path. The dry-run returns aggregate
counts and a five-minute stale-plan fingerprint; confirmation re-evaluates
the exact plan before PostgreSQL revocation and private unlinking.
Existing originals and unrelated cache files are not selected.

This is **not** a background retention service or hard disk quota.
There is no unattended eviction, no original/OneDrive hydration, no
new transcoding profile or real-media quality acceptance in this PR.
GitHub Windows build CI passed for PR #669;
maintainer-host deletion pilot is deferred to the combined WI-0189
acceptance session.

## WI-0189 persisted Video Settings quota checkpoint 4 (2026-10-30)

PR #668 adds optional **user-configured** registered-byte and per-proxy
publication limits to the Videos Settings UI, saved in the existing
application-local preferences file. Existing two-boolean files remain
compatible; null overrides retain launcher configuration. Invalid or
partial values fail closed; changes while video is active require pausing,
and new limits are read for subsequent video attempts. The controls do not
evict, hydrate, open, or modify originals. No new encoding profiles are
enabled. PR #668 GitHub build CI passed; app-UI smoke remains unverified.

## WI-0189 opt-in publication quota checkpoint 3 (2026-10-30)

Merged PR #667 adds a deliberately configured registered-byte publication
fence to the **existing** shared Archive video lane: per-file cap checked
before publication, aggregate receipt bytes checked under a serialized
PostgreSQL success transaction, path-free non-retryable quota diagnostic
and safe known-uncommitted output cleanup. Existing video preferences,
originals, online-only hydration, poster and photo paths are unchanged.
Without BOTH approved config keys, the original WI-0188 behavior applies.

This **does not automatically evict** files, impose a hard physical
filesystem quota, configure the encode profile from Settings or establish
real MOV/MP4 quality/seek/audio acceptance. PR #667's GitHub build
CI completed successfully; optional maintained-host regression remains. WI-0189 remains in progress
with all acceptance checkboxes open.

## WI-0189 retention checkpoint 2 (2026-10-30)

The maintainer merged PR #665 and locally confirmed all **8/8** focused
retention-policy tests passing on Windows after bypassing a NuGet Webcil
conversion permission error via `WasmEnableWebcil=false`; the maintainer
also reports running documentation validation and regeneration.

Merged PR #666 extends this with a **read-only** PostgreSQL-backed registered
proxy inventory/retention preview, safe private-cache evidence checks,
fail-closed incomplete inventory, and a non-mutating CLI command. The
existing limited operator-only revocation maintenance remains unchanged.
No separate maintainer PostgreSQL result was supplied for PR #666. No automatic eviction, persisted quota/profile Settings or
hard filesystem quota is enabled. See the
[WI-0189 retention runbook](../../operations/video-proxy-retention-wi0189.md).

## WI-0189 retention checkpoint (2026-10-30)

PR #665 begins WI-0189 using the existing locally staged/validated
SDR playable-proxy pipeline from WI-0188. It adds a deterministic
**non-deleting** cache-quota planner and focused policy tests, plus
[retention checkpoint documentation](../../operations/video-proxy-retention-wi0189.md).
No automatic eviction, configurable Settings profile, quota wiring,
real-video quality acceptance or browser playback is delivered in this
checkpoint. WI-0189 remains in progress; its exit criteria are unchecked.
OneDrive-only hydration/release remains WI-0207 and is not enabled.

## WI-0188 final maintained-host acceptance (2026-10-30)

- **PASS:** one live video derivative durably published on attempt 1,
  persisted after restart, with original/source and derivative integrity
  verified by the maintainer.
- **PASS:** photo Archive, People, Visual/Caption/Combined search,
  Smart/Creative Collections and slideshow compatibility, and guarded
  photo-first video queue/Settings controls.
- **PASS:** Advance/Pause and interrupted-job recovery/retry; local-first
  selection, current included folders, exclusions and no unrequested
  OneDrive hydration.
- **PASS:** targeted Release test groups 8 + 2 + 4 + 2 + 1 =
  **17 passed, 0 failed, 0 skipped**, covering unsupported/video
  source-budget rejection, output validation and bounded scratch
  cleanup after tool failure, cancellation and timeout.
- **NOT EXERCISED / deferred to WI-0207:** actually hydrating an
  online-only OneDrive video and safely returning only
  app-initiated downloads to online-only. Every tested source was
  already local.
- Seven historical `attempt-limit` rows were not reset.
  Concurrency benchmarks and mandatory AppContainer proof remain
  explicitly out of scope as approved on 2026-10-22.

## WI-0188 scope adjustment (maintainer, 2026-10-22)

The maintainer explicitly replaced the original concurrent photo/video
performance test and mandatory AppContainer/security feasibility gate.
WI-0188 now targets a **Settings-enabled, default-off, photo-first** video
phase inside the ordinary Archive Advance/Pause workflow, one video at a
time. Online-only downloads are independently opt-in. Only app-initiated
OneDrive hydration may be reverted to online-only; original media are
never deleted. Manual local functional/recovery/photo-regression acceptance
replaces elaborate cross-workload benchmark rigs.

See [revised WI-0188 acceptance criteria](../work-items/WI-0188-video-isolated-processing-lane.md)
and [consolidated local acceptance runbook](../../operations/video-processing-shared-archive-verification.md).
Existing AppContainer experiments remain optional. Azure fallback for a
video the computer cannot process is deferred; user consent and private
data handling require a separate design and authorization.

## Maintained-host WI-0188 checkpoint acceptance (2026-10-21)

The maintainer confirmed **PASS** for fresh authoritative backup,
isolated restore, guarded **v34→v37** schema rehearsal, unchanged existing
catalogue counts, repeatable schema initialization, and the maintained
v37 PostgreSQL application's health. Video job control remained
**disabled, paused and unselected**.

Maintained regression checks also passed: existing photo Archive,
People/face review, Visual/Caption/Combined search, Smart and Creative
Collections, metadata edits, slideshows, and the existing Videos
library/posters, without unintended OneDrive hydration or changes to
original media. Finally, the optional database-free FFmpeg/FFprobe
synthetic pilot passed with independently validated SDR H.264/AAC
landscape and silent portrait outputs.

These checkpoints are **accepted**; they do not prove real-video
one-shot publication, hard disk/IO/network isolation, cancellation
under workload, or photo-only versus concurrent photo/video
performance. WI-0188 remains `in_progress` until that separate
evidence is collected. No mass video processing or general hosted
video worker has been enabled.

The next safe development checkpoint adds a **read-only metadata-only
one-shot candidate shortlist**, permitting operator selection of an
opaque video revision UUID. It does not open or enqueue media; the
existing exact-revision `video one-shot enqueue` dry run must still
prove local availability before further trial decisions.

## Goal and rollout (updated)

Build useful private family video playback and search without changing
originals or the existing photo experience. The basic video catalogue,
SDR still posters, isolated local processing, 720p/540p playable proxy
generation, timestamped frame captions/embeddings and opt-in
Caption/Visual/Combined Video Search are **already delivered**
(WI-0150/0151/0188/0189/0190/0191). WI-0202's historical photo-query
performance regression was also resolved. No additional foundational
processing system is required to restart the video roadmap.

Historical delivery sequence (planned before the November 2026
acceptances; refer to the current handoff above for today's status):

1. **WI-0152 — next / ready:** guarded HTTP range playback of existing
   local proxies on desktop and existing trusted-LAN HTTPS phone path.
2. **WI-0210 — proposed:** inventory real archive compatibility gaps;
   select and implement **one** highest-value bounded file/codec/
   duration/size/colour/rotation expansion; defer any further batches
   to later independently justified workitems.
3. **WI-0207 — proposed:** reuse photo-style app-owned OneDrive managed
   hydration, keep newly fetched videos locally while needed for
   processing, safely release only eligible app-hydrated files afterward.
   One real authorized example, core safety and targeted exception tests.
4. **WI-0153 / WI-0154 — deferred:** basic display metadata (date/time,
   GPS/optional altitude, make/model, duration, resolution/FPS) and
   collections/slideshow integration later. Video-enabled Smart
   Collections are **not in this scope** and must not change saved
   photo-only filters.
5. **WI-0155 — proposed later:** a bounded, possibly isolated prototype
   to decide what face recognition in videos should do. Feature wanted;
   full tracking and canonical integration are **future choices**.
6. **WI-0156 — administratively retired:** Live Photo pairing is not
   needed or implemented; no research gate remains.

## Preserved safeguards and pragmatic acceptance

- Keep immutable originals, current revision/exclusion checks,
  app-owned derivative integrity and photo-only default behavior.
- The browser may serve a current authorized *retained* proxy even
  while its source original is online-only. Do not hydrate originals
  just for browsing/search/playback. Future deliberate preparation
  uses the existing managed processing lane.
- User-pinned and previously local OneDrive originals are never
  automatically released. An app-hydrated original can stay local
  for active and already-planned processing and then be released
  safely by managed-cache policy when no longer needed.
- Use existing FFmpeg resource bounds, local control, status/retry,
  backup and disposable tests. Do not add mandatory AppContainer,
  cloud outage rehearsals or detailed GPU/cost/performance studies
  without a real operational trigger.
- Operator-controlled Azure applications remain permitted for
  explicit, vetted inference, not automatic unknown third-party AI
  services or cloud catalogue access.
- Missing/unsupported media must produce honest states; a catalogued
  video does not automatically promise poster/proxy/search readiness.

## Work items

- [WI-0150](../work-items/WI-0150-video-architecture-activation-gate.md) — completed architecture activation.
- [WI-0151](../work-items/WI-0151-video-ingestion-metadata-posters.md) — completed scoped metadata and SDR posters.
- [WI-0188](../work-items/WI-0188-video-isolated-processing-lane.md) — completed opt-in local-video worker.
- [WI-0189](../work-items/WI-0189-video-local-low-resolution-proxies.md) — completed local playable proxies and controlled retention.
- [WI-0190](../work-items/WI-0190-video-frame-caption-embedding.md) — completed scoped frame descriptions/embeddings.
- [WI-0191](../work-items/WI-0191-video-semantic-caption-search.md) — completed scoped video search.
- [WI-0202](../work-items/WI-0202-photo-catalogue-query-performance-regression.md) — completed independent photo-query regression recovery.
- [WI-0152](../work-items/WI-0152-video-serving-playback.md) — completed and locally accepted desktop/phone playback.
- [WI-0210](../work-items/WI-0210-archive-driven-video-compatibility-expansion.md) — completed one archive-driven input-class expansion.
- [WI-0207](../work-items/WI-0207-opt-in-onedrive-video-hydration-and-release.md) — completed first-time and already-inspected online-only hydration/release acceptance.
- [WI-0211](../work-items/WI-0211-recognizable-video-posters-and-library-browsing.md) — completed recognizable posters and Videos library improvements.
- [WI-0153](../work-items/WI-0153-video-collection-membership.md) — deferred explicit photo/video manual collections only; no dynamic video Smart queries.
- [WI-0154](../work-items/WI-0154-video-slideshow-playback.md) — deferred mixed-media slideshows, depends on WI-0152 and WI-0153.
- [WI-0155](../work-items/WI-0155-video-face-processing-evaluation.md) — in progress on isolated `wi-0155`; bounded feasibility only, with final architecture choice still open.
- [WI-0212](../work-items/WI-0212-video-metadata-place-and-manual-people.md) — in progress: video capture metadata, Place and manual person presence.
- [WI-0213](../work-items/WI-0213-video-level-description-and-search-review.md) — proposed follow-up to evaluate whole-video descriptions and retrieval after WI-0155.
- [WI-0156](../work-items/WI-0156-live-photo-pairing-evaluation.md) — retired without implementation.

## Completed first video slice — WI-0151 local acceptance (2026-10-19)

The maintainer completed all **11/11** consolidated PostgreSQL/schema, opt-in ingest, real-file SDR still-poster, default-off library, photo-isolation and cleanup-preview checks. On the final bounded poster materialization pass, 1,316 candidates were enumerated, one eligible poster was **already present**, zero new posters were needed, seven HDR transfers were deferred and zero changes/failures occurred. The previously generated SDR poster was visually accepted. The backup with isolated restore passed, and no original media or OneDrive placeholder was modified. The severe WI-0202 Archive/Smart Collection regression was independently completed before this final acceptance.

**Scope boundary:** WI-0151 establishes durable, revision-bound, exclusion-aware **Rec.709 SDR still posters and read-only metadata browsing** only. No playable proxy, new video analysis worker, mixed slideshow, manual video annotation, caption/frame embeddings, video search or HDR production posters are approved by this result. The rest of M30's milestone exit criteria remain unchecked until their own work items are implemented and verified.

## Exit criteria — current staged acceptance

Completed foundations:

- [x] Explicit architecture activation, existing video discovery,
  metadata and current Rec.709 SDR poster policy (WI-0150/0151).
- [x] Bounded locally available opt-in processing, immutable originals,
  approved photo safety, app-owned local playable proxies and manual
  retention controls (WI-0188/0189).
- [x] Timestamped frame captions/embeddings and useful opt-in Video
  Search with unchanged photo defaults, under the maintainer's
  documented small-SDR-cohort scoped acceptance (WI-0190/0191).
- [x] The historical photo Archive/Smart Collection performance
  regression is resolved under WI-0202's accepted scope.

Remaining initial M30 user-value outcomes:

- [x] **WI-0152:** locally retained proxies play and seek on desktop
  and phone, accepted after PR #720 without recalling the original.
- [x] **WI-0210:** one archive-evidence-driven Rec.709 full-range
  input-class expansion was locally verified after PR #734;
  universal media-format support was not claimed.
- [x] **WI-0207:** verified on the maintained Windows PC after
  PR #761, including an already-inspected SDR original and one
  previously uninspected online-only source in the newly approved
  input-format class. Both were safely processed and returned
  online-only, with successful in-app playback; CI and source
  ownership/release safety tests passed.
- [ ] **WI-0155 (in progress):** review person-level detection and
  read-only known-person suggestions across approved local videos;
  record a bounded next-production-slice and architecture decision.
  Full tracking or production recognition is not a hidden gate.

Additional independently planned M30 work (not retroactive gates on
the accepted initial video foundation):

- [ ] **WI-0212 (in progress, near-term):** display/explain video capture
  metadata, GPS-to-Place, catalogue corrections and video-level
  manual person presence, separate from collections and face evidence.
- [ ] **WI-0153 / WI-0154 (deferred):** explicit manual mixed-media
  collection membership, then optional mixed-media slideshows.
- [ ] **WI-0213 (deferred):** revisit useful whole-video descriptions
  and search after WI-0155 evidence; initial scoped frame captions
  and Video Search remain completed.

Explicitly **not required for the above initial outcomes**:
WI-0153 video collection integration, WI-0154 video slideshow
integration, and WI-0213's future video-level description evaluation
are not prerequisites for the accepted *initial* foundation.
Video-enabled Smart Collection filters, Live Photo
pairing (WI-0156 retired), universal HDR/codec support, exhaustive
scene benchmarking, automatic cloud fallback or new security
infrastructure.

**Registry caveat:** WI-0153, WI-0154 and WI-0213 remain
proposed M30 members; WI-0212 is in progress. The milestone status
generator requires all M30 work items to be terminal before marking
M30 completed. At closeout, deliberately implement the deferred
scope or move it to a later milestone through a reviewed decision.
Do not call these items completed simply to satisfy milestone arithmetic.
