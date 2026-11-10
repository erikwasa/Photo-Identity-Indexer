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

**Current identification workflow clarification (maintainer, 2026-11-10):**
Video face identification must follow the **same three-tier human workflow
as still-image identification**: (1) auto-assign only when the existing
configured score cutoff **and** sufficiently large top-vs-runner-up margin
both pass; (2) otherwise provide **two or three ranked existing-person
suggestions** for user confirmation/correction; (3) for genuinely
unsuggested or unknown faces, allow manual identification, including
creating people and enriching future catalogue evidence through the
normal reviewed workflow. The maintainer informally expects **under
25% auto-assignment, roughly 50–60% suggestion-assisted review, and
roughly 20% fully manual**. These are illustrative expectations, not
measured rates or strict acceptance quotas. In difficult oblique or
distant video (especially V03), **a correct person anywhere among
2–3 human-review suggestions is a useful success**; a high absolute
score and large margin are not necessary for that outcome. Do not
spend disproportionate time optimizing V03 for auto-assignment.

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

## Experimental checkpoint 14 — human review of a large private result (2026-11-09)

The maintainer reports **PASS** for focused tests and the bounded
tiled-1024 trial: **4,043 raw candidates; 3,986 retained;
57 discarded in 17 overflowing frames; 207 seconds**.
Technical bounded processing is now locally verified; none of
these counts establish how many distinct people were detected or
whether known/unknown suggestions were correct. The private
evaluation report is about **150,000 lines**, which is not an
acceptable human-review interface.

**New requirement WI0155-REVIEW-01:** Review should focus on one/few
useful face observations per actually visible person, not every
frame-level candidate. Supply a small, offline, local, private
review shortlist with:

- per-video candidate, overflow and ranking-coverage summaries,
  explicitly showing catalogue people with **zero** top-ranked
  observations;
- a bounded number of highest-ranked frame observations per person,
  plus uncertain outcomes and overflow risk, with full-frame
  visual access and a small CSV worksheet for human decisions;
- no private evidence uploaded, no production app or database writes,
  no automatic identification and no silent assertion that
  higher cosine scores imply truth;
- no modifications to original videos or intermediate decoded frames,
  no additional inference or unnecessary OneDrive hydration;
- clear limitations until face bounding boxes/crops and stable
  review decisions can be captured by a separately approved design.

A standalone PowerShell 7.6 review-gallery generator and synthetic
smoke test are proposed on an isolated WI-0155 feature branch.
The HTML/CSV and thumbnail files remain entirely in
`%LOCALAPPDATA%\PhotoIdentity\WI0155\review-galleries`.
**Local operator review is still pending**; use the already
successful private report and approved case manifests, not another
252-frame inference run. Frame thumbnails are for review only
and are not evidence of image-model fidelity. The absence of
face-specific bounding boxes prevents direct visual localization
of individual candidate ordinals; consider this a next design
requirement after first human-review feedback.

## Experimental checkpoint 15 — editable private review with face-specific geometry (2026-11-09)

The maintainer verified that the offline PR #778 gallery, thumbnail
links and worksheet all work, but human-review usefulness is
**not yet assessed**. Reviewing uncertain face ordinals in
unmarked full frames is impractical.

**Requirement WI0155-REVIEW-02:** Provide face-specific boxes/crops
without changing production face detection or repeating the
full 252-frame catalogue matching run. Reconstruct shortlisted
face ordinals from existing approved local frames using the
same governed YuNet model, multiscale tile size and confidence.
Validate raw/retained/discarded counts and confidence extrema
against the existing private diagnostic report, fail closed
if they disagree, and persist normalized candidate geometry
only in a new private local file. Apply the same maximum 32
retained faces per frame; do not silently recover discarded
candidates or claim these to be false positives. This targeted
local re-detection is model inference but involves no embeddings,
DB reads/writes, originals, video decoding, OneDrive hydration
or production UI changes.

**Requirement WI0155-REVIEW-03:** Make
`HumanDecision`/`ReviewerNotes` directly editable in the
local HTML gallery with explicit CSV save and import. The
reviewer decision values must distinguish correct suggested
person, wrong suggested person, visible unknown person,
non-face detection, insufficient evidence and frame-level
not applicable. A browser must not silently persist names
or identity labels to an account, remote service or database.
Saving is explicitly user-initiated to a private local file,
with a fallback for browsers lacking direct save support.
Box previews and cropped images are review-only and must
not alter the original frame or detector input.

**Limitations/acceptance:** Old report data lacks individual
coordinates; reconstructed boxes are best-effort tied to
consistent re-detection, not a cryptographic match of all
candidate coordinates. Human usefulness/identity accuracy,
multi-person ambiguity and overflow omission remain
unverified until the maintainer reviews candidate crops.
The isolated WI-0155 branch remains separate from main,
and no write to the canonical identity catalogue is
authorized.

## Experimental checkpoint 16 — human review workflow clarity (2026-11-09)

PR #780 passed local operator verification: synthetic tests,
selected-frame re-detection, visible bounding boxes, crop
generation, in-HTML review editing and CSV save/import. The
maintainer reported **partially useful** and completed all
top-ranked-person reviews, leaving uncertain and overflow sections
pending. This is human-interface acceptance, **not verified
identification correctness**.

**Defect WI0155-REVIEW-04:** The same retained candidate
(`clip/frame/face ordinal`) was shown in both the top-ranked
person section and uncertain suggestions, because those samples
were selected independently. Avoid asking for two independent
human decisions for the same exact face. A shared image frame
may still show different face ordinals and must allow separate
face decisions. Preserve existing reviewed CSV imports and
detector geometries where applicable; no modification to source
media, index state, face models or ranked report.

**Clarified requirement WI0155-REVIEW-05:** Distinguish
`Known person - no reliable match` (selected known person
visually recognized even though catalogue ranking is below
threshold/ambiguous) from `Unknown person` (real face outside
the selected known identities) and `Cannot tell` (poor
evidence). Treat `Overflow / omissions` as an **optional
frame-level diagnostic**, not an individual face identity
judgment. Previous reports expose no discarded boxes; an
unboxed face observed by the operator does not prove that it
was one of the discarded detections. Track face-specific
review progress separately from frame-wide overflow warnings.

**Acceptance status:** Gallery improvements proposed on an
isolated WI-0155 child branch; Windows local test and
re-import of previously completed reviews remain pending.
Do not delete the existing CSV, gallery, cases, original
bounded JSON or extracted PNGs. No production integration
or catalogue mutations authorized.

## Experimental checkpoint 17 — actual human review and coverage gap (2026-11-10)

**PR #784 local acceptance: PASS** for synthetic gallery tests,
deduplicated face observations, regenerated face geometry/gallery,
restored decisions from the previous CSV, and review of uncertain
suggestions. The operator now rates the HTML review **useful**.
This is tool usability acceptance, **not recognition acceptance**.

The operator supplied the following **pseudonymous** report aggregates.
These are counts of **top-ranked face observations**, not unique faces,
tracked people, verified recognitions, or independently confirmed
identity matches. `Strong review suggestions` is **zero in every
row**; maximum cosine values are below the exploratory strong-review
threshold in every clip.

| Clip / operator-stated presence | Alias | Top-ranked observations | Maximum cosine | Strong review suggestions |
|---|---|---:|---:|---:|
| V01 (P02, P03) | P01 | 1 | 0.1352 | 0 |
| V01 | P02 | 18 | 0.4962 | 0 |
| V01 | P03 | 5 | 0.5565 | 0 |
| V01 | P04 | 4 | 0.2618 | 0 |
| V01 | P05 | 8 | 0.3878 | 0 |
| V02 (P01, P02) | P01 | 16 | 0.4897 | 0 |
| V02 | P02 | 11 | 0.5123 | 0 |
| V02 | P03 | 0 | — | 0 |
| V02 | P04 | 4 | 0.3081 | 0 |
| V02 | P05 | 7 | 0.3791 | 0 |
| V03 (P01, P04, P05) | P01 | 44 | 0.4895 | 0 |
| V03 | P02 | 115 | 0.3682 | 0 |
| V03 | P03 | 40 | 0.3322 | 0 |
| V03 | P04 | 198 | 0.4088 | 0 |
| V03 | P05 | 3515 | 0.4903 | 0 |

The top-rank counts sum to 36 observations in V01, 38 in V02
and 3,912 in V03: **3,986 retained observations** overall,
consistent with the successful bounded catalogue run.
V03 is heavily dominated by P05 ranking (3,515 of 3,912
observations), which is **not proof** that P05 is correctly
recognized or even the most frequently visible person. The
operator also reports an **incorrect P04 identification/ranking
in V03**. Preserve that as a human-review finding without
turning the aggregate table into a claimed per-face ground truth.

**Crucial qualitative evidence about V03:** the same three
people recur across approximately 180 selected frames, shot
from an elevated one-storey window roughly 10 m away; no
person directly faces the camera and no frame presents an easy
complete frontal face. Image quality could be better. The
operator reports **one of those three people has no detection
in any frame shown in the HTML review shortlist**, even though
the person appears repeatedly in the underlying footage.
Do **not** infer that all 180 frames lack a detector box for
that person: the gallery samples a small subset, biased toward
the highest cosine rankings. The operator observes that the
correct people's aliases often score highest, but absolute
similarities remain weak and **no strong review candidate**
was produced. Relative nearest-neighbour ranking alone does
not constitute reliable automatic recognition.

**Bounded-discard limitations:** prior run saw 4,043 raw,
3,986 retained, and 57 discarded post-NMS candidate faces
across 17 overflowing frames. The operator believes some
discarded candidates were useful genuine faces, a plausible
hypothesis **not demonstrated** by the saved report: it lacks
their geometry and individual evidence. Multiple detections
across frames can belong to one person and false positives
can be ranked above true smaller/oblique faces. Do not call
discarded candidates 57 distinct people or 57 true faces.

### Explicit next acceptance requirements (experimental scope only)

- **WI0155-QUALITY-01 — Human-anchored person coverage.** Use the
  existing local V01–V03 frames and private review observations
  to create a *small operator-selected* ground-truth set of
  frames/people actually visible (including the V03 person
  missing from the shortlist). Distinguish visible body,
  visible face, detected box and face evidence usable for
  recognition. Maintain private aliases and do not upload
  frames or person mappings. The denominator is real,
  operator-verified person appearances, not top-rank counts.
- **WI0155-QUALITY-02 — Candidate-selection fairness.** Evaluate
  time-diverse, person-diverse and low-quality examples, not
  exclusively the highest cosine observation per known alias.
  Include at least one deliberate negative/missed-person case.
  A person absent from the *gallery* must not be counted absent
  from all detections until directly verified across sampled
  source frames.
- **WI0155-QUALITY-03 — Raw vs retained face-box comparison.**
  When explicitly approved, inspect bounded subsets of the
  **pre-top32** YuNet candidates, including *discarded* boxes
  and their confidence, strictly privately and without
  embeddings or person assignments. Test whether high-confidence
  false positives displace actual small or oblique faces.
  Reconstructing only retained boxes cannot answer this question.
- **WI0155-QUALITY-04 — Separate discovery from recognition.**
  Assess whether the actual visible people have at least one
  useful detected face, independently of whether SFace produces
  a confident known-person suggestion. Evaluate human
  inspectability and relative rankings separately from
  calibrated identification accuracy. Keep exploratory score
  thresholds and production identity decisions unchanged
  until sufficient labeled evidence supports any change.
- **WI0155-QUALITY-05 — No automatic production integration.**
  Treat the full bounded experiment as technically successful,
  the gallery as locally accepted, and **reliable identification
  from this difficult oblique/distant cohort as not demonstrated**.
  Preserve existing approved source frames, case file, private
  reports and CSV reviews until the next focused assessment.

**Decision gate:** obtain the local ground-truth/coverage evidence
and discarded-box examples before changing detector model/threshold,
raising limits or integrating video identification into `main`.
This checkpoint records evidence and requirements only; no model,
database, code path or production behavior changes are approved.

## Experimental checkpoint 18 — image-equivalent three-tier user workflow (2026-11-10)

**Maintainer clarification (supersedes any reading of checkpoints
17/earlier that would require high-confidence auto-assignments):**
The target video-identification **review experience** is the same
as the still-photo one, with three outcomes:

1. **Automatic** only if both the configured identity similarity
   threshold and the configured top-vs-runner-up margin pass,
   with safeguards against wrong-person auto-assignment.
   The maintainer anticipates **fewer than ~25%** of cases
   being eligible. Do not invent or hard-code new threshold
   values from the experimental score distributions.
2. **Semi-automatic** otherwise, when the matcher can provide
   **2 or 3 ranked existing-person candidates** that the
   reviewer can accept or correct. The maintainer anticipates
   **~50–60%** of cases. A correct identity within the **top
   three** is meaningful even if top score/margin would not
   allow automatic assignment. Present rank, score, and
   ambiguous evidence as suggestions, not verified identities.
3. **Manual** when useful suggestions are absent, an unknown
   person is shown, or visual evidence needs direct human
   handling. The reviewer may select or create a person
   and add approved identity evidence through the **existing
   governed still-image workflow**, which may improve future
   suggestions. The maintainer anticipates **~20%** of cases.

These proportions are rough planning expectations, **not
per-clip quotas or measured model performance**. The categories
should be mutually exclusive in the UI for each *human-reviewable
person instance/observation group*, and repeated detections of
the same recurring person across adjacent frames must **not**
inflate the denominator or generate thousands of review tasks.
The precise association/grouping and definition of a review
unit remain design questions, so do not claim existing code
already satisfies this end-to-end workflow. Automatic decisions
must prioritize precision/safety over hitting any quota.

**V03 interpretation:** Three known people are present from
an elevated camera about 10 m away with oblique, nonfrontal
faces and limited detail. The operator would consider a
**correct suggested person** successful here and does not
expect score/margin suitable for auto-assignment. Stop
describing absence of strong auto-suggestions as, by itself,
a failure of the target workflow. **Separate** (a) whether
a person yields at least one reviewable detected face,
(b) whether the correct known identity reaches top-1/top-2/
top-3 suggestions, and (c) whether score+margin support a
safe auto-assignment. One of the three people is absent from
the reviewed HTML *shortlist*, not necessarily from all
processed frames; this coverage question remains open.

**Confirmed evidence/schema gap:** The existing catalogue
report's `review.diagnostics.rankedFaces` contains
`topReferenceAlias`, `topSimilarity`,
`runnerUpSimilarity` and `scoreGap`. It does **not**
retain the alias of the second-ranked person or the
third-ranked alias/score. Thus an aggregate `personRankSummaries`
table (or the current human review CSV) is insufficient
to calculate **top-2/top-3 hit rates**. Do not infer a
third candidate from per-person maximum scores or rankings
across different frames. Implement a small governed, strictly
private, per-observation top-3 evidence/selection capability
only after reviewing the existing operator reports and
ground truth. Preserve immutable ranks and allow reviewer
confirmation; do not weaken auto-assignment safeguards.

**New acceptance criteria:**
- **WI0155-WORKFLOW-01:** Mirror still-image human interaction
  and approval semantics across auto/suggestions/manual,
  including an explicit user correction and new-person path.
  Experimental video work remains isolated from main.
- **WI0155-WORKFLOW-02:** Evaluate *top-1/top-2/top-3
  candidate recall* for human-confirmed known-person
  observations; report no-suggestion, unknown, false-positive,
  and insufficient-evidence cases separately. Distinguish
  score calibration / gap precision for auto candidates from
  useful lower-confidence ranked review suggestions.
- **WI0155-WORKFLOW-03:** Measure operator workload by
  **person/episode review units**, deduplicated across
  repeated frames, rather than by frame detections,
  candidate score comparisons or raw model embeddings.
  Require at least one useful observation per visible
  person where face evidence actually exists; avoid
  assuming body visibility guarantees recoverable facial
  evidence.
- **WI0155-WORKFLOW-04:** Correctness of automatic
  assignments is a stricter safety criterion than
  top-3 suggestion usefulness. No threshold relaxation,
  arbitrary rank-based automatic action or person
  creation from unreviewed model output.
- **WI0155-WORKFLOW-05:** **Optional operator-provided
  evidence** may be reviewed in this conversation:
  previously saved **human-reviewed CSV** and the
  existing **bounded private catalogue JSON report**,
  optionally a prior single-pass report or a few
  specifically approved frame crops for ambiguous
  observations. The maintainer explicitly offered to
  provide these reports. Sharing is voluntary, and
  files may contain private paths, clip/person aliases,
  scores, timestamps and biometric-related information;
  review them only for this analysis, do not publish
  them to GitHub or include private payloads in PRs.
  No credentials, database dumps, original videos or
  whole extracted-frame directories are necessary.
  Earlier blanket `never upload` guidance should
  be read as **never automatically publish or upload**:
  voluntary user-authorized report sharing is permitted.

**Next gate:** Analyze the operator's existing private
human-review CSV and catalogue report, *then* decide whether
a focused top-3 diagnostic enhancement is needed. Do not
rerun full 252-frame inference merely to chase stronger
V03 scores. Treat face discovery, safe auto-assignment,
human-assisted ranking, and manual evidence enrichment
as separate acceptance outcomes. Do not merge the
experimental code into production main yet.
