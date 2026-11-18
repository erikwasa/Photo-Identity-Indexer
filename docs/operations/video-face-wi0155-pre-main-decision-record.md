# WI-0155 — pre-main decisions, delivered experiments and retention/removal review

Status: **draft for maintainer sign-off, 2026-11-18**. Scope:
the isolated `wi-0155` video-face feasibility tool and its tests. This is
not a production activation plan or an authorization to persist identities.
See [WI-0155](../delivery/work-items/WI-0155-video-face-processing-evaluation.md)
and [the feasibility runbook](video-face-feasibility-wi0155.md) for full
chronological experiment evidence. Never commit the private human-review CSVs,
people map, snapshots, extracted frames, source paths or original media.

## 1. Product decisions (accepted direction)

| Decision | Rationale and implementation boundary |
| --- | --- |
| **Video-person presence is the result** | The persistent production concept is a deduplicated video/person association. A provisional G/C/D group is a review aid, not a person or a required persistence unit. One credible human-confirmed face is sufficient. Repeated groups of the same person need not merge or be reviewed. |
| **Presence-first human review** | Allow confirmation from one crop/singleton, optional per-crop exclusion in mixed groups, known-person deduplication, skipping/reopening unreviewed groups and explicit incomplete-coverage state. Do not require every group to be merged, dismissed or labelled. |
| **Rare automatic identification only** | Production auto-assignment would require calibrated identity similarity, sufficiently strong winner/runner-up separation, permissions, provenance, audit, correction/undo and measured false-positive risk. Neither detector confidence nor independent-detector corroboration is sufficient. Auto-assignment is **not** in WI-0155. |
| **Use the existing eligible photo catalogue** | Read-only, dedicated PostgreSQL snapshot with current still-photo SFace exemplars; rank across all eligible canonical people. Never enroll video crops/embeddings into still-photo reference collections. Any newly created person must use the ordinary reviewed identity workflow. |
| **Sampling and review stopping are different** | Cover each approved video in bounded windows and explicitly report any incomplete automatic coverage; after that, the operator can stop manual review when satisfied. Unreviewed/ungrouped people must never be silently classified as absent or background. |
| **CenterFace is optional, advisory counterfactual** | It missed four real V04 YuNet face observations; one independent-only V04 sample was not a face. A hard CenterFace agreement gate is rejected. YuNet-only and CenterFace-only evidence remain available for research; not a mandatory production filter. |
| **Refinement is an opt-in value question** | Adaptive refinement can make people clearer or improve suggestions, but also produces repeated observations, cost and poor ranking. Do not mandate a large dense pass. Do not equate hits with distinct people. |
| **No automatic processing or video-source modifications** | Explicitly approved local files only; hash/source-bound extraction and bounded private frames, no OneDrive hydration or production database writes. Keep the video processing boundary distinct from the still-photo identity indexer until explicitly accepted. |

## 2. Verified work and observations

These are **human-labelled experiments**, not population precision/recall
or complete production acceptance.

| Phase | Verified evidence / constraint |
| --- | --- |
| Initial private pilot and fidelity | Standalone ONNX detector/embedding experiments, native-frame/rotation/fidelity checks, approved local frames and original video SHA-256 integrity. V01/V02 supplied some recognizable existing-person top-three evidence; difficult V03 supplied background/noise/singleton stress evidence. |
| Existing catalogue snapshot | Full eligible evaluation used **241 canonical / 240 eligible people and 596 still exemplars** with a dedicated SELECT-only snapshot; no production identity writes. |
| Adaptive two-video coarse/refined coverage | A: 15→127 frames over 105.70 seconds; B: 7→55 over 43.49 seconds; no missing windows/refinement-cap windows, original hashes unchanged. Extractions cost about 391.5 MB and 99.5 MB respectively for those private workspaces. |
| Same-snapshot four-pass evaluation | 204 frames, 530 YuNet observations/embeddings, no overflow/discards. 390 corroborated YuNet observations; just 18 joined 9 CenterFace-corroborated DBSCAN clusters; the other 372 were noise. 140 retained YuNet detections were uncorroborated, and 191 I-only detections are not automatically real faces. CLI reported ~276.55 s evaluation and end-to-end operator run ~9.03 min. |
| Human coarse G review | V04: 6 reviewed singletons, 2 kept+2 merges+2 background, 2 known people, 0 correct person top-three. V06: 53 reviewed singletons, 11 kept+24 merges+16 background+2 uncertain, 7 known people, 1 correct person top-three. These exhaustive reviews establish **diagnostic burden**, not a production requirement to merge all fragments. |
| Refined shortlist acceptance | PR #821 gallery/test/export PASS. A (V04→V05): 4 reviewed leads, two known people both confirmed and clearer, their added-frame suggestions ranked first. B (V06→V07): 9 reviewed leads, two known people confirmed (one clearer), 3 wrong-person leads, 1 background lead and 13 top-three occurrences without crop geometry. The rank-biased shortlist does not establish recall for people outside coarse labels or below stored top-three. |
| Presence-first design clarification | Confirm a known person once from a crop; duplicate groups need not merge. A mixed group can have W's crop annotated as excluded and W's presence independently proposed while other crop(s) confirm Z. G4/G5 may remain unreviewed. This is the new production direction, not an already delivered database workflow. |

## 3. Proposed main-boundary scope and cleanup ledger

We distinguish **rejected production behavior**, **diagnostic tools worth
retaining privately until acceptance**, and **candidate removals**.
Do not delete experimental evidence or regressions solely because the
production UI should not expose them.

| Feature | Decision | Action |
| --- | --- | --- |
| Batch-merging all groups labelled as one person | **Remove**: duplicate presence does not need consolidation; batch merging is high-risk and has no essential place in the new workflow. | This PR removes the combined-workspace batch merge controls and implementation/tests. Explicit, reviewed optional *pairwise* diagnostic merge remains. |
| Mandatory complete G/C/N/U human classification, forced merging of duplicate groups | **Reject for production**; old samples remain evidence. | New opt-in presence-first gallery accepts partially reviewed per-crop decisions. Preserve older source-specific validators for reproducing prior experiments; do not make full review a launch prerequisite. |
| Hard CenterFace agreement gate / rejecting genuine YuNet-only singleton | **Rejected** based on V04. | Never enable for production; CenterFace C/N/U/I is a diagnostic comparison, not an automatic suppression criterion. |
| Always requiring 2+ observations per person | **Rejected**. | Support one verified crop to propose video-person presence. Do not delete true singletons solely to improve group purity. |
| Automatically training still-image identity references from video crops | **Rejected**. | No video-derived exemplars or automatic person-enrolment pathways; retain no cross-media model-training feature. |
| Mandatory dense refinement to 127/55 frames, or exhaustive group merging afterward | **Not justified**. | Keep bounded optional sampling for evidence gathering; determine production trigger and resource caps before activation. |
| Provisional G-groups, shadow DBSCAN, CenterFace corroboration, N/U/I audit tools, old ranking galleries | **Research-only / retain until evidence sign-off.** Useful for provenance, regression and diagnosing missing people/background. | Do not expose as an automatic production identity pipeline. Consider a later archive/removal PR only after replacing their diagnostics and agreeing on supported tooling. |
| New crop-level presence-first opt-in gallery | **Experimental implementation in this PR.** Group and direct crop decisions separated; no SQL or auto identification. | Verify locally with previously approved private cases, and measure deduplicated known presence, mixed groups, exclusions, unreviewed groups and missing representative crop evidence. |
| Automatic video processing in normal Photo Identity import/captioning | **Not accepted or implemented.** | Do not wire video detection into production jobs, API startup or storage without a separate explicit review and rollout plan. |

## 4. Integration risks and gates before a PR to main

**Branch divergence matters.** As inspected on 2026-11-18,
`main...wi-0155` reported **565 WI-0155-side commits and 415 main-side
commits** (branch-history counts, not independent change counts).
The diff against `main` was dominated by added
`tools/PhotoIdentity.VideoFaceFeasibility/`, its test project, new
WI-0155 documentation and solution/CI/status registry edits. This is
favorable for a *selective* clean integration candidate, but does not
establish absence of conflicts.

Proposed acceptance sequence:

1. Merge and test focused WI-0155 fix PRs on `wi-0155` only.
   Archive accepted aggregate results; preserve all existing private
   video/face worksheets and owned frame workspaces until operator-approved
   retention/cleanup.
2. **Select a minimal supported main-boundary:** decide whether the
   experimental code remains an opt-in offline tool/test project or whether
   any API/UI/persistence belongs in the first main PR. Current proof
   supports the offline tool; it does *not* support automatic production
   identification or durable association persistence.
3. From up-to-date `main`, prepare a separate **integration-candidate
   branch**, bringing over approved tool/test code and the required
   solution/CI/docs changes. Reconcile regenerated work-item
   status/roadmap files with current `main` instead of overwriting
   newer unrelated main changes. Review any licensing/redistribution
   impacts of YuNet, SFace, CenterFace, FFmpeg and selected runtimes
   before shipping. No opaque weights, real media, private hashes or
   credentials in Git.
4. Test Windows PowerShell 7.6 synthetic scripts and Node JS, C# unit
   tests, full solution build/tests, docs validation/generation, and
   existing photo/video viewer/processing regressions on the
   integrated candidate. Verify no transitive startup behavior and
   no unsanctioned source/media or PostgreSQL writes.
5. Perform maintainer local acceptance against approved private
   source evidence: group/singleton direct presence, duplicate
   deduplication, mixed-group crop exclusion, stop/resume with
   unresolved evidence, safe import/review, no unintended network,
   no source mutation, no original OneDrive access and explicit
   cleanup boundaries. Record measured runtime/crop storage and
   remaining missing-crop/false-suggestion cases.
6. Only after the maintainer chooses **integrated offline module vs.
   separate experiment** and signs off evidence, propose the
   minimal scoped PR against `main`. Document rollout/rollback
   (including disabling the feature/entrypoints and retention of
   immutable original media). A production identity association
   editor and exceptional auto assignment remain separately gated.

**Current assessment:** Better presence-first semantics and a verified
offline full-catalogue evaluation are meaningful progress, but
production UX/persistence, general direct-crop coverage, limited
representative geometry, catalogue rank reliability, branch
reconciliation, licensing and perf/retention remain unresolved.
No broad main merge or production activation is authorized by this
record alone.

## 5. Additional pre-main corrective fix — browser merge validation

The actual V06 provisional review once exported invalid merge chains
that the PowerShell aggregation later rejected. This PR adds a
shared, **fail-closed** browser merge validator for standalone
G/C/D group worksheets and the legacy combined C/N/U workspace,
on both Save and Import. It rejects unknown/self/circular targets,
cross-source merges, merges ending in anything other than a kept
group, stale targets and conflicting explicitly reviewed identities.
The existing source-bound PowerShell validators remain authoritative;
browser validation is an early usability/safety check, not a substitute
for complete source-frame collision validation.

Synthetic Node tests are required to pass in CI. No automatic
merge or identity action is introduced. Reviewed optional pairwise
human merges remain available only in the legacy diagnostic UI.
## 6. Real V04 presence-first acceptance and remaining pre-main decision

**2026-11-18:** The main-based draft PR #824 passed all six Windows
CI jobs on the corrected PowerShell synthetic-test commit.
The maintainer ran the opt-in actual V04 presence-first gallery
and exported a private CSV which passed strict source-bound
aggregation. Six source-derived representative crops were
reviewed: four known-person confirmations deduplicated to two
distinct people, two false/background faces, zero unresolved
groups, no manual exclusions or mixed groups, and **zero
production assignments**. The CSV's six row keys, single source
hash and aggregate counts were checked independently without
retaining aliases or any biometric data in Git.

**Remaining targeted acceptance:** Browser CSV Import/reload
has not been explicitly reported as passed; V04's exhaustive
six-item classification does not independently demonstrate
early operator stopping. Use a few V06 crop decisions and
leave the majority unreviewed, then Save, Import and aggregate
without additional inference or full re-review. The
mixed-group correction and duplicate-person semantics have
synthetic regression coverage; actual G coarse groups have
only one representative crop each and cannot establish
real mixed-crop behavior without inventing evidence.

Separate sign-offs remain required for model/right-to-use
and redistribute, private evidence retention/deletion,
bounded runtime/storage, offline-only initial integration,
no production source/database writes, and rollback.
## 7. V06 presence-first real-data checkpoint — partial review accepted

**2026-11-18:** The main-based candidate's V06 real gallery
generated 53 original G groups and 53 representative crops.
The private CSV passed strict input-source validation:
15 confirming known-person face crops were deduplicated to
**seven distinct proposed people**, 8 other observations
were unknown-real-face, 7 background, 3 uncertain and
**20 deliberately left unreviewed**. Twenty groups had
no reviewed crop, without being marked absent, false or
completed. The same private measurement reported zero
group exclusions, no mixed-person groups, no unassigned
observations, `PersonCoverageProven=False` and
`ProductionAssignmentsMade=0`.

This provides bounded proof that partial human review is
represented honestly rather than forcing exhaustive
G-group completion. It does **not** show all-video recall,
validate known-person labels independently, resolve the
unknown observations into distinct persons, or prove the
browser's Save/Import round trip (not explicitly reported
by the maintainer yet). An earlier V04 all-reviewed sample
already validated 4 known crop confirmations → 2 people
and 2 background crops.

**Current CI caveat:** The previous full main-based CI
run's fast-assembly step failed on an existing Windows
`VideoGuardedToolExecutorTests.Photo_priority_revokes_child_and_cleans_scratch`
shutdown safety test. WI-0155 tests, integration shards,
launcher and package checks passed. That failure must not
be dismissed as flaky without a successful rerun and
source-safe shutdown confirmation. Do not weaken process
revocation/owned-scratch cleanup merely to merge.

## 8. V06 browser round-trip locally accepted (2026-11-18)

The maintainer confirmed **V06 CSV Import: PASS** after the
previously validated partial V06 review. Together with the
strict source-bound CSV metrics, V04 acceptance and synthetic
regressions, this completes the scoped presence-first gallery
verification: review, skip, deduplicate, save, import and retain
explicit unresolved states. The underlying identities and private
CSV have not been checked into the repository.

Still open for an offline main merge: all current candidate CI
checks must pass, runtime/model licensing and distribution must
be reviewed, and an explicit private crop/worksheet retention,
safe deletion and rollback policy must be accepted. Production
persistence, automatic recognition and the full-video-recognition
quality criteria are not covered by this approval.
