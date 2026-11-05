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

## Acceptance criteria — initial evaluation only

- [ ] A small approved real clip/frame set shows whether existing
  models can find and suggest known people, with explicit examples
  of duplicates, misses and uncertainty.
- [ ] Basic runtime/review burden is recorded; a rough, reproducible
  comparison is sufficient for the initial decision.
- [ ] Maintainer reviews the prototype and identifies the desired
  user behavior and a smallest safe production follow-up, or records
  why further experimentation is needed. No unreviewed canonical
  assignments or original-file changes occur.
- [ ] Any complex tracking, production persistence or full-scale
  benchmarking becomes a **separately scoped workitem** only when
  evidence shows it is worth doing.

## Verification requirements

Bounded prototype and maintainer review. No changes to production
catalogue or biometric governance during exploratory work.

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
