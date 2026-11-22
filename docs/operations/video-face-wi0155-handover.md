# WI-0155 — concise status, decisions and handover

**Snapshot:** 2026-11-22, after merged PR #862. **Work-item status: in progress.**
This is the current handover; dated passages in the [work item](../delivery/work-items/WI-0155-video-face-processing-evaluation.md) and older runbooks describe historical stages, not necessarily the current implementation.

**Bottom line:** Bounded offline video-face feasibility and human review have been demonstrated, and an opt-in, revision-fenced production *foundation* is merged into `main`. **Production recognition is not accepted or enabled.** No automatic canonical identity assignment is authorized. The first real persisted FaceEvidence run is blocked after two failed attempts; no successful current real batch or consolidated native-crop browser acceptance has been reported.

## Delivered, and why it matters

| Area | Delivered / evidence | Meaning and limit |
| --- | --- | --- |
| Offline feasibility (merged #824) | Operator-selected, bounded local frame sampling; YuNet detection, SFace embeddings, read-only ranking against an existing photo-person catalogue; optional grouping, independent detector diagnostics, private crop galleries and source-bound CSV/JSON review | Demonstrates an investigable workflow without altering originals, training photo references or writing production identities |
| Human-facing experimental review (#825–#827) | In-app private bundle review, named canonical people, per-face top suggestions, searchable people, hide/reopen, save/import, and explicit unresolved decisions | Maintainer accepted real V04/V06 review mechanics; historical private packages are **not** production FaceEvidence |
| Persisted evidence and scoring (#828, #831, #835, #847) | PostgreSQL migrations v42–v45, immutable current-revision/source-attested observations and model provenance, production best-of-all-confirmed-photo-exemplars matching, selected-job lease/atomic publication, valid zero-face receipts | No pixels, video face embeddings or original paths are stored in the face-evidence tables; zero detected faces does **not** prove zero people |
| Bounded native producer (#833, #845) | SHA-checked original/frame/model handling, native-size YuNet/SFace analysis, photo-priority guards, and **one explicitly selected** video-revision CLI with metadata-only dry-run and separate rights acknowledgements for apply | No bulk scan, unattended face worker activation, automatic hydration or automatic identity tagging |
| Persisted review and presence (#849, #850, #852, #854) | Opt-in per-video review UI, current names and suggestions, source-attested unscaled native crop on explicit inspection, visual-session fencing, append-only decisions, **second explicit approval** to add video/person presence, deduplication and safe undo (schema v46) | Human review alone never tags a person; linked corrections must first unlink; stale/excluded sources fail closed |
| Integration proof (#855; diagnostic fixes #860–#862) | Disposable PostgreSQL + real API TestServer exercises default-off responses, private headers, zero-face 200, decisions, explicit presence, conflicts, undo and exclusion; sanitized one-shot failure checkpoint diagnostics added | Synthetic contracts are tested, **not** real-video crop fidelity, recognition quality or operational readiness |

All of the implementation PRs above are merged into `main` as of this snapshot; the last code change, #862, corrects an overly broad diagnostic test assertion. **A post-#862 local regression result is not yet recorded** in the handover.

## Product and engineering decisions (with reasons)

1. **Measure video-level person presence, not perfect temporal tracks.** One recognizable, human-confirmed face can justify a deduplicated video/person link. Extra observations can help choose a clearer crop but do not require exhaustive face-by-face or group-by-group review. Unknown, uncertain and unreviewed evidence stays explicit; absence is never inferred.
2. **Use the established photo identity catalogue, without retraining it.** Rank current eligible canonical people from their confirmed photo exemplars; production scoring uses the same best-per-person approach as photos. Offline three-exemplar/group rankings were exploratory and often unreliable.
3. **Keep suggestions advisory and automatic assignment off.** The experiments found wrong-person leads and many background false detections. A high cosine score, detector confidence or a second detector agreeing is not a measured low wrong-person rate. Any eventual automatic action needs separately accepted calibration, runner-up margins, provenance, audit, correction and permissions.
4. **Use native-source pixels for human judgment.** In the accepted V06 gallery, 49/53 representative faces were below 64 pixels. A resized proxy or enlarged crop cannot establish identification quality; a guarded original-backed native crop is available only for an explicitly selected current observation.
5. **Reject mandatory CenterFace agreement, dense refinement and exhaustive merging.** The independent detector missed genuine V04 YuNet faces; V03 repeatedly grouped background textures. Optional counterfactual/grouping tools remain useful diagnostically, not as hard production filters.
6. **Integrate small guarded capabilities into Photo Identity, retain the standalone laboratory.** The earlier separate-repository/feature-branch option was not selected for the delivered production foundation. Offline tooling was selectively brought to `main`; producer, review and human presence are separately gated. This is **not** permission to turn on background inference.

## Real evidence and its limits

- Read-only full-catalogue experiment: **241 canonical people, 240 eligible, 596 confirmed-photo exemplars**. An approved two-video coarse/refined comparison examined **204 frames and 530 observations**, with roughly **9 minutes** of end-to-end operator runtime. It showed useful faces and wrong/background suggestions, **not** calibrated identity precision or full-person recall.
- Difficult V03 initially produced **64 groups with 3,505 unassigned observations**; the maintainer dismissed **55/64** reviewed groups as background. Detector/group count is a poor proxy for distinct people. Targeted independent-detector and unassigned reviews improved diagnosis, not a validated safe automatic filter.
- Accepted presence-first V04: **6 face crops**, 4 known-person confirmations yielding **2 distinct people**, 2 background. Accepted partial V06 checkpoint: **53 representative crops**, 33 reviewed / 20 intentionally unreviewed; 15 known confirmations yielded **7 distinct known people**. Subsequent V06 import and in-app review checks passed. Neither sample proves full coverage; unknown *crops* are not necessarily distinct unknown people. No production identity writes were made.
- **Latest real production attempt:** one of two newly selected always-local videos passed admission/preflight, model-hash checks and a verified v46 PostgreSQL backup/isolated restore. The first selected one-shot returned `retry-pending` (~19.6 s). One controlled retry returned `failureStage=tool-execution`, `failureClass=invalid-media-evidence` (~24.9 s). **No FaceEvidence batch was published.** The two failures predate the new granular `failureCheckpoint` instrumentation; the original cause cannot be reconstructed from those results. ONNX initializer warnings do not establish the cause.

## Everything remaining

**Immediate blocked engineering work — do not spend the third attempt to investigate blindly:**

1. Read the current first video's **queue state, attempt count, selected-job receipt, source/revision/exclusion state and owned scratch** without opening originals or mutating production. Confirm no retained private staging. Do **not** process video two, consume the third existing attempt, change model bytes or fabricate a receipt.
2. Diagnose `invalid-media-evidence` without repeating production inference: inspect allowed sanitized diagnostics, review the native extraction/validation/detector/alignment/embedding boundaries, and consider a **separately approved read-only** frame/source probe. PRs #860–#862 preserve future stage/class/checkpoint/exit-code signals; they cannot retroactively classify the prior failures.
3. Fix the demonstrated root cause, add synthetic regression coverage and obtain green CI/local focused tests. **Only with fresh maintainer authorization**, valid backup/restore, unchanged approved model/tool hashes, clean owned scratch, idle photo processing and current revision may a controlled real retry/new selected job occur. Record whether a durable, correctly source-bound nonempty or genuinely zero-face batch is published, including runtime and failure/cleanup behavior.

**Required before claiming the integrated real-media reviewer is production-ready:**

4. On a **separately authorized current nonempty FaceEvidence batch**, perform **one consolidated real browser session** covering #849/#850/#852/#854: default-off 404; exact unscaled native crop versus labelled proxy; tiny/uncertain/false face examples; current person ranking/search; save/hide/reopen/clear; rapid face switching and stale image callbacks; explicit second-step presence approval; duplicate deduplication; correction conflict and safe undo; refresh; exclusion/stale-source denial; no unrelated photo identities or manual video tags changed. A valid zero-face batch must show incomplete coverage, never asserted absence.
5. Obtain explicit operator sign-off for **local original/media access and model/FFmpeg/runtime rights**, permitted source and private biometric-data handling, scratch/crop/worksheet **retention and safe deletion**, source exclusion, database v46 backup/restore and rollback, default-off configuration, and acceptable wrong-person risk. Do not ship private reports, model weights without rights, or source paths/identities to Git.
6. Record the **quality decision**: on a small approved multi-video set, can the chosen bounded sampling produce at least one *recognizable* observation for each visibly present person, with usable ranked suggestions for known people and honest unknown/missed cases? Record omissions, false positives, near-identical observations, runtime and reviewer effort. If the evidence is insufficient, document that explicitly rather than claim automatic precision or full coverage.
7. Verify the current branch's CI and focused Windows PowerShell 7.6 tests. The synthetic PostgreSQL/HTTP test must run against an **explicit disposable admin instance**; it silently skips database execution when its environment variable is absent. Reconcile the stale 2026-11-18 work-item status/branch/dependency notes before marking complete.

**Closure decision (must be written down by the maintainer):** The [original WI-0155 criteria](../delivery/work-items/WI-0155-video-face-processing-evaluation.md#acceptance-criteria--initial-evaluation-only) require a bounded model evaluation, runtime/review findings and a human choice of the smallest safe next product slice; they **do not** require full tracking, automatic identity assignment or a production-wide benchmark. Much of that evaluation is already evidenced. Either **(A)** explicitly close WI-0155 as a completed *feasibility/requirements discovery* item, accepting its limitations and transferring the open production diagnostics, browser/rights/quality gates above into named follow-up work item(s), **without enabling the features**; or **(B)** retain WI-0155 as `in_progress` until steps 1–7 and the integrated production acceptance are signed off. Do **not** silently label the entire product feature complete merely because the initial prototype criteria are largely demonstrated.

## Where to resume (safe verification)

From repo root in **PowerShell 7.6**, after fetching the current `main` (or this documentation PR branch):

```powershell
dotnet test .\tests\PhotoIdentity.Integration.Tests\PhotoIdentity.Integration.Tests.csproj `
  -c Release --filter 'FullyQualifiedName~VideoFaceNativeFrameTests|FullyQualifiedName~VideoFaceSelectedOneShotTests'
if ($LASTEXITCODE -ne 0) { throw 'WI-0155 native/diagnostic tests failed.' }

# Optional only on a disposable PostgreSQL admin instance, NEVER photoidentity_main:
if (-not $env:PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING) {
    throw 'Set the disposable test admin connection or do not claim HTTP/PG acceptance.'
}
dotnet test .\tests\PhotoIdentity.Integration.Tests\PhotoIdentity.Integration.Tests.csproj `
  -c Release --filter 'FullyQualifiedName~VideoFaceLeasedPublicationTests'
if ($LASTEXITCODE -ne 0) { throw 'WI-0155 HTTP/PG test failed.' }
```

**Detailed operator procedures:** [production FaceEvidence, admission, one-shot and combined review runbook](video-face-review-production-wi0155.md); [offline feasibility and original private review](video-face-feasibility-wi0155.md); [pre-main experimental decisions](video-face-wi0155-pre-main-decision-record.md). The original experimental media, private people maps, crops and review CSVs stay outside Git; do not request them for a routine documentation handover.
