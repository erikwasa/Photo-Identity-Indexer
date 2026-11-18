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

## Current production intent — presence-first, group-assisted video review (maintainer, 2026-11-18)

**Authoritative clarification, superseding older group-completion goals where
they conflict:** The durable production outcome is a **deduplicated set of
people confirmed present in a video** (video ↔ person associations), **not**
a fully reconciled or labelled partition of every detected face into
groups. Automatic provisional groups and ranked catalogue suggestions
are review aids. A person needs **only one credible, operator-confirmed
observation**; repeated appearances need not be tracked, merged or
labelled once presence is established. Presence is independent of the
group/crop providing initial evidence, and appearance timestamps are
not required. Never enroll video face crops/embeddings as still-image
reference exemplars.

**Example (G1–G6):** G1 is confirmed as person X and G2 as Y. G6 may
also show X but merging G6 into G1 is optional because X is already
confirmed present. G3 contains Z and W: the operator should be able
to exclude W's crop(s) from G3, **confirm W directly from that crop
without creating another group**, and confirm the remaining evidence
as Z. Do not incorrectly label W's crop as Z. If the operator has
accounted for the four identifiable people of interest, reviewing G4
and G5 is **not necessary**; they remain explicitly unreviewed, not
classified as background or proof no other people exist. Allow
deduplication and skip/hide of redundant groups. A direct person
presence action is not a group merge and can originate from a
single ungrouped crop.

**Production identification:** Very high-confidence automatic
person-presence assignment is a *rare*, separately calibrated and
authorized path requiring identity-score and top-versus-runner-up
margin safeguards, audit and correction/undo. High detector
confidence or CenterFace corroboration alone does not satisfy
identity confidence. Exact cutoff, persistence rules and false-match
acceptance remain undecided; WI-0155 presently performs **no**
production automatic or manual identity writes. Otherwise show
ranked suggestions for confirmation or allow manual identification
even without a group or successful rank. Video/person presence
must be idempotent across groups.

**Two different stopping rules:** Keep bounded, source-wide
automatic video scanning (or explicitly report incomplete coverage)
to avoid silently missing later faces. But the operator may stop
*manual group review* when satisfied that the relevant
human-identifiable people have been confirmed. Show unresolved and
unreviewed group/observation counts and allow resuming; do not
represent the operator's stopping decision as proven complete
person recall. Production success is **minimal human effort to
reach adequate distinct-person presence**, not mandatory
consolidation, dismissal or labelling of all groups.

**Acceptance priorities updated:** Assess at least one usable face
per distinct identifiable person, singleton and crop-only
identification, direct video/person presence without grouping,
safe correction of mixed groups, duplicate-presence prevention,
review skipping/resuming, wrong-person protection, sampled
false-face burden, model/runtime cost and reported evidence gaps.
Historical full-G/C/N/U exhaustive human reviews remain
experimental evidence and should not become a production
requirement to resolve all provisional groups.

## Current maintainer direction — after V01/V02 human review (2026-11-15)

**Prioritize a usable production review over perfect recognition scores or more
V03-only experiments.** The practical goal is to identify **distinct people
present**, using provisional groups only where they save operator work.
Avoid demanding merges of redundant groups or repeated background dismissals;
provide a reviewable path for every useful face, including single-frame
appearances and direct crop identification without grouping.
Manual merges/splits and false-group dismissals also count toward review
burden; reducing cluster count by hiding genuine faces is not progress.
A correct existing-person suggestion anywhere in the top three is excellent
when available, **not a mandatory outcome for every face or person**.
Unknown/ambiguous/manual identification is normal and must remain possible.

**Separate the evaluation roles of the clips.** V03 is a hard,
distant/oblique, low-quality case useful for face discovery, background
rejection, over-splitting, missed-singleton and grouping stress tests.
The maintainer did **not** review top-three suggestion accuracy during the
PR #809 V03 human review, and does not consider V03 suitable as the deciding
top-three benchmark. The audit script's 0/3 top-three result on three
labelled C-groups is a valid computational comparison *for those selected
groups*, but it is **not a human-reviewed judgement of suggestion utility
or a general recognition failure**. V01/V02 have now supplied a bounded top-three suggestion result
on labelled human review groups: one of two distinct known people
appeared within the top three on an initial group in each clip.
Add only a few approved, already-local representative videos if
a material unanswered product question justifies them.

**V01/V02 targeted review is now complete.** The operator reviewed all
V01/V02 C-groups and each N/U observation from the existing PR #808
report, without needing new model inference, database queries or
extraction. Use these results as the current baseline for recognizable-
person coverage, background dismissals, manual merges, singletons
and partial top-three usefulness. Proceed with at most one bounded,
review-oriented implementation improvement aimed at compact grouping
plus safe singleton/noise access, then compare its human workload
to this accepted baseline. Stop further threshold sweeps, V03
recognition tuning and ever-larger samples when progress is
marginal; document limitations and choose the smallest safe next
slice or defer. Keep the human-governed three-tier workflow and
original-video/still-exemplar isolation rules unchanged.

**Earlier V03 evidence (PR #809):** PowerShell synthetic gallery/rank
test and three V03 galleries passed locally; all four private aggregate
measurement scripts completed with validated evidence. Of the
corroborated-only V03 **18 C-groups**, the reviewer kept 3, marked 15
for merge, dismissed 0 and left 0 unresolved. This is **3 proposed
final groups after human decisions**, *not* 3 automatically generated
groups over all V03 faces: only 54 of 288 corroborated candidates were
clustered; 234 corroborated candidates remained DBSCAN noise. In
96 quality-biased N samples, 69 were labelled real faces, 0 background,
27 uncertain, with three known people represented. In 96 quality-biased
U samples, 15 were labelled real faces, 67 background, 14 uncertain,
with two known people represented. Neither sample is a population
precision/recall estimate. CenterFace must remain advisory; it may
prioritize candidates but cannot safely discard the uncorroborated
true faces. See operations checkpoint 42 and the final work-item
checkpoint below for completed V01/V02 results and the next decision.


**Current video-level review requirement (maintainer, 2026-11-10):**
The review output is a small set of **provisional face/person groups per
video**, each showing several of its best crops plus top 2–3
suggested identities from **all eligible people in the production
catalogue**, not a preselected five-person shortlist. For the historical V03 grouping experiment,
three groups (one per actual person) were an *efficiency ideal*;
additional groups due to angle, uncertain merges or false detections
are acceptable when the human can establish the actual people without
resolving them all. Human identification produces a unique
**video ↔ person association** without an exposed appearance
timestamp. Video-derived crops and embeddings may be used temporarily
to detect, group and review faces but **must never be inserted into
the still-image reference embedding collection or used to improve
future identity matching**. An authorized user may still create a
person through the regular workflow, without using video frames as
reference exemplars.

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

## Revised pre-merge proof and explicit post-merge boundary (maintainer, 2026-11-11)

This section is the **current merge gate** and supersedes earlier
feasibility-only stopping rules wherever they conflict. The earlier
checklists, experiments and GROUP/WORKFLOW numbers remain historical
evidence and/or follow-on design requirements; do not treat every
historical production requirement as a condition for merging
`wi-0155` into `main`.

**Product proof before merge:** Demonstrate that an explicitly approved
video offers **efficient candidate/group-assisted review that permits
confirming at least one usable face for every human-identifiable person
in the selected evaluation footage** without needing to label or merge
every provisional group. The reviewer can confirm a person directly
from a singleton or cropped face, correct a mixed group, deduplicate
existing presences and explicitly leave irrelevant groups unreviewed.
Evaluate useful **top-three suggestions from the entire eligible
production people catalogue**, not a hand-selected five-person cohort. Unknown/unidentifiable outcomes must remain possible. A correct
top-three suggestion on a difficult distant face is valuable even if
automatic assignment would be unsafe. Do not infer universal recall
from a small reviewed test suite.

### Required development/evaluation gates before `main`

- [ ] **FACE-QUALITY-01 — Reduce false-face workload without losing people.**
  Establish the reviewed V03 baseline (64 groups; 55 background
  dismissals, six proposed merges, three retained; reconcile true
  unassigned candidates with matching grouped JSON when available).
  Compare improved candidate validation/prioritization against labelled
  true-face and background examples. Distinguish detector candidate
  precision from within-person grouping quality. Repeated observations,
  group cardinality and cosine similarity are NOT face validators;
  every true, even singleton or oblique, identifiable face must have
  a reviewable path. Report dismissed/background groups, true people
  covered, false merges, over-splits, group-cap saturation and
  unassigned candidates. Meaningfully lower the false-group burden
  versus baseline without losing any of V03's three actual people.
- [ ] **GROUP-QUALITY-01 — Reuse proven still-photo grouping ideas.**
  Evaluate `ProvisionalFaceDbscanClusterer`, the not-same/noise
  semantics, and `ProvisionalFaceClusterKnownPersonAdvisory` from
  M25/WI-0113–0116 against the current video streaming grouper,
  with the SAME private reviewed cases and model-compatible vectors.
  Adapt rather than copy photo DBSCAN thresholds, minimum-sample
  rules or independent-photo vote counts; a single video frame may
  be the only valid appearance and adjacent video frames are
  correlated evidence. Never merge reliably simultaneous different
  faces. Keep genuine uncertain/ungrouped evidence visible instead of
  silently discarding it at a fixed 64-group ceiling. Prefer fewer
  distracting groups, but prioritize usable evidence for distinct
  person-presence decisions and zero wrong-person associations over
  perfect compression. Count **necessary** actions to establish each
  person, not merges of groups showing an already-confirmed person.
  Allow direct confirmation from an ungrouped singleton, crop
  exclusion/split from a mixed group and optional merge only when
  useful. Preserve genuinely unseen people and explicit unreviewed
  groups instead of treating them as background or an absence claim.
- [ ] **SAMPLING-01 — Content- and duration-adaptive frames.**
  The count of **frames sampled per video** (not frames per image)
  is decided from the duration and observed content, rather than
  fixed 12/24/36/120/180 choices. Make an initial whole-video
  coverage pass; selectively increase temporal density at scene
  changes, movement, possible entrances/exits, newly seen or
  poorly observed people, and uncertain detections, including
  background people. Avoid spending hundreds of frames on
  unchanged scenes where additional frames add no information,
  but NEVER stop evaluating the remainder of a video merely
  because a known face was found early. Define a bounded,
  deterministic/replayable plan, maximum unsampled temporal
  gaps, CPU/time/frame/disk limits, safe chunking or explicit
  continuation for longer videos, and an honest report when
  coverage remains incomplete. Reuse existing WI-0155 extraction
  safety and WI-0190's source-free planning concepts where
  suitable; its current 24-frame limit and manually supplied
  scene boundaries do not solve adaptive face discovery.
- [ ] **CATALOGUE-TOP3-01 — Real full-archive recognition proof.**
  Replace the selected-person (currently max 32, typically five)
  experimental scope with a frozen, bounded, read-only view of
  **every eligible canonical person** and compatible current SFace
  still-photo exemplars. Implement actual per-person top-1/top-2/
  top-3 identity+score ranking for reviewable groups using
  uncorrelated/diverse evidence, not just the present runner-up
  score. Evaluate operator-confirmed correct identity within top
  1/2/3, wrong top-1 proposals, ambiguity, no-suggestion, unknown
  and negatives across the *entire* catalogue. Preserve exact
  model/hash/alignment, eligible assignment filtering, stable
  snapshot provenance, privacy and separate read-only access.
  An initial proof may rank human-confirmed true-face crops independently
  of noisy groups; final acceptance requires group-level suggestions
  on **suitably identifiable** V01/V02 or additional approved cases.
  A correct top-three suggestion is valuable but is not required for
  every difficult face. Do not use the unreviewed-for-suggestions V03
  PR #809 0/3 as a hard merge blocker, chase automatic assignment,
  or tune thresholds merely to raise that single metric.
  Do not auto-assign, create canonical tags or enrich photo
  identity references with video crops/embeddings.
- [ ] **DIVERSE-VIDEOS-01 — Evaluate a small representative corpus.**
  Keep V01–V03 as regression cases: use V03 for the unusually
  hard distant-face/background-grouping workload, **V01/V02 first**
  for top-three utility where the faces are actually identifiable.
  Reuse existing private results before any rerun. Only if necessary,
  add a small number of expressly approved, already-local videos
  that cover a material gap (clear known faces, short appearances,
  overlapping people, unknown people or no-face negatives). Do not
  impose a four-to-six-new-video quota or keep evaluating without
  meaningful information gain. Annotate by a human who is
  actually visually identifiable and which intervals/persons
  may be missed; a body without a recognizable face does not count.
  Record per-video identifiable-person presence found, necessary
  actions to reach that outcome, direct singleton/crop decisions,
  corrections of mixed groups, skipped duplicate groups, explicit
  unreviewed evidence, optional top-three successes/uncertainty,
  sampled false-face burden and processing cost.
  Stop once the evidence supports a bounded decision about a safe
  useful next slice; avoid tuning solely to V03.
- [ ] **MERGE-01 — Safety, integration and operator sign-off.**
  Reconcile `wi-0155` with current `main`; verify focused
  and repository tests, Windows PowerShell 7.6 synthetic tools,
  bounded full-catalogue performance, frame hash/source integrity,
  no PostgreSQL writes or unintended OneDrive hydration, private
  report retention/cleanup, and absence of automatic processing.
  Produce an aggregate per-video acceptance comparison and
  document the chosen in-repository experimental boundary versus
  a separate component, plus operational and rollback plan.
  The maintainer explicitly accepts the evidence and integration
  scope before a PR targets `main`.

**Post-merge implementation, not pre-merge proof:** Production
video/person presence persistence with transactional audit/correction,
production review UI and executable merge/split/person association,
automatic canonical assignment or still-photo three-tier workflow
integration, scheduled/background processing, mass archive processing,
cross-video person tracks and time-range identity history remain
separately scoped. The isolated gallery can record manual decisions
for evaluation; no production writes should be introduced to
fulfil this proof.

**Acceptance principle:** A video is not a success merely because
the output has few groups: suppressing hard real faces would make
the number look better. Equally, all three V03 people appearing
somewhere among 64 noisy groups is not yet acceptable usability.
Compare *person coverage + false merges + review burden + correct
full-catalogue suggestions* together. Exact numerical quality gates
can be set using the diversified, labelled evaluation corpus rather
than by guessing thresholds from one difficult clip.

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

## Experimental checkpoint 19 — per-video person grouping and full-catalogue suggestions (2026-11-10)

**Maintainer accepted PR #787** and clarified the intended production
result: **one group per actual person in a video**, each containing
the best few face crops and two or three ranked existing-catalogue
suggestions. V03's realistic ideal is **three groups**, but a
modestly larger number is acceptable when the same person appears
different across angles or detection quality, and when false
detections cause extra provisional groups. The user must be able
to **merge, split, dismiss and identify** groups without assigning
every frame. After identification, the only lasting intended
semantic result is that the person **appears in that video**,
with no user-facing requirement to retain or display the position,
time interval, or per-frame occurrence. Internal time/frame references
may assist temporary grouping, traceability, and review, but are not
the core persisted person-in-video relationship.

**Explicit non-learning boundary:** Video frames, extracted crops
and their face embeddings must **not** be enrolled as reference
exemplars, persisted to enrich the canonical still-face identity
index, or used for future matching. Temporary derived vectors are
permitted for inference, deduplication and grouping within the video,
subject to explicit retention/cleanup policy. Manual review may
identify an existing person or create a new person, but video crops
must never become reference training material; a newly created
person may lack a still-image reference embedding until the ordinary
image review workflow supplies one. Do not conflate creating a person
record with updating their recognition template.

**Full-catalogue requirement:** Video suggestions must rank against
the **entire eligible known-person population** available in the
governed read-only production catalogue snapshot, currently reported
by the maintainer to contain **100+ people**, not only the five
hand-selected identities used in this experiment. The existing
private experimental catalogue reports confirm `selectedPeople=5`,
`eligiblePeople=5` and `exemplarsLoaded=15`. The current
`VideoFaceCatalogueCasesLoader.MaximumPeople=32` and per-person
exemplar-bounded matcher mean the present experiment cannot simply
select or evaluate 100+ persons without a separately approved
scalable, read-only candidate retrieval/ranking change. Design it
so all eligible people can compete, bounded by governed reference
eligibility and resource controls; do not silently change the
production DB or ingest video embeddings. Maintain a consistent
read-only snapshot for evaluation and preserve privacy/OneDrive
guardrails.

**Evidence actually supplied:** The maintainer voluntarily
provided an existing single-pass report, bounded tiled-1024
report, and completed human-review CSV. Those raw private files,
individual comments and person/clip mappings remain **out of
GitHub, PRs and CI**. Safe aggregate interpretation:
- Both reports used five selected people and fifteen reference
  exemplars; **neither measures ranking among 100+ people**.
- With identical evaluated frame counts per clip, the
  single-pass report has V01=30, V02=36 and V03=35 retained
  detections, whereas tiled-1024 has V01=36, V02=38 and
  V03=3,912. Tiled V03 flags every frame as containing at
  least one candidate, but dramatic raw growth is **not
  independent evidence of additional real people**.
- The completed CSV contains **40 rows**, including **four
  optional frame-level overflow entries** and **36 face
  observations** with human decisions. It captures both useful
  correct suggestions and misses/false positives. As the
  shortlist was deliberately selected, these decisions must
  not be used as an unbiased overall detection accuracy estimate.
- Existing diagnostic `rankedFaces` data retains only the
  top alias, top score, runner-up **score** and margin: no
  runner-up identity, no third identity/score, no per-person
  group membership and no reference to the full catalogue.
  Neither report can support a measured full-catalogue
  top-3 hit rate, let alone accurate group count.

### Required review contract and evaluation gates

- **WI0155-GROUP-01 — Provisional grouping:** Given one
  video, emit a bounded manageable number of provisional
  candidate-person groups. Within each group select several
  high-quality **diverse** crops (different moments/angles
  where useful), not merely the highest detector confidence,
  highest identity cosine, or thousands of near-duplicates.
  Keep face grouping **independent of which known-person
  alias ranked first**: top-ranked aliases can be wrong,
  and an individual can rank differently across frames.
- **WI0155-GROUP-02 — Reviewer controls:** Allow one
  reviewed identity or unknown/dismissed status per group,
  with explicit **merge**, **split/reassign crop** and
  **dismiss false detection** to correct over-/under-grouping.
  Identifying a group must not automatically propagate to
  other weakly-associated groups or silently create identities.
  The reviewer can reassign groups later.
- **WI0155-GROUP-03 — Ranked suggestions:** Against a
  frozen read-only snapshot of **all eligible people**,
  provide two/three named ranked person options per
  *group*, with score and uncertainty clearly indicated.
  Preserve enough provenance to evaluate full-catalogue
  top-1/top-2/top-3 reach for operator-verified groups.
  A low-confidence correct third-place suggestion counts
  as useful semi-automatic review; **automatic assignment
  still requires the existing score and runner-up margin
  safeguards**. Never turn group voting or raw observation
  counts into an unchecked auto-assignment.
- **WI0155-GROUP-04 — Video-person association:** After
  review persist at most one active video-to-person presence
  link for an individual (unique by video/person) and allow
  correction/removal. The end-user feature is **presence,
  not an occurrence timeline**. No video-derived reference
  image/embedding may enter permanent still-face identity
  training/evidence tables. Define the relation and
  transactional review audit before integration.
- **WI0155-GROUP-05 — Group-level metrics:** Evaluate
  operator-labelled *true people found*, missing people,
  extra or fragmented groups, falsely merged people,
  false-positive groups, average/median groups shown per
  actual video person, useful top-3 suggestion presence,
  and human decisions needed to finish a video. Do not
  measure success by raw candidate volume or per-frame
  highest score. Compare single-pass vs tiled candidate
  discovery without asserting V03's 3,912 candidates
  are 3,912 faces or 3,912 review tasks.
- **WI0155-GROUP-06 — Isolation and performance:** Keep
  all experimentation off `main`, preserve existing
  accepted frame inputs and prior private reports. Use
  read-only `photoidentity_main` snapshot access, no
  unexpected OneDrive hydration, bounded memory/model
  time and no production writes before a separate
  integration decision. Capture temporary processing
  geometry/embeddings privately with explicit cleanup.
  Recheck speed and memory at 100+ eligible identities.

**Decision:** Treat grouping + full-catalogue top-3 suggestions as
the next experiment's acceptance targets. First design and verify
a small, safe prototype using the already extracted V01–V03
frames; do not launch another unrestricted tiled-1024 full
catalogue evaluation or tune thresholds to reach arbitrary
automatic percentages. The present PR is **requirements/docs
only**; grouped inference and production association logic
are **not implemented**.

## Experimental checkpoint 20 — opt-in streaming provisional group prototype (2026-11-10)

**Implementation proposed on isolated WI-0155 child branch:** first
substantial video-level grouping experiment, deliberately NOT
production integration. Based on the same approved local frame
manifests, detector, aligner and SFace embeddings, an optional
`--provisional-groups` flag adds temporary **within-video**
comparison during the already authorized experimental catalogue
run. This does **not** identify persons, infer their timeline
in the UI, write to PostgreSQL, add reference embeddings,
or modify original videos/extracted PNGs.

### In-memory grouping rule

- Compare face embeddings against up to three diverse
  representative vectors within each previously formed
  provisional group. Similarity is for **within-video
  group association**, never for automatic catalogue
  identity assignment. Initial explicit experimental
  minimum cosine = **0.82** and winner-vs-next-group
  gap = **0.04**. These are **untuned prototype constants**,
  not production-grade model calibrations or per-person
  identity thresholds.
- Two separate detections in one frame cannot join the
  same group. Ambiguous assignments are conservatively
  split into extra groups rather than silently merging
  people. Catalogue top-ranked aliases are **not used**
  to determine group membership.
- Keep up to three representative faces per group, from
  nonadjacent frames where possible, preferring larger
  and higher detector-confidence crops (a crude quality
  proxy, **not** certified face recognition suitability).
  Group IDs are private, ephemeral, local to the clip
  and are **not** person IDs.
- Bound to **64 provisional groups per video** and
  **3 representative crops per group**. Once capacity
  is exceeded, increment an explicit **unassigned
  observations** count rather than silently claiming
  detected persons; neither assigned nor unassigned
  counts are verified unique faces.
- Report only group aliases, observation counts and
  the coordinates of up to three selected crops;
  SFace vectors and per-observation group membership
  stay in memory and are **not persisted**. The private
  `diagnostics.provisionalGroups` field is absent
  unless the flag is explicitly used. Existing reports
  and production behavior remain unchanged.

### Operator preview and correction boundary

A new Windows PowerShell 7.6
`New-VideoFaceGroupReview.ps1` script turns a
new **private grouped catalogue report** into
an offline HTML gallery showing per-group best crops,
without requiring another coordinate reconstruction
run. Reviewers can **annotate** provisional keep,
merge-into, split-needed, dismiss and cannot-tell
decisions, plus optional selected-person pseudonyms
and notes, then explicitly save/import a local CSV.
These annotations do **not** yet execute merges,
splits, video/person associations or identity writes.
No appearance times are displayed. The private
gallery requires user-initiated saving to an
unsynced local destination and may not be
published to GitHub or CI.

Synthetic C# unit tests cover repeated observations,
diverse samples, same-frame exclusion, conservative
splits, bounded capacity/unassigned accounting and
absence of vectors in the group-report schema.
The separate Windows synthetic PowerShell smoke test
covers group images/CSV/HTML, invalid boxes and
source-frame hash integrity. Existing Windows
gallery acceptance stays valid; this branch
does not alter the previous review CSV schema.

### Progress and explicit limitations

- **GROUP-01 — partial:** actual opt-in
  within-video clustering and best sample selection
  are implemented, but their grouping accuracy
  on V01/V02/V03 is **not yet measured**. A fixed
  64-group budget may saturate badly with V03's
  noisy tiled detections. If so, the prototype is
  **not** an acceptable video person inventory.
  No higher-confidence claim is appropriate.
- **GROUP-02 — partial:** HTML annotations for
  group merge/split/dismiss are captured in a
  private CSV. There is no executable validated
  regroup/identity workflow yet.
- **GROUP-03 — pending:** full eligible 100+ person
  catalogue, actual named top-2/top-3 group suggestions
  and top-k accuracy are **not implemented**.
  The existing experiment still compares only
  the operator-selected five people. Do NOT label
  nearest five-person ranks as full-catalogue
  suggestions.
- **GROUP-04 — pending:** no production
  video/person presence table or identity writes.
- **GROUP-05 — partial:** group count, assigned,
  unassigned and samples are measurable; person
  coverage and correct merges/false positives
  require the maintainer's new human review.
- **GROUP-06 — partial:** model/code remains on
  the isolated branch, the new mode is strictly
  opt-in, exports no reference vectors, and
  has group/sample caps. Model-run performance
  and source integrity still need local acceptance.
- **WORKFLOW-01..05 — pending:** no production
  automatic/semi-automatic/manual group workflow,
  no video frame learning, and no change to
  normal still-image identity assignment.

**Decision gate:** Focused C# tests and CI plus
Windows synthetic gallery test are required;
operator inspection of group count, missing
people and false groups on a new private grouped
V03 run is needed before this prototype can
inform grouping architecture. The new mode
requires a fresh explicit inference run because
previous JSON reports contain **neither cross-frame
SFace embeddings nor group membership**. Reuse
existing frames and the same read-only database
connection, do not extract videos again.

## Experimental checkpoint 21 — V03 local acceptance exposes background-group workload (2026-11-11)

**Operator local acceptance of PR #789:** C# tests **PASS**,
Windows PowerShell synthetic gallery **PASS**, real bounded
V01–V03 group inference **PASS**, HTML group gallery **PASS**,
and explicit CSV save/import **PASS**. The maintainer reports
that **all three known V03 people have at least one provisional
group**, so the prototype reached a meaningful *gallery person
coverage* milestone. Overall grouping usefulness remains
**partially useful**. The operator did **not** supply per-clip
group counts or unassigned totals; do not infer them.

**Main remaining defect WI0155-GROUP-07 — background objects:**
A **majority of provisional groups are obviously non-face
background objects** according to the human review, despite
the detector assigning them face candidates and the grouping
model finding sufficient vector similarities. This is a
meaningful precision/reviewer-workload failure, separate from
the successful coverage of the three actual people.
Group size, detector confidence, similarity and repeated
observations alone **do not prove face-likeness**, and applying
a hard group-size threshold risks excluding real small,
oblique or poorly-lit faces. Keep all groups reviewable and
do not suppress them or auto-assign identities based on
proxy quality alone.

**Operator request WI0155-GROUP-08 — more crop evidence:**
Allow **up to five quality-ranked, temporally diverse crops**
per provisional group rather than a hard three. The
limit remains bounded; groups with fewer samples remain
valid. The older three-crop report format and existing
CSV keys/columns remain supported.

### Focused proposed follow-up

An isolated gallery usability improvement provides a
**single-click dismiss for obvious non-face background
groups**, plus **optional reversible filters** to hide
dismissed groups and view only groups with at least three
observations. Filtering is purely client-side:
the saved CSV still includes **every** provisional group,
its original identifier and exact review decision.
The repeated-observation filter is clearly marked
as a *review navigation aid*, **not a validated
face/background classifier**. Dismissal is a reviewer
annotation, not detector learning or a database write.
No automatic false-positive filtering is claimed.

Increase `VideoFaceProvisionalGrouper.MaximumCropsPerGroup`
from 3 to **5**, retain all prior constraints
(conservative within-video grouping, same-frame
separation, max 64 groups, no reference enrollment).
The offline Windows gallery accepts **1–5** crops,
including historical 1–3 crop reports. The grouping
algorithm is otherwise unchanged so that comparisons
remain interpretable. New reports with five candidates
require a fresh opt-in inference run; **the already
successful PR #789 report can immediately test gallery
triage with no new ONNX or DB work**.

**Acceptance goals:**
- All original V03 groups and representative crops
  remain accessible even when a filter is enabled.
- One-click background dismissal and reversible
  filters work in the browser. The saved CSV preserves
  dismissed and hidden groups and can be imported.
- C# tests verify bounded five-crop diversity and
  replacement. Synthetic Windows gallery tests
  verify five crops, legacy three-crop reports,
  source immutability and invalid geometry rejection.
- Do not call this an accuracy improvement. A later
  separately evaluated **face-candidate precision**
  change needs labelled true/false candidate examples
  (preferably the already user-reviewed CSV plus
  false-positive group feedback), explicit measured
  true-face recall, and a person-coverage regression
  before touching detector thresholds/box selection.
  This is especially important for V03's oblique
  ~10 m faces.
- Group counts and unassigned candidates per clip
  remain essential acceptance information before
  considering grouping production-ready.
- Continue to treat `GROUP-03` full eligible
  100+ person top-3 suggestions, `GROUP-04`
  reviewed video-person association and executable
  merge/split controls as pending. Keep WI-0155
  isolated and do not enrich the still-image reference
  catalogue with video crops or vectors.

**Scope:** focused review-efficiency and representative
evidence PR only, no production processing,
PostgreSQL writes, model replacement or automatic
person identifications.

## Experimental checkpoint 22 — completed V03 group-review baseline (2026-11-11)

**Operator acceptance of merged PR #790:** One-click
background-group dismissal **PASS**, hide/reveal **PASS**,
and HTML CSV save/import **PASS**. Prior PR #789 C#,
Windows synthetic, real inference and group gallery had
also passed. The operator voluntarily attached their
completed **V03** offline group-review CSV for private
analysis. Its individual rows, notes, private paths and
person mappings are not committed. A derived,
non-identifying aggregate is recorded here:

| Human disposition | Provisional groups | Assigned face-candidate observations |
|---|---:|---:|
| Dismiss false detection | 55 | 326 |
| Merge group | 6 | 6 |
| Keep group | 3 | 3 |
| **Total** | **64** | **335** |

- All **64/64 groups have review decisions** and no
  unknown/split/pending dispositions in this supplied CSV.
  **55/64 (85.9%)** are dismissed as background/non-face.
  **326/335 (97.3%)** of *assigned-to-group*
  candidate observations belong to dismissed groups.
  This percentage describes only the reviewable group
  evidence, **not the entire source video or its raw
  post-NMS candidate population**.
- All six proposed merge targets are valid,
  terminate at kept groups and are noncyclic.
  Three kept groups remain if the manual merges
  were applied; these are the reviewer's
  **proposed outcome**, NOT merges already executed
  by the system.
- Critically **all nine nondismissed groups contain
  exactly one face observation**. In contrast,
  a background group may represent dozens of repeated
  detections (maximum **54** in this review).
  This is strong evidence that the current within-video
  cosine-based grouping **fails to join many of the
  visible genuine observations** while readily
  grouping background features. The problem is
  NOT simply lack of fourth/fifth crop representatives:
  the true-face groups are singletons. Increasing
  representatives to five improves evidence where
  available, but **does not fix grouping precision**.
- The grouper has a hard maximum of 64 groups,
  which the CSV demonstrates was reached. This
  CSV sums the **335 candidates assigned to
  groups**; it does **not** contain the original
  total observed or the number **unassigned**.
  The matching private `groups-tiled1024-*.json`
  is required to establish missing/unassigned
  observation counts. Do not infer them from a
  different historical run without verifying
  exact input/provenance.
- The operator already reported that all three
  actual V03 people appeared among these
  provisional groups. No clip-wide person recall
  or candidate-level precision has been proven,
  as the gallery can omit additional examples
  after the group cap is hit.

### New acceptance and evidence requirements

**WI0155-GROUP-09 — Repeatable private group-quality baseline:**
Add an **offline read-only PowerShell 7.6 measurement
tool** which validates the exact saved group CSV
and optionally a matching private grouped JSON report,
checks merge targets and cycles, reconciles candidate
totals, and prints only aggregate review statistics
(no groups, persons, notes, coordinates, paths or
embeddings). Establish this 64/55/6/3 result as the
first **reviewed benchmark** for any next grouping
proposal. If the matching JSON is absent, mark
unassigned counts **unknown** rather than assuming
zero. No database interaction or inference required
to calculate the baseline.

**WI0155-GROUP-10 — Separate face candidate precision
from identity grouping:** The next algorithm
experiment must address obviously non-face
candidates **before** they exhaust the 64-group
capacity, or otherwise provide a robust candidate
prioritization that does not hide low-confidence
genuine oblique/distant faces. Evaluate it against
reviewed background **and** all three V03 actual
people, with explicit true-face retention,
false-positive-group workload, grouping fragmentation,
group-cap saturation and unassigned evidence.
Never adopt a hard minimum occurrence-count
filter as a face validator: many background
groups recur and all retained true-face groups
were singleton observations in this review.
Keep a reviewable fallback for uncertain face
candidates rather than silent destructive exclusion.
No video-derived embeddings or crops may be
enrolled into permanent still-person references.

**Decision gate:** WI-0155 remains experimental.
The current grouping prototype demonstrates
operator-visible people but fails the intended
low-workload, per-person grouping requirement.
Do not proceed to full 100+ person suggestions
as though these groups were already clean;
it would waste retrieval work and burden the
reviewer. A following separate supervised
candidate-quality/grouping design may use
privately operator-reviewed examples as
**evaluation labels**, but must not turn
video crops into future identity reference data.

## Experimental checkpoint 25 — full-catalogue V03 acceptance and private rank triage (2026-11-11)

**PR #793 merged to `wi-0155`**, and its maintainer's focused C#,
documentation, synthetic gallery, read-only snapshot, top-three structure,
source-image hash, OneDrive exclusion and no-production-write checks passed.
The separate canonical people archive contained **241 current people**,
**240 eligible exact-SFace people**, and **596 current still-photo exemplars**
(up to three independently sourced photos per eligible person).
This confirms catalogue coverage/technical loading, **not face identification accuracy**.

For the latest complete **180-frame V03** cohort, the report and review CSV
agree on **3,912 post-retention detections = 407 assigned + 3,505 unassigned**;
the **64-group cap** is exhausted. The 64 reviewed groups comprise
**55 dismissed non-face/background groups (398 assigned observations)**,
six proposed merges and three kept groups (nine single-observation
true-face groups). The reviewer confirmed **three distinct identifiable
people**, no wrong-person merge in the reviewed groups, but only **one**
correct top-three match among **nine** genuine-face groups; **two of the three**
known people never received a correct top-three suggestion in the reviewed
groups. Both top-one and top-three success were reported for one group.
**V01/V02 human accuracy is not yet reviewed**. The separate 2026-11-10
baseline that had **335 assigned** observations was a **different** run;
do not combine its counts with this 407-assigned report.

**Interpretation:** This is a technical success of full-catalogue snapshot and
top-three data shape, but an explicit **quality failure** of both
review burden and meaningful full-archive recognition. Although 89.6% of
candidate detections are unassigned, they are not proven non-faces;
the first 64 groups are not a complete person inventory.
The 55 dismissed groups contain 398/407 assigned observations (97.8%).
An uncalibrated high cosine, high detector confidence, or multiple hits on
background objects cannot justify a destructive face filter.
Conversely, every operator-retained face group had only a singleton
observation; a minimum group-support threshold could remove every
reviewed true-face group. The fact that 240 of 241 people are eligible
does not establish that the **specific** three visible people each have
suitable/representative photo exemplars.

### PR #794 — private, zero-inference diagnostic increment (in review)

This increment remains isolated from `main` and changes **neither**
YuNet, SFace, candidate retention, grouping cosine thresholds nor frame
sampling. It closes a major observability gap before algorithm calibration:

- The existing private group gallery now shows each group’s actual,
  verified top-three catalogue **pseudonyms and cosine scores**, with
  an uncertainty warning; no real person names/IDs, image embeddings or
  automatic assignments are exposed. Older unranked reports remain valid.
- `Measure-VideoFaceGroupRankAccuracy.ps1` uses an existing same-run
  group CSV + full-catalogue evaluation JSON + private alias map,
  checks group/snapshot/mapping consistency, and emits only aggregate
  known-person top-one/top-three recall, missed/unlabelled/unknown groups,
  64-group overflow counts, and separate human-dismissed vs retained
  detector-confidence, crop-quality-proxy and catalogue-score medians.
  Even a high score for a human-dismissed **non-face** stays a false
  detection, not a person match. No inference, database or video reads.
- Accuracy metrics are **null/not evaluated** when the group CSV's
  optional `SelectedPerson` fields are empty. The V03 CSV currently has
  no such labels, so its previously reported one-of-three result is
  operator testimony, not a derived script claim. `P0001`-style labels
  must be supplied privately from the separate map and `UNKNOWN` is
  reserved for a genuine face absent from the eligible catalogue.
- Synthetic Windows CI fixtures prove top-one/top-three accounting
  by **distinct person** and by group, verify a high-scoring background
  negative and a missed real person, reject corrupt ranks or map snapshots,
  and ensure private names/aliases/paths never enter aggregate output.

**Decision gate:** Run the synthetic checks in CI first. A **small local
offline review only** of the existing V03 report/map and CSV is useful
after PR #794 merges; no fresh PostgreSQL snapshot, full 180-frame YuNet/
SFace pass or new video extraction is required. Continue towards
candidate-quality filtering and non-saturating group review **only after**
diagnostics show whether confidence/quality/rank signals actually separate
the operator-labelled real faces from the 55 dismissed groups. Dedicated
full-video adaptive frame sampling and multi-clip generalization remain
open. Production video/person writes and UI remain deferred.

**Acceptance progress:** `WI0155-GROUP-03` (actual full-archive top-three
retrieval) implemented but **real accuracy not accepted**;
`WI0155-GROUP-05/09` aggregate review and same-run reconciliation
implemented, with new labelled top-k and negative-class diagnostics pending
local review; `WI0155-GROUP-01/10` precision/saturation **not accepted**;
adaptive coverage **not implemented**; `WI0155-GROUP-06` experimental
isolation and read-only safeguards passed local checks but performance
and diverse-videos acceptance remain pending.

## Experimental checkpoint 26 — V03 labelled feature separation and review-budget counterfactual (2026-11-12)

**PR #794 merged** into the isolated `wi-0155` experiment. The maintainer ran its
private same-snapshot rank audit with **241 canonical people, 240 eligible,
596 SFace reference exemplars, 3,912 retained V03 detections, 407 assigned,
3,505 unassigned, all 64 groups reviewed**. Of the 64 reviewed groups, 55
were dismissed non-faces and nine retained real-face singletons representing
three known people; all nine groups received explicit private ground-truth
person pseudonyms and zero wrong-person group merges were observed.
Actual full-catalogue retrieval accuracy was **1/9 correct top-one groups,
1/9 correct top-three groups, and 1/3 distinct people represented in the
top three**. Two distinct people were missed. No group, real-face or
dismissed background, reached exploratory catalogue cosine 0.80.

The **median** feature separation was:

| Reviewed class (group-level) | Detector confidence | Crop quality proxy | Top catalogue cosine |
|---|---:|---:|---:|
| 55 dismissed background/non-face groups | 0.5954 | 0.1080 | 0.3208 |
| 9 retained genuine-face singleton groups | 0.6531 | 0.1949 | 0.4137 |

This establishes **directional group-level differences**, not nonoverlapping
classes or calibrated cutoffs. The existing 0.80 candidate-suggestion
threshold offers no useful separation for the reviewed V03 groups, while a
lower threshold may incorrectly assign background detections to real people.
Do **not** reinterpret these similarity scores as face probabilities or
assume any of the 3,505 unassigned detections are background.

### PR #795 — deterministic, non-destructive counterfactual (merged; local verification pending)

A new strictly private `Compare-VideoFaceReviewPriority.ps1` analyzes the
already annotated same-run CSV, report JSON and person alias map, first
using the existing cross-snapshot/merge/rank validator. It compares six
**label-independent** review orders (original group arrival, detector
confidence, crop quality, top catalogue similarity, confidence+quality,
and temporal-spread+quality) at small review budgets (8/16/32/48/64,
and all available groups). No ground-truth label enters policy ranking.
Results are *aggregate only*: true-face groups retained/omitted among
the reviewed 64, dismissed non-face groups shown, distinct labelled
people represented and people with a correct top-three candidate
within the selected group subset. All policies leave the 3,505
unassigned observations **unknown/unreviewed** and counted separately.
No deletion/rejection or write occurs; the current grouping
algorithm, detector, matcher, read-only PostgreSQL access, private
video inputs and video sampling remain unchanged.

- A candidate ordering is an **experiment** for reducing the first-pass
  human-review workload, not a validated face filter or a new production
  identification policy. Singleton preservation must be measured at every
  proposed budget; never require repeat observations.
- The temporal-spread heuristic uses frame positions of retained
  representative crops, not validated scene boundaries or proven complete
  sampling, and it is not adaptive extraction.
- Synthetic Windows tests require zero private data, cover a high-score,
  high-confidence *background object* outranking actual people, known
  people absent from top-three, genuine unknown faces, missing identity
  labels, invalid snapshots, aggregate privacy and conservation of all
  labelled groups at the full review budget.
- **Local acceptance:** run once on previously retained matching V03
  evidence after CI passes. Compare policies and decide whether any
  priority actually lowers background review work **without hiding any
  of the three labelled people** at a smaller budget. No renewed ONNX
  inference, SQL snapshots or video extraction needed for this checkpoint.

**Status toward requirements:** `WI0155-GROUP-03` structural top-three
and full-archive matching are complete but real accuracy **fails**;
`GROUP-05/09` baseline and labelled diagnostics accepted;
`GROUP-10` now has an offline candidate-ordering comparison
but no deployed background rejection; `GROUP-01` capacity and
person-level grouping still **fail** (64/64 groups, 3,505 unknown
unassigned observations), and adaptive whole-video sampling and
V01/V02 human verification remain open. Do not merge the experimental
`wi-0155` branch into main until quality and safety gates pass.

## Experimental checkpoint 27 — independent adaptive sampling plan, no source execution (2026-11-12)

While PR #795's six-policy *offline group-priority* comparison awaits
the maintainer's already-approved local evidence, a separate
**SAMPLING-01 implementation increment** can proceed without those
results. A new, pure `VideoFaceAdaptiveSamplingPlanner` and synthetic
tests plan up to ten-minute windows of an approved clip, with
deterministic whole-window timestamps no more than eight seconds
apart and an independent bounded stage for denser samples around
observed motion/scene/uncertain-face/possible-entrance or new-face
cues. Each window stays at or below 180 requested frames; longer
clips require explicit continuation, and optional refinement reports
when the per-window budget is exhausted. The code does not consult
human identity labels or catalogue similarity for frame ordering.

**Strict implementation boundary:** These are timestamps, not actual
examined frames, an automatic content-event detector, a frame
extraction runner, or proof of detecting every person. No change to
the currently working fixed manifests, full-catalogue runner,
64-group cap, matching, scoring or production data is included.
No video, models, people, PostgreSQL or other private inputs are
required for CI. The synthetic tests prove deterministic/bounded
planning, not face recall, runtime, or acceptable review workload.
The current 1/3 V03 correct known-person top-three result and the
3,505 unassigned detections remain open failures.

**Deferred host verification:** run the focused test and docs command
in `docs/operations/video-face-feasibility-wi0155.md` after merging
this PR into `wi-0155`. PR #795 is already merged, but its
private offline comparison still awaits local verification. Before satisfying SAMPLING-01, explicitly opt in
to a new, separately reviewed extraction-and-detection integration
and human-annotated varied-video comparisons (including short
appearances between coarse frames and incomplete long videos).
Do not merge the feature branch into `main` on the strength of
this planner.


## Experimental checkpoint 28 — approved local adaptive extraction bridge (2026-11-12)

The separate PR after #796 adds an explicitly requested `-AdaptiveSampling`
mode to the existing native-lossless/legacy coverage extractor. After
the operator approves a **specific already-local video**, the guarded
FFprobe step supplies its duration to the source-free .NET planning
command, which emits bounded timestamps for at most one ten-minute
window. The extractor then uses the unchanged FFmpeg path, output
manifest schema, per-frame size limit and owned-workspace lifecycle.
Each window gets a new private run; any remaining video is flagged for
**explicit** continuation, not automatically scanned. Cues may be
supplied only from *previously sampled* frames using strict private
bounded JSON, in which case nearby temporal samples are added without
identity labels. The historic fixed 12/24/36/120/180 controls remain
the default.

**Acceptance boundary:** This is the first executable link from planner
to actual source extraction but remains opt-in, isolated from `main`,
and requires Windows PowerShell 7.6 source-fidelity/continuation
verification later. Without independent motion/scene/appearance cue
generation it performs **duration-adaptive coarse sampling**, not a
self-calibrating content-adaptive pipeline. Re-extracting a refined
window currently duplicates its coarse frames. It does not invoke
YuNet/SFace automatically, perform a new full-archive identity snapshot
or auto-create catalogue tags. Recorded face-crop quality and
reviewed person coverage on diverse clips are still necessary before
SAMPLING-01 can be accepted.

CI runs C# synthetic tests for planning, cue schema, rejection and
continued window bounds, plus a PowerShell parse/option smoke test
without access to a private video. The consolidated operator procedure
is checkpoint 28 in the WI-0155 operations document. The pending #795
offline review-priority comparison, 64-group saturation and V03
full-catalogue known-person top-three failures remain separate gates.


## Experimental checkpoint 29 — detector-responsive two-pass sampling, offline only (2026-11-12)

A separate child PR after #797 adds an **explicit** `--derive-adaptive-cues`
command and stores the absolute adaptive-window start in new extraction
metadata. From a completed, owned **coarse-only** extraction and its
matching one-clip private detector report, the command derives bounded
refinement cues using face-count transitions, small face width and
weak/overflowed detector observations. It validates the exact planned
coarse timestamps against the frame manifest, metadata and the report;
it requires the actual frames to remain available. The output is a
**create-new private JSON file** that the existing opt-in
`-AdaptiveCuesPath` extraction switch can consume to produce a second,
denser temporary run.

Neither ranking nor known-person labels are input to the cue decision.
The original frames, detector report, database and video are unchanged.
The second pass remains **operator initiated**; no automatic scene or
motion detector, multi-window executor, duplicate-frame reuse or
recognition-on-extraction is introduced. Face-count fluctuations can
be background false positives; this work explicitly **does not**
declare a candidate a real face, merge video groups or improve
top-three recognition. The 64-group ceiling, false-face workload and
3,505 V03 unassigned detections are still unresolved.

The synthetic tests cover bounded cue kinds, exact provenance,
complete-window/real-file checks, no-face evidence, expected
transitions, safe private output, and refusal to overwrite or publish
inside the repo. No maintainer video verification is required to
merge this isolated development increment into `wi-0155`. **SAMPLING-01
acceptance remains open** until approved videos demonstrate that
the operator-selected, detector-responsive samples actually preserve
more human-identifiable people without excessive review burden.
See operations checkpoint 29 for the consolidated Windows procedure.


## Experimental checkpoint 30 — unassigned-candidate visibility without changing groups (2026-11-12)

After checkpoint 29, a separate, isolated opt-in `--unassigned-audit`
increment addresses part of the **GROUP-QUALITY-01** observability
gap: the 64-group cap historically left **3,505 V03 observations
counted but inaccessible as review crops**. The new mode does not
replace the streaming grouper, loosen its threshold, merge faces or
declare any observations to be background. It samples up to 96
otherwise unassigned private crop references across 12 temporal
sections (at most eight per section, two per frame), while keeping
the full unassigned count, all existing groups, person suggestions,
and the separate 180-frame/32-per-frame inference safety budgets.

The supplemental audit lives only in the existing explicitly
requested private catalogue report under
`diagnostics.provisionalGroups.unassignedAudit`. It contains **no
extra embedding vectors or canonical identities**. CLI stdout
contains only aggregate audit counts. Existing reports and normal
runs are unchanged. No database writes, source-video scanning,
automatic labelling, face suppression or production integration
are introduced. Synthetic tests exercise strata, deterministic
bounded selection, complete count reconciliation, unchanged normal
grouping, no overflow, JSON privacy and invalid input rejection.

**Acceptance is NOT passed:** sampled overflow may be mostly
background or miss unique true faces; it is not an accurate group,
a track, or a 96-item unbiased representation of the 3,505 cases.
It does not improve the 55 false groups or top-three recall on V03.
A dedicated sampled-crop gallery, labelled unassigned audit, and
comparable still-photo DBSCAN/noise/not-same experiment remain future
work. Human verification of new private V03 and diverse-video
evidence is postponed for the consolidated local acceptance session.
Operations checkpoint 30 contains the later opt-in commands.


## Experimental checkpoint 31 — first consolidated local results and reference-free adaptive bridge (2026-11-13)

The maintainer confirmed the experimental `wi-0155` branch at
PR #799's merge commit `a43db2bb`, 84/84 release C# tests passed,
and documentation `validate` and `generate --check` passed.
The six-policy **offline V03** review-priority experiment also ran
using previously reviewed private ground truth: reviewing eight
quality-first groups finds six of nine authentic groups, but only
two of three actual people. Reviewing eight catalogue-similarity-first
groups sees all three people but only five of nine genuine groups;
its correct top-three people count is zero at that budget. Both
arrival-order and catalogue-similarity priority need **32 reviewed
groups** to show all nine genuine groups (23 background groups
remain). This is evidence that *ordering can help*, not a safe
threshold or improved full-catalogue top-three ranking.

The real PR #799 read-only **all-eligible** evaluation completed for
three clips, 252 frames, 241 canonical/240 eligible people, 3,986
ranked detections, and 17 frames with overflows/57 discarded raw
candidates. V03 reproduced the existing 180-frame,
3,912-candidate, 64-group, 407-assigned, 3,505-unassigned
baseline **exactly**; new audit samples: 96 from all 12 temporal
strata, 3,409 not sampled. Original group counts, assignments
and exact group details were unchanged. This accepts the
**observability/invariance implementation**, not FACE-QUALITY-01
or GROUP-QUALITY-01: neither real-face validity nor person recall
in the sampled overflow has been reviewed.

The first specifically approved V03 adaptive coarse-only extraction
completed with **22 frames spanning a 166.68-second window**,
largest consecutive sample gap **7,928 ms**, no continuation
required. Source hash invariance, visual quality, actual cue
derivation and refined extraction remain unverified.

During the acceptance run, the previous Step 5C instruction to
supply a manually curated `references.json` was identified as a
regression to an **obsolete prototype reference source**, contrary
to the current existing-catalogue direction. The new isolated
`--detector-only` command processes *only* previously extracted
local frames with YuNet, emits the minimal private diagnostics
needed by the existing validated `--derive-adaptive-cues` adapter,
and requires **no reference stills, SFace, person identities,
PostgreSQL or original video access**. The separate **full
eligible PostgreSQL catalogue** remains the only intended
identity reference source for final video-face recognition.
Original media and retained still-photo embeddings are unchanged;
the command never automatically scans a collection or writes to
the production database.

Remaining acceptance: Windows-host rerun of new focused detector
tests; detector-only diagnostics → validated cues → refined
extraction on the already-approved V03 video, human inspection,
original-video hash check and eventually diverse approved videos.
See operations checkpoint 31. `SAMPLING-01` and all other
pre-merge quality gates remain open until human-reviewed proof.


## Experimental checkpoint 32 — first real V03 adaptive two-pass acceptance (2026-11-13)

PR #800 merged into the isolated `wi-0155` branch. The maintainer
reported **all automatic tests passing** and completed the previously
blocked reference-free detector/cue and refined-extraction workflow.

On 22 approved V03 coarse frames (166.68-second window), the YuNet-only
diagnostic run examined **22/22 frames with detector candidates**,
reported **486 raw / 473 retained / 13 discarded candidates**,
and took 12,110.831 ms. The cue adapter wrote **22 private cues**
successfully; no reference-image manifest or database identity
consultation was required for sampling. The subsequent opt-in
refinement extracted **180 frames**, including the original 22
coarse timestamps and **158 additional samples**; it reached the
bounded per-window frame budget, had **no additional window**, and
wrote **569,296,333 bytes** of private frame data (within the
configured 1,024 MiB limit).

The maintainer confirmed **zero missing coarse timestamps**,
source-video SHA-256 unchanged during refined extraction, coarse
still SHA-256 hashes unchanged, and visually acceptable frame
orientation, colour and quality. The three visible people mostly
remained in view; crucially, **one person became fully visible only
briefly when leaving the frame and that unobstructed-face interval
appears in refined samples only**. This supports a real,
human-observed *face visibility* benefit, but not increased
known-person top-three accuracy or a justified 158-frame workload.

**Acceptance distinctions:** `SAMPLING-01` two-pass *mechanics*
have passed a real approved Windows V03 test. Wider sampling
**effectiveness/efficiency remains open**, because every coarse frame
generated a cue, the refinement exhausted the 180-frame budget and
the detector still returns large numbers of background candidates.
`FACE-QUALITY-01`, `GROUP-QUALITY-01`,
`CATALOGUE-TOP3-01` and multi-video human acceptance remain
unfulfilled. No production integration or identity enrollment
should follow these observations alone. See operations checkpoint 32.


## Experimental checkpoint 33 — measure real faces missed by V03 group saturation (2026-11-13)

The V03 baseline still stands: 64 groups, 407 grouped observations,
3,505 unassigned observations, 55 of the 64 groups dismissed by a
human as background, and **only one** of three visually identifiable
known people retrieved with a correct top-three suggestion. The
separate 96-example PR #799 temporally stratified unassigned audit
was structurally verified, but we have not human-labelled its
crops. This is a critical unknown: candidate detector errors and
lost real-face observations are different failure modes.

PR #802 adds an opt-in **Windows/PowerShell 7.6, fully local
unassigned-candidate review** of the previous audit, requiring no
model inference, database connection, original video or new frame
extraction. It validates the original private multi-clip catalogue
report, cases, frame manifest, grouping accounting, 64-group
saturation, 12-stratum/8-sample capacity, maximum two samples per
frame and image geometry. The standalone gallery creates private
bounded crop and source-context image copies, simple manual
face/non-face/uncertain labels, optional private known-person
aliases for real faces, and strict CSV save/import. A separate
read-only script reconciles the saved CSV against the same
report, returning only anonymous counts (including real-face
samples among unassigned, distinct known people observed and
exploratory confidence/quality threshold retention counts).

**This is quality observability, not quality improvement yet.**
Samples favor higher crop-quality proxies in each temporal stratum,
so the human-labelled sample ratio cannot be extrapolated to the
3,409 not sampled; a real face in the sample is nevertheless
important proof of missed visible evidence. It provides evidence
for later comparison of candidate quality filtering and M25 DBSCAN
grouping, while avoiding premature automatic suppression or
unsafe merging. `FACE-QUALITY-01`, `GROUP-QUALITY-01`, top-three
accuracy and diverse-video acceptance remain open.

Post-merge local verification: run the synthetic Windows gallery
test and then use the **existing** PR #799 private report and
matching cases manifest to open and annotate the 96-sample V03
gallery. Import/save CSV, run aggregate measurement and report only
counts. See operations checkpoint 33. No archive write or
production integration is authorized by this PR.


## Experimental checkpoint 34 — corrected face audit and isolated M25 shadow grouping (2026-11-13)

The completed PR #802 V03 review initially reported 48 Real face,
46 Cannot tell and 2 Not a face out of the 96 quality-biased
unassigned candidates. The maintainer clarified that **all 46
Cannot tell entries represented real human faces whose person's
identity was not visually determinable**. With a separate
corrected face-validity CSV (the original identity-conservative
review preserved), the true classification is **94 real faces,
2 non-faces, 0 uncertain face-validity cases**. Of the 94 real
faces, only **82** were at or above detector confidence 0.70,
and **none** exceeded crop-quality proxy 0.50; 12 would be
excluded by a 0.70 detector cutoff and all 94 by a 0.50
quality cutoff. These are biased-sample counts, NOT population
precision estimates; the 3,409 other unassigned observations
remain unreviewed. No known/unknown identity aliases were
recorded, so no person-level inference can be drawn.

**Impact on requirements:** `FACE-QUALITY-01` cannot safely
be addressed by naive high-confidence/quality filtering.
`GROUP-QUALITY-01` needs a method that retains singletons
and does not silently discard real faces after hitting 64
streaming groups. The existing 64-group review found substantial
background burden; the separate unassigned audit is a quality
*biased* slice and cannot contradict its group-label results.

PR #803 introduces an **opt-in shadow-only use of the existing
M25 DBSCAN clustering engine** on the exact SFace vectors
already computed inside the all-eligible read-only video
catalogue experiment. It adapts M25 with a clearly experimental
video policy (cosine distance 0.18, min two members) and explicit
same-frame not-same constraints; unlike the still-photo
known-person advisory it does not treat adjacent frames as
independent identity votes. It keeps singleton and otherwise
ungrouped evidence as **explicit noise observations**, not
deleted or rejected faces, with up to 96 temporally stratified
private noise-crop examples for future human review. Any noise
not sampled remains counted and is not assumed to be background. The bounded shadow review stores
up to 64 groups' source-frame boxes plus full-eligible-catalogue
top-three pseudonymous suggestions, and reports all remaining
clusters and members as *unrepresented*, not lost. The original
streaming grouping, all V03 baseline assignment counts,
all other catalogue ranks and the private human-labelled audit
are left unchanged.

This is **algorithm-comparison infrastructure, not demonstrated
quality improvement**. The run cannot yet assert fewer false
groups, no wrong-person merges, known-person recall or improved
top-three identification. Real V01–V03 read-only comparison and
human adjudication are pending; further diverse-video cases
are required before any cutoff or grouping substitution.
See operations checkpoint 34. All `GROUP-QUALITY-01`,
`FACE-QUALITY-01`, `CATALOGUE-TOP3-01`,
`DIVERSE-VIDEOS-01` and `MERGE-01` acceptance gates
remain open; `SAMPLING-01` two-pass mechanics remain
verified only for V03.

## Experimental checkpoint 35 — source-free shadow review preparation (2026-11-14)

PR #803 was merged into wi-0155. Further local/private review is
deferred at the maintainer's request. The previously reported **three
clip aggregate** counted 267 clusters, 2,663 clustered observations,
1,323 noise observations and 195 clusters not represented among
bounded shadow review shortlists, with zero same-frame collisions.
These are NOT V03-only, and are not human-review quality results.
V03 streaming baseline remains 64 groups, 407 assigned and 3,505
unassigned observations.

The next isolated increment adds **D-prefixed private DBSCAN cluster
review, rank adjudication and aggregate measurements**, reusing the
existing PowerShell 7.6 gallery/worksheet with an explicit group
source selector. Existing G-prefixed provisional review remains the
default, with no altered detection, clustering or identity score.
The offline tools reuse an existing matching private PR #803 report,
case manifests, and people map; no fresh inference or SQL is needed.
Unshown clusters and noise remain accounted for and cannot be
interpreted as false or missing faces. Source-free synthetic tests
exercise a withheld 65th cluster, identity rank mismatch, baseline
isolation and failure on invalid accounting/co-presence.

The combined human verification procedure is in operations checkpoint
35. This is review infrastructure only, not proof of meeting
FACE-QUALITY-01, GROUP-QUALITY-01, CATALOGUE-TOP3-01 or
DIVERSE-VIDEOS-01. No automatic identity writeback or main-branch
integration is authorized.

## Experimental checkpoint 36 — verified shadow DBSCAN failure on V03 human quality (2026-11-14)

Following PR #803 merge, the maintainer reran the all-eligible,
read-only three-video catalogue experiment and performed private
source-manifest validation. **Structural acceptance PASS**: the old
V03 baseline remained 64 groups / 407 assigned / 3,505 unassigned;
V03 shadow DBSCAN accounted for all 3,912 detections with **259
clusters, 2,636 cluster members and 1,276 noise observations**,
**zero same-frame collisions**, 195 clusters / 483 members omitted
from the bounded 64-group shortlist, and 96 sampled noise crop
references. V01 and V02 also passed all accounting and original
baseline checks. The three-clip total is 267 shadow clusters,
2,663 cluster members, 1,323 noise observations.

**Human quality acceptance FAIL for this candidate policy and
largest-cluster review order:** the separately rendered V03 local
gallery showed **20/20 largest shadow groups (D01–D20) consisting
of background objects rather than faces**, while **24/24 displayed
noise samples visibly contained human faces**. This is a selected,
not random comparison: it does NOT establish that the remaining
239 shadow clusters are background nor that all 1,276 noise
observations are real faces. Person identifiability of the 24
noise samples was not established. Nevertheless, this exposes
unacceptable review prioritization and important genuine-face
loss from clusters at the chosen experimental cosine distance.

The shadow algorithm's passing accounting and co-presence checks
are distinct from face-detection, grouping and recognition quality.
**Do not replace the original video grouping with this DBSCAN
configuration, discard the counted noise, or infer identities
from its background-dominated groups.** Retain the comparator only
as isolated diagnostic evidence. The next development stage must
separate face validity, face/person identifiability, and temporal
identity grouping, with explicit human-reviewed ground truth.
`FACE-QUALITY-01`, `GROUP-QUALITY-01`, `CATALOGUE-TOP3-01`,
`DIVERSE-VIDEOS-01` and `MERGE-01` remain open; the already
verified V03 two-pass sampling mechanics remain intact.

## Experimental checkpoint 37 — independent detector agreement before grouping (2026-11-14)

The V03 private screenshots confirm that the large false
DBSCAN clusters D001–D020 repeatedly represent the **same
cracked-stone paving**, even when real people are visible
elsewhere in the original frame. A consistent SFace embedding
for a non-face crop is not evidence of a person; DBSCAN density
reinforces repeating background patterns. Conversely, all 24
reviewed DBSCAN noise samples were genuine faces. The missing
signal is *independent face validity*, not a less restrictive
embedding-similarity threshold.

PR #806 adds a strictly separate experimental **CenterFace
corroboration audit** using the already governed independent
CenterFace adapter and pinned model. It reads the exact existing
private full-catalogue PR #803 report and approved local frames,
selecting the same first 20 D-groups / 24 noise samples reviewed
by the maintainer. It measures spatial YuNet–CenterFace
agreement at two confidence levels without changing candidate
retention, SFace embeddings, DBSCAN membership, ranks, identity
assignments or originals. No PostgreSQL access and no fresh
video-frame extraction are required. The model must already
be installed and verified; weight/training-data rights remain
provisional in the existing manifest.

This produces measurable positive-versus-negative agreement
evidence, **not a trained or validated automatic rejection
rule**. CenterFace may fail on genuine small and obscured faces,
so absent corroboration is uncertainty rather than a definitive
non-face. A subsequent decision to change gating would require
false-negative/false-positive assessment on human-labelled
examples and varied videos, preserving real singleton faces
and catalogue top-three person coverage.

**Local model-integrity verification and the real V03 agreement
experiment are pending.** Structural confidence and group
counts from the existing DBSCAN run remain separate from
human quality acceptance. All real quality acceptance gates
remain open; isolated `wi-0155` remains the only target.

## Experimental checkpoint 38 — independent-detector V03 host acceptance PASS (2026-11-15)

The maintainer completed PR #806 local verification: focused tests,
CenterFace pinned model verification and standalone read-only
independent-detector execution **PASS**. The approved V03 evidence
involved **77 distinct frames, 100 representative crops** from
human-dismissed D001–D020 repeated-paving DBSCAN groups, and **24
human-confirmed real-face crops** from the first 24 selected DBSCAN
noise samples. At YuNet/CenterFace IoU >= 0.25, CenterFace agreed
with **0/100 paving crops** at either confidence 0.30 or 0.50;
for human-confirmed noise faces it agreed with **24/24 at 0.30**
and **23/24 at 0.50**. The private report was written; automatic
suppression remained disabled.

**This succeeds as a bounded diagnostic separation test**, not as
population face-detection accuracy or a proven grouping improvement.
These positive/negative observations were selected from a single
V03 scene and were repeatedly correlated with one dominant paving
texture; independent representative clips and all-candidate review
are still required. At the more conservative 0.50 cutoff the
candidate corroboration would already lose one known real face
within this selected cohort. Preserve every unsupported candidate
as reviewable evidence in any future experimental gate; do NOT
silently discard detector-negative real singleton faces.

**Next direction:** Add a separate, explicitly opt-in, source-safe
shadow agreement stage over all retained candidate boxes to compare
counterfactual filtering/review ordering before DBSCAN, preserving
baseline results and negative cases. Evaluate all-candidate face
recall, person representation, per-person top-three accuracy,
wrong-person merges, background review cost, short-exposure face
retention, multi-video generalization and model rights. No revised
production detector threshold or grouping adoption is authorized.
FACE-QUALITY-01, GROUP-QUALITY-01, CATALOGUE-TOP3-01,
DIVERSE-VIDEOS-01 and MERGE-01 still OPEN.

## Experimental checkpoint 39 — full-candidate CenterFace comparison without dropping evidence (2026-11-15)

PR #808 builds on PR #806's selected V03 independent-detector
positive-versus-negative result: CenterFace corroborated **0/100**
human-confirmed paving non-faces and **24/24** human-confirmed
real-face noise crops at confidence 0.30, but only **23/24**
real faces at 0.50. This is not representative of all V03
detections; no automatic face-rejection cutoff is justified.

An explicitly opt-in `--shadow-centerface` stage in the
read-only, all-eligible experiment performs **one independent
CenterFace pass per approved decoded frame** and records
per-candidate spatial agreement for **all retained YuNet
detections**, not only previous representative samples. It
reuses the same frame's SFace vectors in a **separate
corroborated-only M25 DBSCAN shadow**, while preserving all
original provisional G-group assignments, unfiltered DBSCAN
results, top-three catalogue evidence and source images.
Every unsupported observation remains explicitly counted;
up to 96 temporally stratified private review crops are
retained, and any additional exclusions remain visible as a
count. The independent shadow still enforces same-frame
cannot-link and cluster/noise member accounting.

The opt-in stage repeats one approved **SELECT-only catalogue
experiment** because embeddings are deliberately not persisted
in existing private reports. No video extraction, source changes,
identity writes, background jobs or application integration
are involved. Synthetic regressions cover co-present candidates,
false-background separation, retained genuine singleton
disagreements, baseline invariance and source integrity.

**Acceptance is not yet complete.** We still require actual
all-candidate V01–V03 results; independent human quality
review of alternate groups and non-matches; preservation of
short-exposure genuine faces; ranking accuracy and person-level
coverage; varied-video generalization; and review of the
CenterFace model's provisional rights. See operational
checkpoint 39. `FACE-QUALITY-01`, `GROUP-QUALITY-01`,
`CATALOGUE-TOP3-01`, `DIVERSE-VIDEOS-01` and `MERGE-01`
remain open. `SAMPLING-01`'s two-pass V03 mechanics remain
previously verified, but overall efficiency remains open.

## Experimental checkpoint 40 — full all-candidate independent agreement measured (2026-11-15)

The maintainer passed PR #808 focused tests (103/103), three governed
model hashes, V01–V03 all-eligible read-only run, and source/accounting
checks. 252 frames held 3,986 retained YuNet candidates. CenterFace
corroborated **337** at confidence >=0.30 (276 at >=0.50);
**3,649** nonmatches remain accounted for, not discarded.
The original V03 baseline of 3,912 candidates, 64 chronological
groups and 259 unfiltered DBSCAN clusters was **unchanged**.

V03 contained 288 independently corroborated observations, forming
18 corroborated-only DBSCAN clusters (54 cluster members) and leaving
234 corroborated candidates as DBSCAN noise. 3,624 YuNet V03
candidates were not corroborated, but this is **not a count of false
detections**. Original and new grouping had no same-frame collisions.
The measured three-clip elapsed time was about 302 seconds; no
throughput or generalization acceptance can be inferred yet.

PR #809 extends offline group and crop review to separate **C###
corroborated groups**, **N### corroborated-but-ungrouped noise**
and **U### noncorroborated candidate samples**. The two latter
sample sets are quality-biased, not population estimates. Private
CSV measurement now distinguishes sources. The review must answer
whether C-groups contain real faces and identifiable people,
whether N contains brief genuine faces, and whether U contains
significant false negatives. Until human review and additional
video evidence, no automatic rejection, threshold calibration,
DBSCAN adoption, identity writeback or integration is justified.

See operations checkpoint 40 for the Windows PowerShell 7.6
post-merge gallery and aggregate-verification procedure.
`FACE-QUALITY-01`, `GROUP-QUALITY-01`,
`CATALOGUE-TOP3-01`, `DIVERSE-VIDEOS-01` and
`MERGE-01` remain OPEN; `SAMPLING-01`'s V03
mechanics remain verified while efficiency is pending.

## Experimental checkpoint 41 — PR #809 reviewed; target product usefulness (2026-11-15)

**Local PR #809 technical and human-review verification: PASS.** The
PowerShell 7.6 synthetic test and C/N/U gallery generation passed, and
all four matching private measurement readers completed with validated
evidence and source-specific CSV checks. V03's CenterFace corroborated
subset yielded 18 C-groups (54 members), human-reviewed as 3 kept
plus 15 merges, zero dismissed/split/uncertain/unreviewed, giving
3 **proposed** final groups. The independent script's
`RetainedGroups=18` includes merge rows: it does not contradict
`ProposedFinalGroups=3`. There were 234 additional corroborated
DBSCAN noise observations; of 96 biased samples, 69 were labelled
real faces belonging to three distinct known people, 27 uncertain,
zero background. Of 96 biased uncorroborated U samples from 3,624
candidates, 15 were real faces belonging to two distinct known
people, 67 background and 14 uncertain. More clusters eliminated
does **not** equate to that many saved production review decisions;
all real-face noise/unconfirmed detections need a safe path.

**Identity suggestion results are deliberately not accepted or
rejected from this review.** The operator did not inspect top-three
suggestions; the measured 0/3 on the three kept C-groups is valid
for those labels but is not an appropriate assessment of full
catalogue suggestion value on V03. Catalogue 241 canonical /
240 eligible / 596 exemplars and read-only evidence accounting
were validated. Reassess suggestions only on visually suitable
V01/V02 and, if needed, a few better-quality approved clips.

**Next smallest evidence step:** reuse the already generated PR #808
all-eligible report and private case/people map to review V01/V02
CenterFace C-groups (2 and 6), corroborated noise (7 and 15
observations) and uncorroborated samples (19 and 6). Do not
re-run inference just to produce another diagnostic. Judge *total*
human burden, recognizable-person discovery, wrong merges and
suggestion utility on suitable crops. If there is clear progress,
choose one contained implementation improvement, with one
focused comparison; otherwise stop the exploratory ablations
and define the smallest safe production-oriented pilot and
remaining limitations, subject to maintainer approval. No
automatic suppression, canonical writes, source hydration,
still-image exemplar enrichment or main-branch promotion.

## Experimental checkpoint 42 — V01/V02 human review complete; exploratory stop/go (2026-11-15)

**Local PR #810 follow-up verification: PASS for the supplied aggregate
readers.** The operator ran matched full-catalogue report and reviewed
C/N/U CSV measurements for both V01 and V02. All group, rank and
sample readers returned validated source evidence. Previously recorded
PR #809 PowerShell synthetic tests and gallery rendering remain
accepted; no new inference, extraction or database access was needed
for this review.

| Reviewed measurement | V01 | V02 |
| --- | ---: | ---: |
| CenterFace-corroborated retained observations | 17 / 36 YuNet candidates | 32 / 38 |
| Initial C-groups | 2 | 6 |
| Kept / manually merged / dismissed C-groups | 2 / 0 / 0 | 2 / 4 / 0 |
| Proposed final groups after review | 2 | 2 |
| C-group observations / corroborated DBSCAN noise | 10 / 7 | 17 / 15 |
| Distinct known people labelled in grouped review | 2 | 2 |
| Top-three correct initial groups | 1 / 2 | 2 / 6 |
| Distinct known people with at least one correct top-three group | 1 / 2 | 1 / 2 |
| N corroborated noise: real / background / cannot tell | 7 / 0 / 0 (all 7) | 12 / 0 / 3 (all 15) |
| U noncorroborated: real / background / cannot tell | 7 / 9 / 3 (all 19) | 1 / 3 / 2 (all 6) |

The full-catalogue read-only ranking comparison used 241 canonical
people, 240 eligible and 596 still-image exemplars. V01 found two
human-kept groups for two labelled people with no merge burden;
V02 needed four **human** merges to produce two proposed final
groups for two labelled people. These are not automatically
consolidated production groups. No reviewed group was dismissed as
background. The group-level 1/2 and 2/6 top-three outcomes are
genuinely useful partial suggestions, not calibrated success rates
or mandatory per-person quotas; ranks refer to initial C-groups
before human merge, and reviewing the two proposed final V02
groups would need a separate ranking algorithm.

Unlike V03's quality-biased 96-of-many audits, both V01 and V02
N and U review sets covered **every** observation in those
particular report subsets. Of 22 corroborated-but-ungrouped
observations across both clips, 19 were labelled real faces and
3 uncertain; none was labelled background. Of 25 YuNet
observations without CenterFace corroboration, 8 were labelled
real faces, 12 background and 5 uncertain. It is therefore
unsafe to suppress all CenterFace nonmatches or to omit DBSCAN
noise from the person's discovery path. These counts are
per-detection observations, not distinct people or unconditional
video-level recall.

**Stop/go decision from current evidence:** Enough practical
progress has been demonstrated in the *group-first* idea to
stop repeated V03-only detector/DBSCAN cutoff sweeps and
unfocused evaluation rounds. Next develop/design **one bounded
review-facing prototype improvement**: start with a compact
CenterFace-prioritized C-group shortlist; make ungrouped genuine
faces discoverable in a separate, capped/expandable review lane,
with representative crops and context, without flooding the
reviewer or silently dropping potential people. Prefer
reusing existing reports for a targeted before/after comparison;
only request another approved clip or inference pass if a
concrete missing capability cannot be judged from V01/V02/V03.
Accept human correction, manual fallback, and occasional missed
top-three suggestions; do not pursue perfection or automatic
person assignment in this stage.

**Open risks before any production integration:** one-group-per-
person automation (V02 still needs four manual merges), meaningful
singleton/noise triage and coverage, background burden on
other scenes, runtime and adaptive-sampling efficiency,
CenterFace model-weight/training rights, and explicit choice
of integration boundary. Original media and production
catalogue/embeddings remain untouched. No PR should target
`main` without separate maintainer approval; the smallest
production slice remains a later explicit decision.

## Experimental checkpoint 43 — compact group-first private workspace (implementation; local acceptance pending)

A new **opt-in, read-only** `New-VideoFaceReviewWorkspace.ps1`
reuses the previously tested CenterFace C-group, N-noise and
U-uncorroborated offline gallery builders. The default browser entry
is a C-group review editor with existing top-three suggestions,
manual keep/merge/dismiss/split decisions and source-specific CSV
save/import. Below it are two **collapsed evidence lanes**, each
showing up to eight temporally distributed thumbnails first, with
the rest of the maximum 96 sampled audit crops accessible in a
second expandable section. Every thumbnail links to its exact
N/U audit-card anchor for classification and separate source CSV
save/import. The workspace shows the full source counts and how
many observations were not sampled, without calling them background
or inventing identities.

This is a substantial **review-workflow improvement**, **not**
an automatic grouping improvement: it does not merge V02's four
remaining fragments, choose identities, suppress YuNet candidates,
reduce the actual count of observations or establish production
quality. It deliberately avoids presenting every noise crop as
a new face group. CenterFace nonmatches and DBSCAN noise remain
accessible, and the viewer can fall back to manual identification.
Both existing C and N/U CSV formats remain unchanged. Windows
7.6 synthetic coverage checks C/N/U source isolation, matching
report SHA in audit worksheets, count reconciliation, deep links,
bounded preview display and unmodified source frames. Existing
validators own source manifest, frame geometry and report checks;
no extra inference, PostgreSQL reads/writes or new model is used.

**Local acceptance remains pending.** Reuse V01/V02 approved
private reports, cases and source frames to verify: compact
C-groups first, N/U initially collapsed, first eight audit
thumbnails spread across the samples, additional sample
expansion, open-to-label anchor navigation, all three independent
CSV save/import paths, and no source-frame changes. Review the
human effort qualitatively against the accepted checkpoint 42
baseline (V01: two groups/zero merges; V02: six groups/four
manual merges), not as a new model accuracy test. This workspace
currently requires at least one C-group and at least one
sampled candidate in **each** N/U source because it composes
the existing nonempty gallery builders; no-face/empty-source
production handling is deferred. Sampling up to 96 leaves
additional unrepresented observations explicitly counted,
not made individually navigable. Do not claim complete person
coverage from the sampled view.

`FACE-QUALITY-01`, `GROUP-QUALITY-01`,
`DIVERSE-VIDEOS-01`, `CATALOGUE-TOP3-01` and
`MERGE-01` remain not fully accepted. Especially before
integration: more automatic within-person consolidation,
a bounded but comprehensive path for genuine singleton/noise
faces, representative corpus, Windows runtime,
CenterFace licensing/training rights, and explicit choice of
video-component versus in-app boundary still require decisions.
No changes may target `main` without maintainer sign-off.

## Experimental checkpoint 44 — single-save C/N/U review repair (2026-11-15)

The maintainer merged PR #812 and passed its Windows synthetic tests,
but **did not accept the browser workflow as more convenient**.
Although C/N/U evidence appeared together, C decisions used one
explicit save picker and the separate N/U audit pages each downloaded
a default-named CSV; review still required three saves, renames and
manual moves. This is a meaningful usability failure, despite
correct underlying report/CSV validation.

The follow-up adds one combined **C + N + U worksheet** in the
existing private group-first page. N/U thumbnails now have their
own face classification and optional private alias controls
directly beneath each crop; linking to the earlier source gallery
still allows enlarged/context review. A single `Save all C/N/U`
action uses the browser file picker when supported (otherwise one
uniquely named fallback download per save). `Import combined CSV`
restores all three sets of decisions with all-or-nothing
clip/source/report-hash/crop-identity validation. Previous
individual galleries and source-specific CSV readers remain
available, but are not required for the combined workflow.

The new `Measure-VideoFaceReviewWorkspace.ps1` takes the
**one combined CSV** and the existing matching private report,
people map and clip alias. It checks source identifiers, clip and
SHA-256, splits the worksheet into temporary private C/N/U CSVs
and delegates to the existing strict group/rank/noise/disagreement
validators, then deletes the temporary files. Only aggregate
metrics are returned. No new model inference, database access,
automatic identification/merging or still-exemplar enrichment.

Synthetic regression covers combined source-bound aggregation,
correct top-three preservation, N/U classifications, rejection
of wrong report hashes, cross-source rows and duplicated samples;
the existing Windows suite also checks source-frame immutability.
**Local acceptance pending** for one save picker, one combined
CSV re-import, labels and comments retained across all three
sections, and matching aggregate output. Previously saved separate
V01 C/N/U CSVs remain valid under the old measurement scripts;
this change does not silently overwrite or migrate them.

The earlier V01/V02 recognition and grouping conclusions remain
unchanged. This addresses a **workflow defect**, not unresolved
automatic person-group consolidation, unrepresented evidence
outside the capped 96 samples, no-face inputs, CenterFace model
rights or future integration scope. Keep all changes isolated
from `main` pending explicit maintainer approval.

## Experimental checkpoint 45 — PR #813 single-save human acceptance (2026-11-15)

**Maintainer local acceptance: PASS.** PR #813 was merged into the
isolated `wi-0155` integration branch after its final Windows CI
checks passed. The operator verified the combined V01 private
review workspace in the local browser:

- Both C-groups rendered representative crops, existing catalogue
  suggestions and human decision controls: **PASS**.
- Corroborated DBSCAN-noise N lane expanded and allowed in-page
  human face classification: **PASS**.
- YuNet/CenterFace disagreement U lane expanded and allowed
  in-page human face classification: **PASS**.
- A single **Save all C/N/U** action produced one combined CSV:
  **PASS**. The distinct filename required no manual rename,
  download consolidation or file relocation.
- After page reload, **Import combined CSV** restored all
  three sets of decisions: **PASS**.

The read-only `Measure-VideoFaceReviewWorkspace.ps1`
returned `SchemaVersion=1`, `ClipAlias=V01` and
`CombinedEvidenceValidated=True` for the saved worksheet.
This confirms the combined worksheet matched its report and
source-specific validators. The operator reported that the
corrected workflow looked good. The prior PR #812
three-save browser usability rejection is therefore
**resolved** for the accepted V01 case; older separate
C/N/U CSVs remain usable.

This acceptance verifies *review usability and evidence
integrity*, not a new face-detection accuracy, model
ranking or automatic grouping result. Do not repeat
V01/V02 human labelling or full inference merely to
recheck the one-save behavior.

**Next product-quality gap:** V02 still had six
algorithmic C-groups representing two manually labelled
people, requiring four manual merges. The desired outcome
is few groups per person without wrong cross-person merges,
plus safe discoverability of genuine N/U faces and
singletons. Consider one bounded, *report-only* assisted
consolidation/review prototype against existing private
evidence; gate any additional experiment on a meaningful
reduction in operator effort without person-coverage loss.
No all-N/U suppression, persistent identity writes,
catalogue exemplar enrichment or `main` integration
is authorized. No-face/empty-source handling, capped
N/U sampling coverage, licensing/packaging and production
integration boundary remain open.

## Experimental checkpoint 46 — human-confirmed C-group merge assistance (implementation; local review pending)

The merged PR #813 one-save C/N/U workflow and its validated
CSV remain the accepted foundation. This development increment adds
**optional reviewed-group merge assistance** to the private
CenterFace C-group workspace, without recomputing faces, SFace
embeddings, DBSCAN memberships or existing catalogue rankings.

- A collapsed **possible same-person fragments** pane shows up to
  twelve candidate group pairs with side-by-side existing
  representative crops. A pair is a *review lead* if the two
  groups share a catalogue top-three alias, or if the operator
  has explicitly selected the same known-person alias.
  Rank overlap is not calibrated, and it is NOT an identity or
  automatic merge. The reviewer must separately tick an
  explicit same-person/simultaneous-face confirmation and
  click the corresponding merge action. Other groups remain
  available through their original manual controls.
- A separate **reviewer-labelled batch consolidation** proposes
  a target for each repeat of an operator-entered known-person
  alias (never UNKNOWN), preferring an already kept group,
  then a larger group. It applies eligible merges only
  after one explicit bulk confirmation and never changes
  a SelectedPerson value. This reduces *mechanical*
  review actions after identities are established; it does
  not change automatic clustering quality.
- Both paths refuse groups already marked Merge, Dismiss,
  Split or Cannot tell, refuse distinct nonempty selected
  person aliases, and refuse pairs with **overlapping
  representative frame numbers**. This is a necessary
  review veto, not a proof that groups never overlapped in
  frames absent from the representative crop subset.
  The reviewer must still check that they depict the same
  person and never merge simultaneously visible distinct faces.
- Only existing `Decision` / `MergeInto` fields are written
  in the private **combined** C/N/U worksheet when the user
  explicitly saves; N/U decisions, source counts, model
  evidence and the validated CSV schema are unchanged.
  No persistent group or person writes, inferred identities,
  video extraction, additional model inference or
  catalogue reference enrollment occur.

Windows PowerShell gallery tests now check the C-group
rank/frame attributes, merge-pane HTML and copied script.
A dependency-free Node synthetic test exercises explicit
confirmations, rank-only no-auto-merge, distinct-person
conflicts, representative-frame veto, chosen target,
known-alias batch and invalidation after edits; the Windows
CI workflow invokes it. Real Windows browser acceptance,
and whether V02's four manual merges are practically less
burdensome, remain **pending**. Previous V01/V02 group
quality baselines remain unchanged and are not replayed.
No claim is made that this automatically consolidates six
V02 C-groups into two; actual clustering improvement,
full same-frame membership proof, N/U singleton coverage
beyond the 96-sample cap, no-face handling, model licensing
and `main` integration remain open.

## Experimental checkpoint 47 — PR #815 accepted, reviewed video-person presence proposals (2026-11-15)

The maintainer merged PR #815, then reported **PASS on all 12**
V02 browser checks: preview pairs, explicit consent before
merging, correct keeper/target decisions, refusal of
distinct-person proposals, no N/U or person-label changes,
and a working already-labelled same-person batch.
The operator rated review work **somewhat easier** and
explicitly said the improvement is sufficient. This is
**human-review ergonomics accepted**, not a claim that
DBSCAN automatically produced fewer fragments. Do not
continue refining merging UI without a new meaningful
product need or rerun the full V02 review just to confirm.

The next opt-in **report-only video/person presence proposal**
consumes one source-validated V01/V02/V03 combined C/N/U CSV,
the exact matching full-catalogue private report and its
private still-photo person-alias map. The new
`Measure-VideoFaceReviewedPresence.ps1` first invokes the
existing strict four-source validators, then derives at most
one candidate **video-to-known-person association** per
reviewer-confirmed private P-alias. It accounts separately
for retained/merged C-group roots and distinct N/U audit
sample evidence. It does NOT infer identities from rank
suggestions, nor deduplicate unknown faces or assume that
unlabelled/cannot-tell detections are background.

The pure `VideoFacePresenceResolution.psm1` requires
all merge chains to end at an explicitly kept group,
refuses contradictory person aliases within one merge,
prevents labels on dismissed/unresolved groups from
creating a presence association, and vetoes C-group
merges whose sampled representative frames overlap.
That check is necessary but not sufficient to prove
non-overlap of all unsampled source members; the full
frame-to-person safety boundary remains open. A video
cannot be declared free of a person on these samples.

Default output is **aggregate counts only**. Optional
`-PrivateDraftPath` emits a create-new JSON under
`%LOCALAPPDATA%\PhotoIdentity\WI0155\reviewed-presence`
with only reviewer pseudonym aliases and bounded support
counts; it contains no names, canonical IDs, frames,
timestamps, embeddings or source paths. The input
file hashes provide provenance. An existing draft cannot
be overwritten and no database or permanent identity
association is written.

Pure Windows synthetic tests cover deduplication,
N/U-only known evidence, UNKNOWN isolation, cycles,
cross-person merge conflicts, selected-label misuse,
simultaneous sampled-frame veto, and malformed audit
labels. The existing synthetic full-report test checks
the end-to-end V03 source-bound wrapper, zero original
writes and create-new draft safety. The existing
one-file C/N/U workflow is unchanged. Windows CI and
a **single local V02 presence summary** remain pending.
This is an isolated product-evidence handoff toward
the required video/person association, not production
persistence or permission to merge to `main`.


## Experimental checkpoint 48 — complete-member same-frame merge safety (synthetic-only gate; 2026-11-17)

**Maintainer's V01 acceptance after PR #816:** The matching legacy
combined V01 review and full-catalogue report validated. Re-review
finished with **two known-person proposals from two kept C-groups,
zero merges, zero unlabelled kept groups, zero unreviewed N/U samples,
14 human-labelled real-face N/U observations (7 N and 7 U), three
cannot-tell samples, zero unsampled N/U observations and zero
production writes**. This is *review completeness for available
V01 observations*, NOT complete person detection recall. The
existing PR #816 provenance / human-person proposal functionality
is accepted for that V01 evidence; no further V01 local review is
needed for this slice.

**New experimental full-member proof:** The shadow DBSCAN result
already enforces not-same-frame inside each computed cluster, but
until now the private report serialized only its at-most-five
representative crops. Newly produced PRIVATE source reports now
also retain *every* source member's sorted frame ordinal per
reviewable D-group (and derived C-group). The complete list length
must equal the reported group observation count and must include
all shown representative frames; within-group frame repetitions
are invalid. This bounded local-only evidence contains **no face
IDs, model vectors, source paths, names or canonical identity IDs**.
Noise and clusters outside the 64-group review shortlist remain
accounted for separately; this change does not claim coverage of
their full membership or classify them.

The C-group browser assistant can veto a merge if *any complete
member frame* overlaps between the source groups, including hidden
members absent from representative crops. Invalid complete
membership attributes reject suggestions. Legacy reports retain
their representative-only behavior, explicitly warned as not
sufficient to certify full merge safety.

More importantly, the independent **read-only presence resolver**
accepts a reviewed merge only if ALL fragments (including its
keeper) have valid full source membership and every distinct
member frame is nonoverlapping across the entire merged component.
It rejects missing, partial, unordered, duplicated, out-of-bound
or representative-inconsistent membership; self/cyclic/conflicting
identity merges remain rejected. A legacy report with **zero
merges** still passes unchanged; a legacy saved report with one
or more manual merges **cannot be retroactively certified** by
up to five representative crops and is deliberately refused
for a fully certified merged presence proposal. The browser
can still import the older CSV for private human review; do
not misrepresent it as complete-member certification.

Aggregate-only output now includes
\`FullFrameMemberGroupsAvailable\`,
\`FullMemberMergeSafetyValidated\` and
\`FullMemberMergeSafetyStatus\` (\`no-merges-to-verify\` or
\`verified-complete-membership\`). The opt-in create-new
pseudonym-only private draft carries the status but **no
frame lists**. Existing report schema and human CSV format
remain backward-compatible; no production PostgreSQL,
source, video extraction, identity/reference or automatic
merge writes occur. This proof only establishes **no distinct
source frame repeated across the merged groups** for the
reviewable captured membership; it does not prove same-person
identity, rule out duplicate detections or correct false
detections, or prove all faces were found.

Automated CI tests cover the actual C# emitted full membership
(including members not chosen for representative crops),
PowerShell strict full-count/non-overlap validation, hidden
same-frame collisions, legacy no-merge acceptance and merge
refusal, malformed evidence, and JavaScript human-confirmed
UI merge-veto logic. **No operator local verification of this
synthetic safety change is required now.** Existing V01/V02
private reports cannot acquire these new lists without a
new explicitly approved evaluation run; do not rerun the
videos just to verify this PR. Any later genuine real-video
proof belongs to one consolidated near-merge review.

### Remaining sequence before choosing whether to target main

1. **Person-coverage / false-face burden (FACE-QUALITY-01,
   GROUP-QUALITY-01).** Reuse accepted V01/V02 human review and
   V03 distant-face stress evidence. Implement only the remaining
   bounded, measurable grouping/face-prioritization changes that
   reduce false groups/merges without losing real singletons,
   N/U faces or identifiable people. Confirm no silent 64-group
   cap losses or false-negative shortcut. This is NOT a request
   for more threshold sweeps or further merge-UI polish.
2. **Adaptive sampling / empty and long clips (SAMPLING-01).**
   Reconcile current opt-in adaptive timestamp/cue prototype
   with duration/content-driven extraction, whole-video
   coverage, safe continuation and honest incomplete-coverage
   accounting. Add synthetic short/no-face and long/scene-change
   regression tests. No OneDrive hydration or batch archive
   processing.
3. **Existing full-catalogue evidence and small diverse
   acceptance (CATALOGUE-TOP3-01, DIVERSE-VIDEOS-01).**
   Preserve current 240 eligible-person read-only SFace
   baseline. Use V01/V02 for recognizably identifiable
   top-three usefulness, V03 for grouping stress, and only
   a few *explicitly approved already-local* additional
   examples if a concrete coverage gap remains (brief
   faces, simultaneous people, unknown/no-face). Measure
   person coverage, rank utility, false groups, manual
   work, N/U survival and uncertainty together. No fixed
   quota, no mandatory correct top-three for every face.
4. **Boundary readiness (MERGE-01).** Resolve independent
   CenterFace model rights/distribution, environment and
   security/privacy retention/cleanup, main-branch code
   reconciliation, CPU/time/storage bounds, original hash
   safety, zero unwanted DB writes and OneDrive hydration,
   CI and aggregate review evidence. Explicitly choose
   integrate-vs-separate with maintainer and write rollout
   and rollback decision. Only then plan the scope of a
   PR targeting \`main\`. Production video/person database
   persistence and normal three-tier production review
   remain *post-merge implementation*, not requirements
   for today's prototype PR.


## Experimental checkpoint 49 — independent-only face candidates and quality-diverse singleton review (2026-11-17)

**Problem observed in the code path:** CenterFace had only been used
to test overlap *against previously retained YuNet candidates*. Thus
even a valid CenterFace-only face was invisible in the C/N/U
review evidence. Also, the high-quality-only 96-sample temporal
audits could preferentially show clear crops while missing
tiny, blurred, briefly visible or low-confidence **genuine
singleton faces**. None of the existing human review proves
a safe hard rejection threshold.

**New source-safe, strictly experimental behaviour:**

- For each approved, already-local frame, the CenterFace shadow
  now counts all confidence >=0.30 independent detector boxes,
  and separately counts those with no retained YuNet candidate
  overlapping at IoU >=0.25. These are **I candidates**,
  not additional YuNet detections, matched SFace identities,
  true faces, track segments, DBSCAN group members or person
  presence proposals. CenterFace-only false positives and
  possible duplicate boxes remain for human judgement.
- Up to 96 *I* candidate crops (8 per 12 temporal strata,
  at most 2 from one frame) can be exported from the opt-in
  private report to the existing offline gallery using
  `-AuditSource CenterFaceOnly`, with source-bound I001...
  rows and the same human Real face / Not a face / Cannot tell
  worksheet. The independent-only aggregate has its own
  counts and unrepresented observations; missing legacy
  independent-only fields fail closed rather than appearing
  as zero review work. A single frame is limited to 32
  independent boxes; excess fails closed, never truncates.
- Future shadow N (corroborated DBSCAN singleton noise),
  U (YuNet not corroborated) and I (CenterFace-only)
  samples use `quality-extremes-v1`: each temporal stratum
  retains the strongest and weakest candidate crop-quality
  proxies and offers up to four of each within the old
  eight-sample limit. Other provisional G-overflow audits
  retain their earlier highest-quality sampling policy.
  The sampling policy is explicit in new private reports,
  with legacy reports remaining readable. An unmatched
  candidate is NEVER suppressed if it is not sampled.
- The private report/aggregate console adds independent-only
  detections and bounded sample totals, without writing
  any face vector, canonical person ID, file path or sampled
  image to stdout. Existing YuNet C/N/U counts, clustering,
  rank suggestions, reviewed merge constraints and the
  two-person accepted V01 presence summary are unchanged.
  **I worksheets do not feed the current reviewed-presence
  resolver**; doing so without a human-and-source agreement
  design would overstate independent-only face evidence.

These changes improve **visibility and audit coverage**, not
verified detector recall, face probability, grouping purity
or actual person coverage. No automatic background suppression,
CenterFace-only SFace embedding, catalogue matching, data
retention policy change, production database write, OneDrive
hydration or video extraction has been authorized. Reports
created before this checkpoint contain no I evidence and
must not be reinterpreted as if they did.

**Synthetic verification in Windows CI:** C# tests exercise
normal and independent-only frame accounting, non-loss of
N/U noise, deterministic high-and-low proxy samples, per-frame
limits and legacy policy behaviour. PowerShell gallery
tests cover I aliases, source hash, measured counts and
malformed I-accounting rejection. No V01/V02/V03 inference
rerun is requested to validate this implementation.

**What remains before planning a `main` integration:**

1. **Face/group quality (partially advanced here):** establish
   operationally useful, low-burden C groups without silent
   loss of genuine N/U/I singletons. Existing human C/N/U
   evidence is encouraging for V01/V02 but needs consistent
   group purity, split burden and any I-only missed-face
   contribution assessed; no unjustified grouping threshold
   or automatic suppression rule.
2. **Adaptive coverage:** implement and bound actual
   duration/content-driven extraction/continuation with
   honest missed-window accounting and synthetic empty,
   brief-face, scene-change and long-video tests. The
   earlier 22-coarse-to-180-refined V03 run verified
   mechanics, not recall, efficiency or all-duration policy.
3. **Small consolidated acceptance:** combine existing full
   240-eligible-person, V01/V02 usefulness and V03 stress
   evidence; only run a few additional expressly approved
   already-local videos if there is a material remaining
   scenario gap. Measure review work, person coverage,
   true singletons, N/U/I survival, uncertainty and rank
   utility together without fixed top-three quotas.
4. **Merge readiness boundary:** decide CenterFace model
   rights/distribution, privacy/retention, runtime budgets,
   build/CI health, source hashes and absence of writes/
   unwanted hydration; reconcile divergence with main,
   decide integrate-versus-separate and plan rollout/
   rollback. Only **then** scope a PR against `main`.
   Production person-presence persistence and three-tier
   integrated review are deferred follow-on work.


## Experimental checkpoint 50 — visual-change cues for adaptive coverage (2026-11-17)

The source-free adaptive planner already covers each complete <=10-minute
window with <=8-second coarse timestamp gaps and requires explicit
continuation to cover clips longer than ten minutes (up to one hour).
However, prior optional refinement was driven only by YuNet detections:
when the detector found no face, a scene cut or changed background
was not eligible for additional nearby samples. A missed short
appearance could therefore have had no detector-derived cue.

**Opt-in private experimental change:** `--derive-adaptive-cues
--include-visual-cues` now reads the *same preflighted, completed,
already-local coarse frame workspace* after verifying its manifest,
ownership metadata and per-frame detector report. Images are decoded
to at most 64x64 pixels in memory and reduced to an 8x8 luminance
grid. Significant change across >=4 cells produces a `Motion` cue;
across >=24 cells produces a `SceneChange` cue. Both boundary frames
are candidates for bounded refinement. Cues are deduplicated against
detector-based entrances/exits, small/uncertain detections and
candidate overflow, retaining the higher-priority cue per planned
timestamp. They can only refine an existing coarse plan and never
drop a coarse frame, stop at the first identified face, infer a person,
or write source/video/identity/embedding/database state. The normal
`--derive-adaptive-cues` behavior remains backward compatible.

These 8x8 thresholds are **not calibrated face quality, optical flow,
scene segmentation, recall or detection confidence**. Camera
movement, lighting/exposure changes and edits may also produce
cues; sub-8-second genuine faces can still be missed entirely.
An 180-frame window cap and explicit
`refinementBudgetReached` indicator apply unchanged. Video windows
after the first still require explicit continuation; this is not yet
a whole-clip orchestrator or proof of full person coverage.

Synthetic C# regressions cover a no-detection scene change, localized
motion, stable no-face footage, unchanged/coarse frame preservation,
deduplicated detector + visual cues, incomplete/tampered plans, a
long clip's last window, source-only 64x64 decoding and bounded
review workload. Existing coarse-only reports remain readable,
and the visual option is off by default. No V01/V02/V03 local
video rerun is required for this checkpoint; the optional visual
workflow belongs to the consolidated review after remaining
development. Main remains untouched.

**Next before planning `main`:** finalize explicit whole-clip
window completion accounting and resource limits; use the existing
human V01/V02/V03 C/N/U reviews and any justified I evidence for
face/group burden and genuine singleton preservation; then perform
one consolidated acceptance on only approved already-local clips,
followed by rights/privacy/runtime and branch reconciliation, and
an explicit integrate-versus-separate decision.


## Experimental checkpoint 51 — whole-clip adaptive coverage receipt (2026-11-17)

**Gap:** Earlier sampling plans safely returned `hasMoreWindows` and
`nextWindowStartSeconds` but had no source-bound *whole-clip* accounting.
Someone could report a completed first/last ten-minute window while silently
missing the middle ten minutes, double-count a window, or accidentally
mix windows from different videos. This invalidates any assertion that
whole-video coverage is complete, regardless of the individual frames'
face-recognition quality.

**New experimental vertical slice:**

- Each NEW approved `-AdaptiveSampling` extraction records
  `sourceDescriptorSha256` in private `extraction-metadata.json`.
  The input is a stable local-path + original-file size/mtime +
  FFprobe duration/dimensions/codec/pixel-format/rotation descriptor,
  SHA-256 hashed without a second opening of the source video. The
  source size, time and availability are checked again at the end
  of extraction. The hash **is not a SHA-256 of the original bytes**:
  same-size/time replacements remain possible and no source-content
  authenticity claim follows. The path and digest are never printed.
  Old extracted manifests intentionally lack this field and cannot
  be certified for cross-window coherence by the new auditor.
- `--audit-adaptive-coverage` accepts 1–6 explicit private,
  completed `frames.json` manifests for **one clip**, in any
  order. It verifies each ownership marker/run ID, adaptive metadata,
  declared source descriptor, duration, canonical ten-minute
  window positions, required whole-window coarse samples, frame
  order/count and naming/encoding, fully-local frame-file existence,
  per-frame 8-MiB bound, extracted byte total and workspace budget.
  Duplicate run IDs/windows, mismatched sources, incomplete/modified
  inputs, malformed metadata and missing coarse frames **fail closed**.
- A valid yet incomplete selection yields a **successful, explicitly
  incomplete audit**, with missing windows, earliest missing start,
  covered-window duration and maximum requested-frame gap. Only a
  complete collection of every canonical window may report
  `wholeClipCoverageComplete=true`. An incomplete video must never
  be mistaken for a no-face video or used as evidence that a person
  was absent. The tool never starts a missing window automatically.
- A create-new, private, no-path coverage JSON receipt includes
  the window positions and source descriptor. Stdout contains only
  aggregate window/frame counts, bytes, maximum gap, saturation,
  `sourceContentHashVerified=false`, and completion status; no
  original/video/frame paths, identity labels, scores or pixels.
  No PostgreSQL, SFace, YuNet, OneDrive hydration, video open,
  pixel decoding, crop write, cleanup or identity writes occur.
- Synthetic no-media C# tests cover short/boundary and full-hour
  clips, multi-window partial and complete receipts, arbitrary
  submission order, the earliest missing window, false completion
  prevention, duplicate/mixed sources and run IDs, missing coarse
  timestamps, forged cap flags, tampered manifests, frame files,
  metadata and incomplete ownership markers. The CI suite verifies
  this without asking for additional V01/V02/V03 inference.

**What this proves and does not prove:** This is structural
timeline-plan/extracted-file accounting under the documented
<=8-second coarse-gap policy, not proof that the original bytes
are unchanged, that FFmpeg selected the intended pixels, or that
every human-identifiable face has been detected. Visual or
detector-selected refinements remain content-adaptive *opportunities*
and can saturate the 180-frame cap. New receipts cannot
retroactively certify old V01/V02/V03 manifests.

**Remaining before `main`:**

1. **FACE/GROUP-QUALITY:** compare false-group burden, purity,
   over-splitting and genuine singletons across C/N/U/I; do not
   require corroboration to keep a real face or shortcut at the
   group cap. Reuse the completed V01/V02/V03 human evidence.
2. **SAMPLING/COVERAGE acceptance:** account for actual whole-
   video clips, bounded extraction/CPU/storage, no-face and
   short-appearance examples; choose a tiny number of explicitly
   approved, already-local new clips *only if* existing data has
   a material gap. Require complete windows before interpreting
   no-face/absent-person outcomes.
3. **MERGE readiness:** reconcile production/main separation,
   CenterFace model licensing and redistribution, original-content
   SHA safety if required for deployment, privacy retention,
   zero unapproved writes/hydration, test/CI/rollback. Explicit
   maintainer decision integrate versus separate, THEN plan PR
   to `main`. The three-tier integrated presence review and
   production DB association remain post-merge implementation.

## Experimental checkpoint 52 — two approved real-video coverage runs and V01 review acceptance (2026-11-18)

**Maintainer local evidence (runs on 2026-11-17; no private paths or
identities retained in Git):** The merged PR #820 whole-clip audit and
PR #819 visual/refinement machinery were exercised on two different,
explicitly approved and fully local videos. Both source originals had
matching SHA-256 before and after the run, and both structural audits
reported `wholeClipCoverageComplete=true`, one expected/verified
window, zero missing windows, and zero windows at the 180-frame cap.
The coverage receipts set `sourceBoundByDescriptor=true` and
`sourceContentHashVerified=false` (source descriptor is not a
content hash; the maintainer's independent before/after SHA-256
comparison is separate evidence).

| Operator evidence | Video A | Video B |
| --- | ---: | ---: |
| Source length (seconds) | 105.70 | 43.49 |
| Coarse frames | 15 | 7 |
| Refined frames / added samples | 127 / 112 | 55 / 48 |
| Derived refinement cues | 15 | 7 |
| Maximum requested timestamp gap (ms) | 1536 | 1500 |
| Refined extracted frame bytes | 391,502,696 | 99,466,064 |
| Coarse detector frames with faces / detections | 5 / 6 | 7 / 53 |
| Refined detector detections | Not yet measured | 404, in 51/55 frames |
| Structural whole-window / source SHA-256 | PASS / PASS | PASS / PASS |

Video B's seven selected cues were **all**
`PossibleNewOrPoorlyObservedFace`: none were selected as
`SceneChange` or `Motion`. The operator visually estimated
approximately ten distinct people of interest **already represented
in the seven coarse frames**, usually only once. No new person was
observed solely in refinement; one or two may be easier to identify
from refined images. These observations are human estimates, not
ground-truth labelled recall or catalogue identity accuracy. The
increase from 53 coarse detections to 404 across all 55 refined
frames is not an increase from 53 to 404 *people*; repeated faces,
missed identities and false positives remain possible. Visual-cue
benefit remains unverified on real footage. These trials do not
establish no-face or >10-minute multi-window case behaviour.

**V01 consolidated review verification, independently rerun from
existing matching private files:** `CombinedEvidenceValidated=true`;
240 eligible people, 596 exemplars; two of two C groups kept, none
merged, dismissed, split or unreviewed; two known-person proposals,
one of two distinct people in initial top-three suggestions. All
seven C-noise/N samples were genuine labelled known-person faces;
19 U samples were all reviewed (7 real, 9 background, 3 uncertain).
Zero N/U unrepresented observations, zero unknown or unlabelled
reviewed presence units, and zero production assignments. The
presence resolver correctly reported `no-merges-to-verify`.
PowerShell `-NoTypeInformation` deprecation warnings did not affect
validation. Previously accepted **V02** human-review metrics remain
historical evidence (6 C groups, 4 manual merges to 2 kept groups);
no new V02 combined worksheet was available, so no fresh V02 source
validation is claimed.

**Next bounded local acceptance (runbook checkpoint 52):** Reuse the
already-extracted coarse/refined owned frame sets from BOTH videos
in **one** read-only all-eligible catalogue run with four distinct
case aliases: V04=Video A/coarse, V05=Video A/refined,
V06=Video B/coarse, V07=Video B/refined. All four compare against
the same one-time eligible-person database snapshot. Review actual
C/N/U and, if available, independent-only I evidence, preserving
real singletons and unknown faces; compare the same persons and
review effort *within each pair*. Do not claim these aliases are four
independent videos. Do not request further extraction, V01/V02/V03
reruns or automatic association writes. Keep reviewer-selected
people and files strictly private.

**Pre-main gates remain:** face/group purity and manual burden,
distinct-person coverage and ranking utility for both videos;
resource-efficiency judgment on heavy 15->127 and 7->55 refinements;
CenterFace rights/distribution, retention/cleanup, safe runtime and
source integrity, main branch reconciliation/rollback, and a
maintainer integrate-vs-separate decision. Production video-person
persistence is still out of this experimental branch's scope.

## Experimental checkpoint 53 — singleton-heavy four-pass results and focused refinement review (2026-11-18)

Maintainer completed the exact four-pass, all-eligible private catalogue
experiment. One snapshot: 241 canonical people, 240 eligible, 596
exemplars; 204 examined frames, 119 frames with detections, 530 retained
YuNet observations and embeddings, zero overflow/discarded. Runtime was
276.553 seconds reported by the experiment and 9.03 minutes end to end.
These are candidate observations, not unique people.

| Evidence | V04 A coarse | V05 A refined | V06 B coarse | V07 B refined |
| --- | ---: | ---: | ---: | ---: |
| Frames | 15 | 127 | 7 | 55 |
| YuNet | 6 | 67 | 53 | 404 |
| Corroborated at .30 | 0 | 8 | 42 | 340 |
| Provisional G-groups | 6 | 62 | 53 | 64 |
| Corroborated C-groups | 0 | 0 | 0 | 9 |
| Corroborated noise | 0 | 8 | 42 | 322 |
| Uncorroborated | 6 | 59 | 11 | 64 |
| Independent-only I observations | 1 | 13 | 22 | 155 |
| N/U/I samples | 0/6/1 | 8/52/13 | 12/10/11 | 87/55/78 |

Across all passes only 18 of 390 corroborated observations were clustered,
with 372 corroborated noise. The C-first combined workspace correctly
cannot represent three of these four cases; the G-group galleries for
V04/V06 opened successfully and presented **all** coarse observations
as singletons. Human review and both strict G-group measurement scripts
passed after correction of an invalid V06 merge chain.

| Reviewed coarse G evidence | V04 | V06 |
| --- | ---: | ---: |
| Initial groups / reviewed | 6/6 | 53/53 |
| Kept roots | 2 | 11 |
| Merge decisions | 2 | 24 |
| Dismissed background | 2 | 16 |
| Cannot tell | 0 | 2 |
| Retained known-labelled groups | 4 | 22 |
| Retained UNKNOWN-labelled groups | 0 | 13 |
| Distinct known people | 2 | 7 |
| Correct initial top-one groups | 0 | 0 |
| Correct initial top-three groups | 0 | 1 |
| Distinct known people with correct top-three | 0/2 | 1/7 |

V06's proposed final group count is unresolved (two uncertain groups);
13 UNKNOWN observations must not be treated as 13 distinct unknown people.
V04's single I-only crop was visually confirmed not a face. V04's four real
YuNet faces were all uncorroborated; the second detector cannot be a hard
suppression gate. Both coarse passes have zero unassigned observations.
The first coarse B estimate of approximately ten visible people is not
a validated total recall denominator.

**SINGLETON-REVIEW-01 (clarified acceptance):** a useful single face
observation must be reviewable with reference suggestions even where
there are no repeated-observation clusters. Evaluate identity utility and
review effort at the person level; neither group counts, nearest
catalogue aliases, corroboration, nor sampled N/U/I evidence are proof of
person presence. Keep unknown and uncertain candidates unresolved, with
explicit bounds; never require a second observation to retain a face.
The current G fallback preserves singleton ranks but produces excessive
manual consolidation and false groups, and the C/N/U workspace rejects
empty sources. The reported rankings are weak. V06 exposed browser
export of invalid merge chains; browser-side validation remains a
separate usability improvement.

**Next focused experiment:** instead of exhaustively labelling the
126 refined provisional groups, the new Windows-only offline
`New-VideoFaceRefinementComparison.ps1` reuses the same report,
matching cases/map and the validated V04/V06 G worksheets. Each *explicitly*
human-labelled coarse singleton is an anchor; do not infer labels from
merges. Search all stored *added timestamp* refined top-three ranks for
the same pseudonym, within its own physical video only. Show at most
three crop-backed suggestions per person with confirmed coarse references;
count missing geometry and people without suggestions. Compare improved
rank, comparable target scores (unknown below top three), and human
same-person/visual-clarity decisions separately. This is a biased
shortlist, not full refined detection review; it cannot discover persons
absent from the coarse labels, verify unknown identities, inspect I-only
ranks, or conclude refinement has no value when no lead exists. No
new inference, database, original video, persisted identity or automatic
face enrolment. Acceptance of the new tool requires synthetic CI and a
bounded maintainer browser check with existing private evidence.

See operations runbook checkpoint 53. Preserve the isolated branch,
unresolved broader quality, rights, privacy, performance and integration
gates; no main promotion.

## Experimental checkpoint 54 — actual focused refinement review accepted (2026-11-18)

**Maintainer local verification of merged PR #821:** synthetic PowerShell
test PASS; private input validation PASS; comparison gallery PASS;
browser CSV export PASS. The exported private review was inspected
offline and contains all 13 lead rows, each with a human decision.
The private file, pseudonyms, detailed scores, per-frame references and
source identities stay outside Git. These findings are **human-labelled
shortlist evidence**, not general video-person recall.

| Measure | A: V04 coarse → V05 refined | B: V06 coarse → V07 refined |
| --- | ---: | ---: |
| Known coarse people | 2 | 7 |
| Additional refined frames | 112 | 48 |
| Added ranked observations | 61 | 351 |
| Known coarse people with added-frame top-three occurrence | 2 | 5 |
| People with crop-backed reviewable shortlist | 2 | 4 |
| Stored target-person occurrences without crop | 0 | 13 |
| Shortlisted / human-reviewed leads | 4 / 4 | 9 / 9 |
| Distinct people visually confirmed in added-frame leads | 2 | 2 |
| Distinct people with visually clearer added-frame leads | 2 | 1 |
| Same-person clearer lead observations | 4 | 2 |
| Same-person not-clearer observations | 0 | 3 |
| Wrong-person suggestion observations | 0 | 3 |
| Background / non-face observations | 0 | 1 |
| Uncertain or unreviewed shortlisted observations | 0 | 0 |

One earlier provisional note stated five V04 leads, but the generated
aggregate and saved CSV both confirm **four**; four is authoritative
for this bounded review.

**Ranking interpretation:** Both A people had no correct coarse top-three
suggestion but have human-confirmed rank-one suggestions on added
refined frames. For B, two people have human-confirmed rank-one
added-frame suggestions; the coarse G-group review had zero distinct
known people top-one and only one known person top-three overall,
so the added-frame review demonstrates some ranking improvement.
The private worksheet alone cannot determine exact target-score
improvement relative to all coarse observations, because absent
top-three aliases have no stored target score. Do not infer person
recall, accuracy percentages for unreviewed frames, or full-video
coverage from this shortlist.

**Learning:** Adaptive refinement can improve identity-review
evidence on both clips, and provide visibly clearer evidence for
3 of the 9 distinct known coarse people across the two clips.
Video B remains much harder: 13 potentially relevant rank occurrences
are not crop-backed, and four of nine sampled suggestions are either
wrong-person or background. Seven B known coarse people are the human
reference set; five have rank leads, four can be reviewed with stored
crops, two are visually confirmed from the shortlist. These are
**distinct denominators** and must not be conflated.

**Checkpoint status:** Focused offline comparison implementation and
its bounded local gallery/export acceptance are PASS. The broader
`DIVERSE-VIDEOS-01`, `GROUP-QUALITY-01`,
`CATALOGUE-TOP3-01`, `FACE-QUALITY-01`, and general
`SINGLETON-REVIEW-01` feasibility gates remain open. Repeated
detections and top-three hits are not distinct people; I-only
candidates and persons outside coarse known labels remain unevaluated
by this specific tool. Do not mandate refinement, automatically
assign identities, filter by CenterFace corroboration, or merge
`wi-0155` into main based on this partial evidence.

Next scoped development choices should address (1) faithful
geometry-backed review for retained rank leads, including explicit
accounting of unsupported candidates, (2) group fragmentation and
singleton-first review without a hard corroboration gate,
(3) catalogue ranking failure diagnosis on genuine faces,
(4) browser merge-chain validation, and (5) bounded processing
cost of refinement and model rights before integration.

## Checkpoint 55 — production review goal clarified: confirmed presence, not finished groups (2026-11-18)

This is a **design/acceptance clarification after the local
PR #821 review and while PR #822 is pending**. It does not change
any previous detector, group, rank or human-worksheet measurements.

**Representative workflow:** G1→person X and G2→Y establish two
unique video/person presence associations. G6 may also depict X:
merging with G1 is optional. G3 may contain both Z and W; exclude
W's crop from G3, establish W's presence **directly from its single
observation** (without requiring another group), then confirm G3's
remaining valid Z evidence as Z. If the operator believes the four
identifiable people of interest have been accounted for, they may
stop and leave G4/G5 unreviewed. Skipped groups retain an honest
outstanding state; no absence, background or all-person
completeness is inferred.

**Revised production acceptance:** The operator can record
one confirmed presence per distinct person, independently of
how many groups or individual crops that person appears in;
identify directly from a singleton/ungrouped face; remove a
misgrouped crop without losing the observation; avoid unnecessary
merging/review of already-found people; correct and undo mistaken
associations; and stop/resume human review without losing
remaining evidence. Auto-assignment of video/person presence is
allowed *only* in a future separately approved high-confidence
identity+margin tier with audit/correction; not in this prototype.
This is about human-confirmed presence outcomes, not a
full tracking or automatic multi-face classification requirement.

**Evaluation adjustment:** Optimize number of distinct
identifiable people recovered per necessary human action and
risk of wrong-person association, rather than demanding all
G/C/N/U groups be labelled, dismissed or merged. Existing
exhaustive review results stay useful as *diagnostic* evidence
for false groups, misses and identity ranking; they are no
longer a proposed routine production workflow. The automatic
sampling requirement to cover the whole source (or explicitly
declare incomplete coverage) remains separate from early
human-review stopping. Do not equate "operator finished
reviewing relevant faces" with "verified no other people".

**Scope boundary:** This is a future production design requirement;
current isolated WI-0155 scripts do not implement general
group-crop exclusion, free-standing person presence editing,
stop/resume, audited auto-assignment or production writes.
Historical face-preservation, no-video-reference-enrolment,
privacy, rights, runtime and integration decision gates remain.
No additional V05/V07 exhaustive group labeling is required.
