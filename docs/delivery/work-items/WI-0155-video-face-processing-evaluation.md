---
id: WI-0155
title: Evaluate face discovery and identity processing inside video
milestone: M30
status_source: ../status/work-items.yaml
depends_on: [WI-0151, WI-0188]
related_adrs: [ADR-0006]
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Recognition.Onnx, PhotoIdentity.Api, PhotoIdentity.Persistence.Postgres, docs]
---

# WI-0155: Evaluate face discovery and identity processing inside video

**Current direction (maintainer, 2026-11-07):** Judge the experiment by whether a
video yields **at least one useful, human-reviewable face observation per
visible person** and, when an eligible known-person reference exists, a
credible ranked match to the **existing Photo Identity catalogue**. Correct
tracking or consolidation of every frame is not a prerequisite. Evaluate a
small, diverse set of **additional expressly approved local videos** before
choosing a production slice. All WI-0155 development, evaluation and
documentation changes stay isolated from `main` on the long-lived
`wi-0155` integration branch (or an independently isolated prototype)
**until near work-item completion**, when the maintainer explicitly decides
whether to integrate into Photo Identity or retain video recognition as a
separate component. Neither integration nor independence has been selected.
The historical checkpoints below remain as evidence, not current priorities.

## Product direction (maintainer 2026-11-02)

**Face recognition in videos is wanted**, but the architecture,
review UX and accuracy requirements are not fixed. This item should
begin as a **bounded feasibility prototype and requirements discovery**,
not automatically implement person tracks, a full review workflow,
large retention schemes or a video-wide identity engine. It was initially
proposed behind playback/compatibility priority; see the new 2026-11-05
experimental-branch scheduling decision below.

A prototype **may live in a separate experimental repository** if
that best isolates dependencies and development churn. Such a
prototype must use approved private local fixtures and must not
write to the maintained Photo Identity catalogue, source originals
or canonical identities. A separate repository is an option, not
a mandatory gate or excuse to reimplement the production data model.

## First stage — limited prototype / discovery

- Reuse existing photo face-detection/embedding models on **a few
  operator-selected already-local video frames** with timestamps.
- Establish which actual user tasks matter: identifying a known
  person in a clip, locating their appearances and choosing a
  representative still. Unknown person outcomes are legitimate.
- Observe rough quality, duplicate detections and per-clip run time,
  without requiring statistically significant population recall,
  GPU accounting or full tracking accuracy as a first milestone.
- Try basic near-adjacent-frame deduplication only if duplication
  materially affects the output. Temporal multi-object tracking
  is an **optional subsequent experiment**, not a prerequisite.
- Document a human-reviewed decision about the **smallest useful
  next production slice** and required safeguards. A no-go may
  reject a particular implementation approach, not the entire
  long-term product goal.

## Out of scope for this first stage

- Automatic canonical identity assignment, photo review-queue
  flooding, mandatory track start/end models, bulk archive scanning,
  a new recognition service and large-scale precision/recall
  evaluation before a promising prototype exists.
- Face processing as a prerequisite for ordinary video playback,
  captions, collection browsing or online-only hydration.

## Acceptance criteria — revised, feasibility only (2026-11-07)

These are **requirements for evidence and a decision**, not acceptance of
production recognition, a numerical accuracy guarantee, or authorization to
modify the maintained catalogue. Existing exploratory evidence is valuable,
but the following criteria remain open pending a human-reviewed multi-video
evaluation and the final architecture decision.

- [ ] **Person-level usefulness across more than one video:** the existing
  pilot plus at least one additional *expressly approved, already-local*
  video (target two or three varied new clips if practical) are reviewed
  privately. Record the manually observed people, how many yielded **at
  least one usable detected face**, and which appearances remained
  unknown, missed or unreviewable. No compulsory per-frame recall target.
- [ ] **Existing-catalogue known-person matching evaluated:** deliberately
  opt in to a bounded **read-only** eligible-exemplar view of the actual
  PostgreSQL Photo Identity catalogue, or an independently controlled
  equivalent read-only snapshot. Verify exact SFace model ID/hash and
  compatible alignment before scoring; use only current eligible
  assignments, not Unknown/Rejected/superseded evidence. Compare video
  findings to human-known positive and negative cases; report correct
  rank-1, weak/ambiguous suggestions and wrong known-person suggestions.
  A database integration is **not yet implemented** by the existing CLI.
- [ ] **One useful observation can suffice:** for a known person, one
  sufficiently informative, reviewable video frame may support a
  *suggestion for human review*; additional frames count as useful only
  if they provide stronger and reliable ranking evidence or a better
  manually reviewable view. Do not treat maximum raw similarity over
  many frames as confirmed identity; monitor false suggestions and
  uncertainty. Continue examining a video for **other** people after one
  known-person candidate is found.
- [ ] **Unknown and multiple-person cases:** include at least one
  intentionally unknown-to-the-gallery person (or equivalent negative
  case) and a case with people at different times or simultaneously.
  Unknown faces may remain without candidate labels; never force them to
  an existing person merely because it is the best score.
- [ ] **Bounded runtime and review burden:** record frames examined,
  approximate processing time and human assessment of usability
  (e.g., usable/borderline/unusable), redundant suggestions and
  meaningful candidate improvements. Human ease of identification is
  qualitative and is **not claimed as a precise model metric**.
- [ ] **Privacy, provenance and read-only safety:** prove the explicit
  experiment cannot change source photos/videos, production canonical
  people, review history, embeddings, suggestions or matching policy;
  no background ingestion, cloud hydration, automatic assignments,
  implicit worker jobs or third-party black-box processing. Biometric
  frames, embeddings, names, IDs, paths and detailed reports stay
  private, outside Git/CI/issues/chat.
- [ ] **Architecture decision and scoped follow-up:** near completion,
  the maintainer evaluates evidence and explicitly chooses either a
  carefully bounded integration into the main application **or** a
  separately maintained video-recognition component with an appropriate
  identity-data boundary. Document interface, operational ownership,
  security, retention, test plan and rollout/rollback (or no-go) before
  any PR targets `main`.
- [ ] **No premature tracking or persistence:** deduplication,
  temporal tracking, cross-video association, UI review workflow,
  catalogue writeback, large-scale calibration and full benchmarking
  remain separate work unless findings demonstrate a concrete need.
  Extra group-count improvements are not themselves acceptance
  criteria.

## Verification requirements

Bounded, explicit local prototype plus maintainer review; private
multi-video, per-person and negative-case observations. For any
catalogue-backed experiment, demonstrate exact-model compatibility,
eligible-exemplar filtering, read-only access, stable/provenanced
snapshot use and no persisted or canonical changes. Do not silently
apply the photo matcher’s score thresholds to video or confuse
YuNet pilot detections with the permanent CenterFace archive profile.
No change to production biometric governance during exploration.

## Completion notes

- Status: in_progress on the isolated `wi-0155` integration branch.
  Video recognition remains a desired feature, but this work item
  stays a bounded feasibility/prototype decision.

## Experimental development checkpoint — 2026-11-05

The maintainer selected WI-0155 for parallel work and explicitly chose a
**long-lived `wi-0155` integration branch**. Feature changes are reviewed
as pull requests targeting `wi-0155`; merging them into production
`main` is deferred until a separate human decision. This scheduling
decision supersedes the 2026-11-02 instruction not to start yet.

The first feature branch `wi-0155-offline-frame-feasibility` introduces
an isolated, opt-in, pre-extracted-local-frame CLI with the **existing
verified YuNet/SFace models and OpenCV face-alignment contracts**. It
reports aggregate detection/embedding counts, elapsed time and a
tentative adjacent-frame near-repeat count. Input is capped at
12 ordinary JPEG/PNG stills / ten minutes, from one explicitly selected
clip manifest, with no original video access, OneDrive, PostgreSQL,
canonical identity writes, persistent embeddings or API/UI changes.
Source-free fake-model tests exercise input limits, privacy-safe
output and one-to-one repeat-count behavior.

This is development infrastructure for an **evaluation**, not a
claim that the models correctly identify familiar people in videos.
A human-approved private pilot, known-person comparison design,
miss/duplicate review and a smallest-production-slice decision
remain pending. None of the work-item acceptance checkboxes is
fulfilled by implementation alone. See
[the experimental Windows procedure](../../operations/video-face-feasibility-wi0155.md).

## Experimental development checkpoint 2 — private reference review

A second standalone evaluation stage adds `--references` and
`--private-report` to the **opt-in local-only** feasibility CLI.
Up to six independently approved single-face stills carry numeric
pseudonyms `R01`–`R99`. For the selected video frames, the tool can now
compare existing SFace embeddings against those references and record
timestamped, face-ordinal **tentative suggestions**, a runner-up
similarity, score gap and explicit uncertainty status. Only a new
private, non-repository report receives those details; stdout remains
aggregate-only. Input stills, catalogue, canonical identities and model
artifacts remain unchanged.

The similarity and gap thresholds are exploratory—not calibrated
person recognition or authorization to assign identity. A
byte-identical video frame cannot double as a reference.
The new tests exercise pseudonym validation, relative-path controls,
low scores, ambiguity, single-reference uncertainty, copied-frame
rejection, private report restrictions and unchanged input hashes.
See the [checkpoint 2 runbook](../../operations/video-face-feasibility-wi0155.md).

**Status:** checkpoint 2 merged as PR #741 into `wi-0155`; local synthetic
tests and first approved private real-media pilot completed. The pilot
revealed both a missed detection and a suppressed known-person
suggestion. Further comparison with known-positive and known-negative
cases, and a maintainer product decision, remain required.

## Experimental development checkpoint 3 — first pilot findings and diagnosis (2026-11-05)

The maintainer verified the merged PRs #740 and #741 in the separate
WI-0155 Windows worktree. Local build/tests, CLI help, docs validation and
generation, governed YuNet/SFace model verification, private report writing,
and source-image SHA-256 invariance passed.

**Pilot observations (operator-supplied, one approved clip):**
two selected still frames, the same person visible in both and present
in one independently selected reference photo; YuNet detected a face
in **one of the two frames**, produced one embedding, and the
experimental matcher made **no known-person suggestion**. Total elapsed
processing was about **1.35 seconds**, with human review taking a few
seconds. This is one small pilot, **not a population detection or
recognition-rate estimate**. The withheld suggestion is a known-positive
miss; the private ranking of the correct reference is not yet established.
Neither input images nor catalogue data were modified.

This creates distinct questions that require separate evidence:

- **Detection:** Why did one of the two selected frames produce no
  detection? The face may have been small, angled, blurred, poorly lit or
  below YuNet's current confidence setting. A visible person is not
  necessarily a detector-visible face; human inspection must confirm.
- **Matching:** For the frame where a face was detected, did the true
  reference rank first but fall below the exploratory cosine cutoff,
  or was the highest-ranking reference actually the wrong person?
  Lowering a threshold without finding out could introduce false positives.
- **Reviewability:** Withhold automatic labels; compare a handful of
  deliberately chosen known-positive, unknown-person and difficult
  frames, recording human burden and clearly incorrect suggestions.

The third experimental PR adds **explicit private diagnostic mode**
to the local CLI. It records per-selected-frame detector counts and
confidence ranges, plus the highest-ranked reference *even when
the default threshold suppresses it* and a read-only sensitivity
comparison at cosine cutoffs `0.363`, `0.45`, `0.60`, `0.80`.
Optional YuNet confidence settings `0.5`, `0.7`, `0.9` allow
separate local runs to compare a missed frame. None of these cutoffs
is calibrated or promoted to a production setting. Diagnostics are
strictly opt-in, written to an explicitly selected new **private**
report outside Git, and not printed to stdout.

**Work-item state:** in progress, not complete. Initial functionality
and a single real pilot are available; quantitative accuracy claims,
production adoption, video frame sampling, durable identities and
automatic catalogue writes remain out of scope. Do not commit any
real identities, reference mappings, face coordinates, frame-level
biometric scores or private reports. See the
[local sensitivity runbook](../../operations/video-face-feasibility-wi0155.md).

## Experimental development checkpoint 4 — search for people across the clip (2026-11-05)

The maintainer's repeat pilot provided decisive additional local evidence:

- With YuNet detector confidence **0.9**, only the first of two sampled
  frames yielded a detection (detector confidence **0.9082**).
- At detector confidence **0.7**, **both** frames yielded a detection:
  frame 2 scored **0.8962**, just below the original 0.9 cutoff.
- Both face observations had the **correct reference ranked first**,
  using a private pseudonym. Cosine scores were **0.5402** and **0.6571**.
  Neither passed the existing, deliberately conservative 0.80 review cutoff.
- The operator also confirmed **another different person appears elsewhere
  in the same video but is absent from both selected frames**. The prior
  pilot therefore says nothing about whole-video person coverage; it
  evaluated two observations of one person, not every person in the clip.

**Decision:** Before tuning SFace cutoffs or recommending production person
discovery, the evaluation must **sample the video timeline**, count detected
faces including any without reference matches, and check what distinct
appearances are missed. Two early manually selected frames are not an
adequate coverage evaluation, however well they match a known person.

The next experimental checkpoint adds an **operator-invoked local
PowerShell timeline sampler** using the maintainer's approved FFmpeg and
FFprobe executables. It processes exactly **one expressly approved,
already-local** video, within an expressly selected **<=10 minute window**,
and writes **12, 24 or 36** evenly spread JPEG samples plus their
relative timestamp manifest to a new private non-repository directory.
The sampler refuses linked, offline and cloud-recall inputs; it does not
scan archives, hydrate OneDrive, persist identities, or modify the original.
It is not invoked by Photo Identity, startup, browsing or any worker.

An explicit `--coverage --diagnostics` mode raises the feasibility CLI's
manifest limit from **12 to 36** selected frames, requires the existing
approved private reference manifest and a new private report, and adds
cross-frame **heuristic face-appearance groups**. Each newly encountered
appearance receives a private group alias, first-seen sample/time and
observation count. Grouping uses an **uncalibrated 0.80 SFace repeat
cutoff** and prevents two faces in one frame from being assigned to the
same group. Groups are **not unique people**, person identities, tracks or
reliable counts of individuals: one person may split into multiple groups
and unrelated people can accidentally merge. Their purpose is to
prioritize **human inspection of potentially different visible faces**,
including people absent from the reference set. Raw embedding vectors
and face crops are never written.

**Acceptance still open.** A new private local video pilot must check
whether the *second* known-to-operator person is actually sampled and
detected, inspect false splits/merges, note short appearances that were
missed, and then decide whether targeted additional frames or a
separately scoped tracking/sampling approach is needed. Even 36 samples
cannot guarantee every person is seen. Do not change production
confidence defaults or create canonical identity assignments from
this experiment. See the [coverage runbook](../../operations/video-face-feasibility-wi0155.md).

## Experimental checkpoint 5 — evidence-led grouping sensitivity (2026-11-05)

The maintainer completed the **36-frame local coverage pilot** from merged PR #748.
This pilot found **19 frames with faces**, **21 detected face observations**,
and produced **14 tentative groups** in roughly **4.5 seconds**. The second
known-to-operator person was visible in samples 32–36 and was detected, so
the original two-frame coverage omission was addressed.

Private manual viewing indicates that the first person appears through
much of the earlier part of the clip and the second appears towards the
end; they are not simultaneously present. The known zero-detection
samples included poor-quality views (blur, partial face, odd camera angle),
which may not be useful for recognition even if found. **Further lowering
YuNet confidence is not a priority from this evidence.** Keep the
diagnostic 0.7 selection for repeatability; it is not a production default.

**The grouping algorithm, not person discovery, is the current gap.**
A first-observation exemplar and conservative repeat cutoff 0.80
produced 14 provisional groups, many with only one observation.
For the last sequence, frame 32 started a group and frame 33 started
a second despite the operator observing the same person in both;
the closest similarity was about 0.62. Frames 34–36 then repeated
the latter group's observation with scores near 0.87–0.90.
Frames 14 and 17 generated two detections each, although the
operator reports no simultaneous appearance of the two known people.
Without reviewing boxes and face pairs, those must **not** be
treated as proof of two different people.

The next isolated checkpoint adds a **read-only comparison inside the
same opt-in private `--coverage` JSON report**. It retains the
unchanged baseline first-exemplar/0.80 grouping, and separately
replays the **same in-memory observed embeddings** using a
bounded multi-observation group bank at comparison cutoffs
**0.60, 0.70 and 0.80**. It also records private same-frame face-pair
bounding-box overlap and embedding cosine as **potential duplicate
review hints**. Neither low-cutoff merges nor overlapping boxes are
automated identity decisions. The new comparison is for detecting
false splits **and false merges**, not selecting a universal threshold.

The operator will rerun the already extracted 36 JPEG stills and
reference manifest without rereading the original video. Human review
should check whether sample 32 can be grouped with 33–36,
whether earlier appearances reduce excessive fragmentation,
and whether samples 14/17 were duplicate detections or genuinely
distinct image content. All private face-level evidence stays local;
only non-identifying aggregate outcomes should be shared.

**Open:** Select a robust grouping method and obtain contrasting
human-labelled examples before declaring a smallest production slice.
Do not persist canonical person identities, clips' face vectors or
unreviewed group assignments.

## Requirements decision record — 2026-11-07

### Confirmed user requirements

1. **Purpose is identifying people, not producing perfect face tracks.**
   A single usable face sample can be enough to propose an identification
   for review. Additional observations of an already proposed person
   matter only if they materially improve supported similarity/ranking
   evidence or present a clearer image to a human.
2. **Reuse the existing identified-person catalogue.** The maintained
   PostgreSQL database already has canonical people, reviewed face-person
   assignments and eligible SFace exemplars. Separate operator-created
   reference photos were a safety measure for checkpoints 1–5, **not
   the desired permanent identity source**.
3. **Evaluate on more videos before choosing architecture.** Expand from
   the existing two-person pilot to a few diverse private videos,
   covering known and unknown cases and ideally different appearances
   within a scene. Do not equate group count with identified-person
   coverage.
4. **Keep work isolated until near completion.** Feature branches and
   experiment documentation PRs must target `wi-0155`, not `main`.
   The experimental CLI, prototype database adapter and any matching
   logic must remain out of normal Photo Identity startup, worker
   orchestration, scheduled jobs, video processing and UI paths. A
   stand-alone separate repository/component remains a valid final
   outcome. Only a late, explicit maintainer architecture decision can
   authorize a main-branch integration proposal.
5. **Local/private, explicitly approved processing.** Inputs are
   approved local clips; no OneDrive on-demand hydration, automatic
   catalogue writes or unvetted external inference. Neither source
   media nor canonical identity state may be modified.

### Newly clarified acceptance measures

- **Person discovery yield**, reviewed per video: number of manually
  observed people versus number with at least one useful detected face,
  not percentage of individual frames that contain a detection.
- **Known-person candidate usefulness**: a reviewer can find the
  right person among explicitly ranked, exact-model suggestions
  when eligible catalogue evidence exists; wrong and ambiguous
  suggestions are counted too. Distinguish detection, ranking,
  threshold suppression and actual identity confirmation.
- **Unknown-person rejection**: a face absent from the catalogue
  must be allowed to remain unknown instead of receiving an
  unsupported identification.
- **Review effort and redundancy**: how many observations a reviewer
  must examine to get useful evidence per person, with optional
  best-view selection. More sampled frames have value only when they
  locate another person or improve identification/review.
- **Provenance/isolation gate**: catalogue-backed matching may **read**
  a bounded current, eligible, same-model exemplar snapshot, but
  cannot invoke production regeneration, auto-assignment, or writes.
  Changed identity evidence during evaluation must be detected or
  separately re-snapshotted rather than silently mixed.
- **Late integration decision gate**: before any merge to main,
  choose in-app integration vs a separate service/CLI/component,
  document costs/ownership/private-data transport (if separate),
  user review/acceptance and rollback. It is permissible to finish
  feasibility with a decision and subsequent separately scoped
  production work instead of immediate app integration.

### Open design questions — not committed requirements

- How to access PostgreSQL exemplars safely: in-process read-only
  repository adapter versus an operator-approved, locally confined
  read-only snapshot or narrowly scoped interface; how to enforce
  least privilege and catalogue-evidence freshness.
- Which exemplars to select or cap per person, especially when
  automatic photo assignments are eligible, while preserving fair
  person-level ranking and avoiding dominance by people with many
  photos.
- How to select one/few representative video frames without
  hiding a different person or over-optimizing highest cosine;
  whether any deduplication is necessary at all.
- How to handle unknown-person review, uncertain candidates,
  human-selected representative frames, and future persistence.
- Exact production deployment boundary: Photo Identity integrated
  module versus stand-alone video recognition with a controlled
  read-only identity interface. **Do not settle this during
  early experiments.**

### Evidence so far and next experiment

The accepted single-video, 36-frame pilot produced 21 face
observations across 19 frames and detected the second person
missing from the original two-frame sample. Private grouping
sensitivity yielded 14 baseline groups, versus 12/11/9 tentative
groups using multi-observation cosine cutoffs 0.80/0.70/0.60.
At 0.60, the five late appearances of the second person were
in one tentative group, and no group overlapped the early and
late observation windows; **this is not a calibrated identity
threshold**. Frames 14 and 17 contained two non-overlapping,
low-similarity detections requiring visual interpretation.

These historical grouping results are now **diagnostics only**:
fragmentation is not the primary success measure. The next
substantial experiment should first establish a bounded
catalogue-backed read-only identity gallery contract, then run
the current clip and additional approved videos against known
and deliberately unknown people. No main-branch work or
production automatic-assignment behavior is authorized.

See the [experimental operator runbook](../../operations/video-face-feasibility-wi0155.md)
and [recognition/identity matching governance](../../architecture/identity-matching.md).


## Experimental checkpoint 6 — catalogue snapshot and multi-video ranking (2026-11-07, proposed)

The isolated WI-0155 feature branch adds an **opt-in experimental** path in
the existing standalone video-face CLI. A private cohort manifest explicitly
selects **1–32 canonical people** and **2–4 separate already-local videos**.
The operator supplies each video's existing bounded timestamped frame manifest;
no automatic archive scan or video hydration occurs.

A dedicated local PostgreSQL **SELECT-only** login (not the application's
writable connection) is mandatory. A guarded REPEATABLE READ, READ ONLY
transaction selects at most three distinct-current-photo SFace exemplars
per explicitly selected eligible person. The reader requires the exact
approved SFace FP32 ID/hash, 128-dimensional vector format, and
sface-five-point-v1 alignment, and excludes merged identities, obsolete
revisions, reversed/Unknown/Rejected latest review decisions and
incompatible data. It records a private snapshot token plus review and
embedding high-watermarks. The transaction closes before decoding video
frames. All clips are compared with the **same in-memory snapshot**, so
writes or new reviews during a video run cannot silently change the gallery.

The new ranker competes **between people**, never between multiple photos
of the same person for the top-two places. The report preserves candidate
suppression at experimental cutoffs, unsuppressed private diagnostics,
per-person top-rank observations and no-candidate counts for human review.
It makes **no** canonical assignments or claims about calibrated
identification, usable-frame quality, distinct detected people, or unknown
precision. Full temporal tracking, production matcher regeneration and UI
integration remain out of scope. The original private-reference CLI is
untouched in normal use.

**Acceptance is not complete:** code and synthetic tests alone do not prove
the role ACL or data-shape compatibility with the maintainer's live
PostgreSQL database, or that correct people are ranked against the actual
gallery. Local verification of the safe SQL path, model integrity and
input/output invariance, followed by the approved multiple-video
positive/negative human review, remains required. Record false suggestions
and any missing eligible exemplar before deciding on the minimum production
slice. Keep all results private. No branch promotion to main is authorized.

The [checkpoint 6 operator procedure](../../operations/video-face-feasibility-wi0155.md)
defines read-only role setup, bounded case files and PowerShell 7.6 trial
commands. Both integrate-into-app and remain-separate outcomes are still open.

## Experimental checkpoint 11 — effective detector input and tiled comparison (2026-11-08)

**Code proposed on isolated WI-0155 child branch; full local acceptance
deferred.** Increasing coverage to 180 frames and preserving native
decoded V03 resolution did not resolve distant-face visual quality.
The original V03 is HEVC 1920 × 1080, yuv420p10le, -90° rotation.
A second bottleneck was identified in the existing governed face
detector: the default single-pass YuNet preprocessor maps the entire
portrait image to the fixed 640 × 640 model input, irrespective of
lossless source-frame extraction.

**Requirements added/clarified for FID-03 and SAMPLE-01:**

- Verify each native-lossless decoded frame still has the source's
  expected display dimensions before it reaches the detector. Reject
  unintended rescaling or rotation against an existing, private
  extraction-metadata sidecar; remain compatible with older
  manually prepared frame manifests without that sidecar.
- Keep single-pass YuNet as the unchanged baseline. Compare the same
  native PNG frame set and selected catalogue identities with an
  **explicitly enabled**, existing governed YuNet multiscale/tiled
  pipeline (initially 1024-pixel tiles, optionally 640), with
  bounded operator controls; never silently change production.
- Record decoded-frame resolution separately from the detector's
  model tensor dimensions and tiling size in the **private** diagnostic
  report. Also record a minimal per-frame proxy for face pixel
  dimensions without exposing coordinates or identity data on stdout.
- Use controlled same-timestamp comparisons. Measure face discovery
  yield, human-confirmed right/incorrect/unknown suggestions and
  runtime/storage cost, not face count alone. Multiscale inference
  may improve small-face recall but cannot restore missing source
  pixels and is not expected to fix image sharpness itself.

The implementation remains a standalone opt-in video-face experiment
targeting `wi-0155`, not `main`. Catalogue matching continues
against the existing read-only snapshot, with no writes or automatic
identity assignment. Physical V03 quality, exact effective face
visibility, false detections and processing cost remain **unverified**
until the combined Windows operator trial described in the
[WI-0155 runbook](../../operations/video-face-feasibility-wi0155.md).
The already-merged temporary frame lifecycle should be tested in
that same acceptance session, after preservation of the original
and confirmed report persistence.

## Experimental checkpoint 12 — initial Windows results and diagnostic follow-up (2026-11-09)

The maintainer's first combined Windows catalogue trial completed
**single-pass** inference for 3 videos and 252 sampled frames:
74 frames contained detections, 101 total faces/embeddings, and
46.223 seconds aggregate processing time. The private output
was persisted. **This verifies processing, not correct identity
assignments or additional unique-person discovery.**

The same-cases **multi-scale 1024** run failed with the original
all-purpose, privacy-preserving error. The exact stage and reason
cannot be inferred from that error alone; it is **not** legitimate
to claim model/tiling failure or exceedance of the face-count gate.
The isolated diagnostic follow-up adds stable, non-identifying
failure stage/category reporting, with synthetic regression tests
for detector exceptions and the bounded >32-face condition. It
does not weaken safety caps or change detection thresholds.

**Acceptance plan:** keep the existing private 180-frame PNG
extraction, V01/V02 inputs, catalogue case snapshot selection, and
successful single-pass report. After merging the diagnostic fix,
rerun *only* the tiled configuration with a fresh private report
destination and the existing dedicated read-only PostgreSQL role.
A safe error category, if still failing, determines the next
targeted correction; successful detection must still undergo
private human review of true known/unknown matches. Delay
automatic temporary-frame cleanup until the full evaluation and
selected retention have been verified. Integration versus
separation remains undecided; no production writes or app
registration are authorized.

## Experimental checkpoint 13 — bounded face candidates for tiled evaluation (2026-11-09)

The subsequent Windows tiled-1024 retry returned a **confirmed
`face-count-limit` / `frame-face-limit`** after 35.6 seconds.
At least one frame exceeded the **32 candidate faces per frame**
post-merger safety limit. The actual number of valid people among
those candidates is unknown, and this failure does not constitute
an identification-quality assessment.

To enable bounded, **review-only** experimentation without
simply raising the limit, the isolated catalogue CLI adds a
multiscale-only, explicit `--face-overflow-policy retain-top-32`.
The original default is `fail` and the existing limit of
32 aligned/embedded/ranked candidates per frame stays in force.
This experimental policy deterministically ranks post-NMS
candidates by detector confidence, then keeps at most 32.
It privately records raw candidate counts and discarded
candidate counts; stdout contains only aggregate suppression
totals. It does not claim discarded candidates are false
detections or that confidence ranking preserves every useful
person. Local human review must compare person-discovery yield,
false detections, right/unknown suggestions and omitted candidates.

**Acceptance remains outstanding.** Preserve the 180 PNG V03
extraction, already successful single-pass report and the
read-only database cohort. After this child-branch PR merges
into `wi-0155`, rerun only the tiled-1024 experiment using
a new private report and opt-in policy. CI synthetic tests
check strict default, bounded retention, deterministic
selection and diagnostic accounting; they cannot establish
real image accuracy. No main-branch production integration,
database mutation or original-file change is authorized.
