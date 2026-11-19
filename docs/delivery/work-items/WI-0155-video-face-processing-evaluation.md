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

## 2026-11-19 — Accepted review UX and productionization direction

PR #826 was merged and **all 12 maintainer local acceptance checks PASS**:
source-bound V06 v2 JSON (53 groups and 53 crops), native/frame/face
geometry, 241 canonical snapshot names, search and human selection,
CSV restoration and export, original V06 aggregate counts, no identity
writes. This evidence is accepted; do not ask the maintainer to repeat it.

**Maintainer product direction:** stop investing in standalone
experimental gallery variants; develop the integrated video review
experience and production-grade ingestion/presence path. Explicit UX
requirements: show top suggestions as individual actionable options,
clicking one confirms that specific face and automatically hides the
completed crop; reviewed crops are hidden by default with a way to
reopen them. Source of current names must be the application's
PostgreSQL catalogue, not a separately imported browser file.

**Draft PR #827 (this branch):** use the existing read-only
`/api/review/people/maintenance` endpoint for current canonical
names; historical v3 review bundles contain source-matched
alias→person-UUID bindings (but never names), generated using the
existing private source map only at package creation. A selected
suggestion confirms one crop, and hides it without scrolling back
to the beginning; importing reviewed CSV hides completed crops,
while unreviewed or incomplete known-person decisions remain open.
Already-generated per-face `rankedFaces` suggestions replace
group-averaged leads in new review packages. v1/v2 bundle and
source-hash/alias-only CSV support remain for legacy recovery.
DB outage or a person absent from the historical alias map must
fail closed for confirmation. No new inference or production writes.

**SFace production-parity audit:** same YuNet, OpenCV face aligner,
exact SFace embedder/protocol are used in photo and video, but
the photo regeneration scorer uses **all confirmed face exemplars
per canonical person and a per-face maximum cosine score**. The
offline video experiment limits the set to **three distinct
still photos per person**, then may combine two observations
per provisional group. Additionally, compressed sampled video
frames and tiny native face regions carry substantially different
input evidence. PR #827 corrects the *UI's group-ranking attribution*;
it does **not** claim to repair reference-set selection or improve
recognition accuracy. Those need a production-parity before/after
identity benchmark using unchanged reviewed V06 labels,
differentiating per-face rank from temporal grouping.

**Next production vertical slice (not authorized in this PR):**
source-revision-bound frame discovery and bounded native-quality
evidence in the existing guarded video worker; versioned face
observations and read-only candidate scores; auditable reviewed
video/person presence with deduplication and correction/undo;
shared canonical person eligibility/merged-person fencing; privacy,
retention, source deletion, model licensing, performance and
failure rollback. Automatic assignment remains disabled until
false-positive risk is explicitly measured and accepted. This
is product implementation work, not another independent
shadow-experiment acceptance loop.

## 2026-11-18 — V06 in-app acceptance and named-review quality follow-up

The maintainer completed the **merged PR #825 local web review**, using
53 V06 provisional groups and 53 source-backed native-resolution crops.
JSON import, image rendering, prior CSV import, hide/reveal, CSV
export/re-import and strict source-bound measurement passed. The
independent aggregate confirms correct deduplication:
**15 known-person crop confirmations → 7 distinct known people,
8 duplicate confirmations**. Additional review decisions in the new CSV:
9 unknown-face crops, 10 false/background crops, 4 uncertain,
**15 unreviewed**, 0 mixed/excluded, `InputEvidenceValidated=True`,
`PersonCoverageProven=False`, `ProductionAssignmentsMade=0`.
No further reclassification is requested.

The operator measured native PNG crop long edges: **53 measured,
minimum 24 px, maximum 102 px, mean 39.45 px; 49/53 under 64 px,
53/53 under 112 px**. This is a material usability/feasibility
constraint; no upscaling or cosmetic enlargement should be pursued
as a substitute for actual video evidence. Native video-frame
quality, pixel-scale face geometry and better genuine temporal
representatives remain questions for a separately gated trial.
See [source-to-video/person diagrams and explicit stage comparison](../../operations/video-to-person-review-pipeline-wi0155.md).

**Follow-up product-facing review slice (this branch):** The source
snapshot already includes a private `P0001 → canonical UUID/name` map
for **all** current people (241 canonical, 240 eligible for SFace
ranking in the completed experiment). A versioned review bundle now
carries only the original snapshot identifier and selected-person count;
the user separately imports the original private map inside the local
web application. It exposes real names for exploratory group leads,
a searchable selector across all canonical people, and native PNG,
extracted-frame and detector-face pixel dimensions. The CSV remains alias-only and tied to the original
report hash; existing V06 decisions can be re-imported. Both files
remain local browser-memory data, without API queries or DB writes.
A mismatched catalogue snapshot is rejected. Legacy v1 packages remain
alias-only; regenerate a v2 review bundle from already approved V06
evidence without repeating inference.

**Unchanged scope boundary:** The production video ingestion path
still publishes metadata/proxies/posters and supports manual video-person
tags; it does not ingest face suggestions. Native-frame quality
selection, a live catalogue-backed named picker and persisted,
auditable presence review are desired future product features, not
authorised WI-0155 production changes. This review slice needs CI
and one local native-names/CSV smoke before acceptance. The WI-0155
feasibility closeout still requires explicit maintainer agreement
on observed limitations, model rights, private retention/cleanup
and rollback. No automatic identity assignment is accepted.

## 2026-11-18 — In-app review integration after PR #824

PR #824 was merged to main as an **opt-in offline experiment**, not
production recognition. The maintainer selected the next user experience:
**move crop-level video review into the existing Photo Identity web app,
rather than treating exported static HTML galleries as the product UI.**
The historical HTML/CSV tooling remains a diagnostic fallback until the new
workflow is locally accepted; it is not removed without an exit path.

This branch provides a first vertical slice at /video-face-review, linked
from Videos and Advanced navigation. The existing validated offline
-PresenceFirst runner additionally exports a bounded private
presence-first-web-review.json (report SHA-256, clip/group/crop keys,
advisory pseudonym ranks and base64 reconstructed PNGs). The operator
explicitly selects that file in the already-running web app. Review,
hide/reveal, one-crop known-person confirmation, unknown/background/uncertain,
per-crop exclusion, deduplicated presence counts and CSV Save/Import run
**in browser memory**. Nothing is uploaded or stored by the API. The
standalone **source-bound PowerShell measurement against the private report
and people map remains the final validator**; a browser-visible report hash
is not independent provenance proof.

Scope and safeguards:
- No video source reads in the web app, inference, OneDrive hydration,
  API endpoints, database migrations/writes, auto-enrolment, automatic
  identity assignment or production person presence changes.
- Bundle contains sensitive crops: operator-owned local files, no
  Git/public asset or browser-storage persistence. Navigation warns on
  unsaved edits and clears the live review session. Existing private
  source artifacts are not silently deleted.
- A confirmed P0001-style alias is a **private proposal**, not yet
  an authenticated canonical person ID; human alias lookup and
  existing-source CSV verification are required. An incomplete review
  cannot claim full person coverage. Unknown crop counts do not represent
  distinct unknown identities.
- Synthetic PowerShell and Node tests must cover deterministic bundle
  construction, schema/crop bounds, privacy redaction, original-input
  integrity, valid/invalid cross-source imports, duplicate people and
  explicit unreviewed groups. Current CI and a one-time approved local
  **in-app import → selective review → export → import → strict measure**
  smoke are pending.

**WI-0155 closeout proposal, not yet completion:** This item was scoped
as a bounded *feasibility/desired-behavior decision*, not production
recognition rollout. Close it only after integrated CI and the one new UI
smoke pass and the maintainer accepts the evaluation conclusions:
existing-photo read-only rankings are helpful leads but too unreliable
for automatic assignment; presence-first manual review is the smallest
safe behavior. Record final resource/retention and model-rights boundaries
explicitly. Durable video/person presence persistence, vetted
auto-recognition, sampled-frame coverage quality and any integrated
automatic processing require separately scoped implementation/acceptance;
they must not become hidden WI-0155 completion requirements.


## 2026-11-18 — Offline main integration candidate (pending acceptance)

**Proposed integration:** Only the standalone opt-in video-face feasibility
tool, its Windows/Node/C# tests and documented private review runners
are under consideration for `main`. Normal Photo Identity APIs,
workers, still-image indexing, database schema, identity persistence
and automatic video processing are **not changed**. The full
chronological experiment diary remains on the `wi-0155` branch;
this checkpoint is the concise integration summary.

**Production workflow decision:** A video should carry a **deduplicated
set of known people confirmed present**, not an exhaustively resolved
set of face groups. One credible face crop may be sufficient.
Provisional groups and top-three suggestions assist human discovery.
Repeated groups of the same person need not merge. A mixed group may
have an incorrectly grouped crop annotated as excluded and that
person independently confirmed. Other groups can remain explicitly
unreviewed. The operator may stop *manual* review when satisfied;
automatic video sampling must still report bounded coverage honestly.
The current standalone tool produces only **private proposals**,
not production associations. No automatic identity assignment is approved.

**Evidence from the private four-pass experiment:** The approved A/B
coarse/refined evaluation used 204 frames, 530 YuNet detections
and embeddings, an eligible read-only snapshot of 240 people and
596 still-image exemplars. V04/V06 coarse human review found
2/7 distinct known people, but only 0/2 and 1/7 known people
were suggested correctly in the top three. The separately reviewed
refined shortlist confirmed two known people on each video and
clearer crops for two people on A and one on B; B also yielded
three wrong-person suggestions, one background detection and 13
missing-geometry ranked occurrences. These are bounded human-review
observations, **not whole-video person recall or precision**.

**Rejected or removed:** Batch merge of already-labelled groups
has been removed, while optional pairwise diagnostic merging remains.
No mandatory complete group classification, hard independent-detector
suppression, requirement of multiple appearances, mandatory dense
refinement, video-reference enrolment or uncalibrated auto-matching.
The historical G/C/N/U counterfactuals and auditors are retained as
**optional offline diagnostics**, not new production processing.

**Current acceptance blockers:** Main-based full CI/integration
verification, local V04/V06 crop-review Save/Import and source-bound
aggregate validation, runtime/face-model rights and redistribution,
private crop retention/deletion, startup/network/database no-write
checks and a rollback plan. Persisting video/person presence in
production remains a separate future implementation choice.
`WI-0155` is still **in progress**.

See [pre-main decision/removal record](../../operations/video-face-wi0155-pre-main-decision-record.md)
and [bounded offline verification](../../operations/video-face-feasibility-wi0155.md).

## Current status — 2026-11-08

**In progress on the long-lived `wi-0155` feature branch**, which remains
separate from production `main`. Experimental development started on
2026-11-05; PRs [#757](https://github.com/erikwasa/Photo-Identity-Indexer/pull/757)
(read-only existing-catalogue identity evidence) and
[#760](https://github.com/erikwasa/Photo-Identity-Indexer/pull/760)
(higher-resolution, denser frame coverage) were merged into that feature
branch, **not `main`**.

The current feasibility question is whether a few explicitly approved local
videos provide at least one *usable face observation per visible person*,
and, when eligible existing Photo Identity identity evidence exists, useful
**read-only, ranked known-person suggestions** for human review. Include
unknown-to-gallery people, missed faces, ambiguous and incorrect matches,
runtime and human review effort; do not mistake more detected frames or
higher raw similarity for identification accuracy. No production catalogue
writes, canonical identity assignments, background archive scans or
automatic video hydration are authorized.

The detailed experiments and evolving acceptance procedure remain on
`wi-0155`. A final maintainer decision about **integration into Photo
Identity versus a separate component** is deliberately deferred until
multi-video review. This status update does not merge the prototype or
approve production recognition.

## Product direction (maintainer 2026-11-02)

**Face recognition in videos is wanted**, but the architecture,
review UX and accuracy requirements are not fixed. This item should
begin as a **bounded feasibility prototype and requirements discovery**,
not automatically implement person tracks, a full review workflow,
large retention schemes or a video-wide identity engine. It was initially
proposed behind playback/compatibility priority; the 2026-11-05
isolated-feature-branch decision superseded that scheduling constraint.

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

- Status: in_progress on `wi-0155`; the first work item is still a
  feasibility/prototype decision, not production recognition acceptance.
## 2026-11-18 — Main-candidate V04 presence-first local acceptance

**Actual maintainer test on main-based draft PR #824:** The opt-in
`-PresenceFirst` provisional G gallery generated from the existing
approved V04 four-pass report/cases: schema 1; 6 groups; 6
representative crops; 0 unassigned and 0 unshown group
observations; `ProductionAssignmentsMade=0`. The maintainer saved
a private crop-review CSV and ran
`Measure-VideoFacePresenceFirstReview.ps1` against the
corresponding private report and people map. It returned
`InputEvidenceValidated=True` and the following **aggregate
human-reviewed outcomes**:

- 6/6 unique representative crop rows reviewed, no unanswered or
  uncertain decisions; 4 known-person confirmation crops and 2
  background/not-a-face crops.
- **2 distinct known-person presence proposals** from 4
  confirmations; **2 duplicate known-person confirmations**
  correctly deduplicated without merging provisional groups.
- 0 unknown, 0 excluded-crop annotations, 0 mixed-identity groups,
  0 unreviewed groups, 0 unshown or unassigned group
  observations.
- `PersonCoverageProven=False` and
  `ProductionAssignmentsMade=0` throughout.

The uploaded private CSV was independently checked only for its
schema, row uniqueness, decision counts and aggregate alias
cardinality. It is **not stored in this repository**. Successful
Save/export and source-bound CLI verification are demonstrated;
**CSV Import/reload was not separately reported** and must not
be represented as locally passed. The all-reviewed V04 worksheet
also does not exercise partial/early-stop behavior or mixed-group
crop correction; these are supported by synthetic tests and need
a bounded real-data smoke check without exhaustive review.

**Main-candidate CI:** The previously reported full Windows GitHub
Actions run for commit `e437d0c0` succeeded on all six jobs,
including C#, PowerShell and Node synthetics, launcher,
package, integration shards and generated docs. No automatic
production identification or video/person persistence is implied.
The pending verification gates remain local Import and selective
V06 early-stop acceptance, model/right-to-distribute and private
output retention/rollback sign-off. Do not merge solely on the
V04 aggregate.
## 2026-11-18 — V06 presence-first partial-review verification

The maintainer opened the opt-in **main-based candidate** `V06`
presence-first G-group gallery. It returned mode `PresenceFirst`,
**53 source-backed groups / 53 representative crops**, and
`ProductionAssignmentsMade=0`. The saved private worksheet was
validated with `Measure-VideoFacePresenceFirstReview.ps1`, returning
`InputEvidenceValidated=True`, consistent single-crop source
accounting, and the following aggregate reviewer decisions:

| V06 source-bound presence-first result | Count |
| --- | ---: |
| Representative crops | 53 |
| Known-person confirmation crops | 15 |
| **Distinct known people proposed present** | **7** |
| Duplicate known-person confirmations | 8 |
| Unknown-real-face crop observations | 8 |
| Not-a-face/background crop observations | 7 |
| Cannot-tell crop observations | 3 |
| **Unreviewed representative crops / groups** | **20 / 20** |
| Mixed-person group labels / exclusion annotations | 0 / 0 |
| Unshown group observations / unassigned observations | 0 / 0 |

**Review coverage:** 33 of 53 representative crops received a
human review decision; 20 remain explicitly unreviewed. This
demonstrates that incomplete manual review is a valid *input
state* and does not prevent correct deduplicated known-person
proposal accounting. `PersonCoverageProven=False` and
`ProductionAssignmentsMade=0` by design. Eight unknown-real-face
**crops** are not eight distinct unknown people; seven proposed
known people do not establish full video-person recall or the
correctness of every identity without the underlying human review.

**Acceptance granularity:** This confirms source-bound V06 gallery
generation, real private CSV export and strict aggregate measurement,
including partial-review preservation. The maintainer has **not
explicitly confirmed browser CSV re-import/reload** in this result,
so the Save → Import round trip remains a narrowly scoped local
check. The one-representative-per-G-group source has no real
mixed-person group to exercise per-crop correction; synthetic
tests cover that UX. Neither source file nor private CSV is
committed. No extra full-video inference or exhaustive G/N/U
review is necessary for this milestone.

PR #824 also reported all WI-0155 tests and both integration shards
passed. The most recent full run on the preceding documentation
checkpoint did **not** pass overall: an unrelated existing
`PhotoIdentity.Core.Tests.VideoGuardedToolExecutorTests.Photo_priority_revokes_child_and_cleans_scratch`
test failed while verifying Windows child-process shutdown.
A targeted rerun was requested; the test is outside this
WI-0155 offline diff. Treat the integration CI gate as open
until the current candidate's CI is green. Do not suppress
that test or change its fail-closed cleanup safety behavior
without a separate investigated fix.

## 2026-11-18 — V06 private CSV import accepted

The maintainer explicitly reported **V06 CSV Import: PASS** in the
presence-first browser, following the already source-validated
partial V06 review (53 representative crops, 33 reviewed, 20
unreviewed, 15 confirming known-person crops → 7 distinct known
people, 0 production identity writes). This closes the remaining
local Save → Import workflow check without another inference or
full-group review. Import success does **not** prove complete people
coverage, mixed-group handling on this single-crop-per-group dataset,
production persistence, or automatic identity precision. The
private worksheet remains outside Git. Remaining pre-main gates
are current candidate CI, offline-only delivery scope, model/runtime
rights, private artifact retention/deletion and rollback.
