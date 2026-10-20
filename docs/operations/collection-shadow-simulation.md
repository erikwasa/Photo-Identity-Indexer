# WI-0195 — safe, read-only collection-shadow simulation

**Research-only. Not a deployed collection policy.** This phase follows the completed WI-0193 calibration/held-out study and WI-0194 visual-redundancy no-go. It does **not** approve an absolute quality tier, automatic Very Low exclusion, an automatic redundancy threshold, or a combined production policy. The former WI-0195 four-arm production-quality experiment remains **blocked**. This workflow tests only what the existing 600 scored photos and captured real **read-only** Creative Collection previews can support. Never use the consumed WI-0193 held-out human quality ratings or the WI-0194 reviewed pair labels as input.

## What the simulator does

1. Accepts existing private TOPIQ NR (`pyiqa-topiq_nr-0.1.16`) raw proxy scores from WI-0193, fixed higher-better and **exact raw score-gap 0.16755089163780212**, unchanged from the one-shot research freeze. No model checkpoint is loaded; no new inference is performed. Only raw scores, *not* the benchmark's human quality labels, are read.
2. Imports a **captured read-only Creative preview** (Smart anchored or search anchored) `selectedCandidates` with revision ID, existing score, kind, and original order. For the shadow, deliberately choose **fewer photos** than the captured preview contains. It never assumes the preview `Candidates` (which lack selection scores) are equivalent to `SelectedCandidates`. Selected candidates are a shortlist, not an entire original candidate universe.
3. Simulates the baseline first N vs the optional **research-only selective quality preference** in contiguous runs of *exactly identical existing selection score and candidate kind*. A candidate may be promoted only when its existing TOPIQ raw score exceeds the current candidate by at least **the frozen gap**. Missing evidence or smaller gaps abstain. A photo may **never** cross relevance-score, kind, eligibility, direct/context or original anchor boundaries. No new photo is introduced.
4. Explicitly disables three unvalidated arms in both the code and report: **Very Low exclusion**, **automatic redundancy suppression**, and **combined quality+redundancy suppression**. Earlier WI-0194 dHash0–8 and CLIP review buckets were promising but insufficient to pass 95% false-suppression safety; do not use them as suppression rules. Do not fabricate four successful arms.
5. Produces **new private exclusive-write** blinded left/right photo-ID lists, separate score/policy/source audit, sanitized stdout counts including TOPIQ score coverage and changed-selection slots. When the candidate overlap with the 600-photo WI-0193 export is too small, **there may be zero actual changes**; the runner correctly stops without requiring human preference review. A zero-change outcome is a feasibility limit, not proof that quality preference has no value.
6. Optional current-proxy read-only .NET mapping + loopback browser visually compares only the baseline/quality-shadow selections for changed scenarios. It verifies immutable proxy SHA-256 and bytes, saves private blinded choices and has a **Finish & Report** button. The local runner outputs only aggregate comparison preferences. No API/collection/snapshot writes.

## Single batched Windows local verification

Run after CI passes and the full PR is merged. Reuse your existing local app/console, the PostgreSQL connection and durable proxy root/profile if you opt into browser review.

Capture **at least one existing Creative preview** at a larger target (for example, 80) from the current running Photo Identity app; substitute your real local API base URL and an existing Smart Collection GUID. Capturing a preview is a GET and does **not** create or mutate a collection/snapshot.

~~~powershell
cd "C:\Kod\codex\Photo Identity Indexer"
git switch main
git pull --ff-only origin main

# Substitute actual local Photo Identity API base URL and existing collection GUID
$apiBase = "http://127.0.0.1:<your-api-port>"
$collectionId = "<existing-smart-collection-guid>"
$uri = "$apiBase/api/smart-collections/$collectionId/creative-preview?targetCount=80"

$preview = Invoke-RestMethod -Method Get -Uri $uri
$preview | ConvertTo-Json -Depth 80 |
  Set-Content private/wi0195-creative-preview.json -Encoding utf8

.\tools\PhotoIdentity.PhotoQualityEvaluation\run_collection_shadow_simulation.ps1 `
  -PreviewInput private/wi0195-creative-preview.json `
  -ShadowTargetCount 20
~~~

The PowerShell runner runs its offline synthetic tests and source/score consistency checks, imports the private captured preview, creates/reuses immutable private scenario/score-blind/audit files, and prints an **aggregate-only JSON report**. No browser or new scoring is needed for preparation. It does not overwrite any existing files. If your selected Creative preview has fewer than 21 selected candidates, capture a larger pool or use a smaller `-ShadowTargetCount`; do not modify original source files.

**Important coverage limitation:** WI-0193 score exports cover only **600 pre-existing research photos** out of roughly 27,050 current proxy-backed photos. A real collection will often have zero or too few matching scores. In that event, report the aggregate; there is **no automatic full-archive rescore** and no user review needed. Do not use human quality review labels to fill missing model scores. A separate rights/runtime/backfill research decision would be needed.

If the aggregate reports changed selections **and** you want to inspect them with existing current review proxies, run:

~~~powershell
$env:PhotoIdentity__ReviewProxyProfileId = "jpeg-1600-q78"

.\tools\PhotoIdentity.PhotoQualityEvaluation\run_collection_shadow_simulation.ps1 -Review
~~~

That option invokes the read-only PostgreSQL proxy mapper only for the locally captured photo identities and opens a browser on `http://127.0.0.1:5186`. Prefer A / Prefer B / No preference / Unsure; keyboard 1–4. Click **Finish & Report** when finished; the local server exits and prints a private-audit-bound **aggregate vote count**. The scoreblind UI does not tell you which arm is baseline, and its revision IDs, source files, model evidence and choices remain in untracked `private/` files. Multiple sessions can resume; the reporter never publishes raw photo IDs, pair labels or model scores.

**Report back:** the aggregate simulation JSON (scenario/source counts, score coverage, selection changes, disabled policy arms), a brief test PASS/FAIL, and—only if any meaningful selection changes were reviewed—the aggregate blinded preference count. Do **not** share the Creative preview, blind lists, proxy paths, scores or private review file.

## Acceptance/disposition boundary

The original WI-0195 full four-arm acceptance criteria are **not satisfied** by this narrower probe and must not be marked complete. The only allowed end decision from this research phase is whether a **separate, fully licensed/covered and independently evaluated quality-preference product experiment** merits work, versus a no-go/defer. Very Low exclusion and automatic redundancy/combined suppression remain **NO-GO** unless their separate evidence gates are satisfied. No saved collection changes, Archive/Search/Smart/Creative APIs, slideshow snapshots, derived quality catalogue persistence, source originals, or OneDrive hydration occur.

## Bounded score-covered collections: final M37 research phase (2026-10-20)

The maintainer's first merged WI-0195 simulation on an 80-photo Creative
shortlist (20 target slots) **PASSED 8/8 tests** and reported **0 existing
TOPIQ scores / 80 candidates, 267 missing-score comparisons, 0 changed
selection slots and 0 production changes**. This establishes simulator
integrity, **not** that quality preference is useful or useless. The 600
WI-0193 benchmark scores do not overlap arbitrary collections sufficiently
for a representative human comparison. Avoid sampling more unscored
collections or repeating unchanged A/B review.

The next development batch supplies a **one-shot, bounded local score-coverage
bridge** and an auditable end-of-milestone decision ledger
(`docs/delivery/milestones/M37-decision-ledger.md`).

### Boundaries

- Map **at most 240 distinct current review proxies** from the private
  captured Creative/search scenario cohort; refuse stale/missing proxies.
  Only `jpeg-1600-q78`, no original reads, hydration or catalogue writes.
  No same-person/time restriction is used as a quality proxy.
- The default command runs offline synthetic tests, maps the existing
  immutable review-proxy locators *read-only* and prints a **no-inference**
  plan: model-score overlap, missing photo count and approximate CPU time
  using historical ~4.423 seconds/photo median.
- If the operator explicitly authorizes third-party model use,
  `-Score -AcceptResearchOnlyThirdPartyWeights` permits **at most 120 NEW
  photo scores**, reusing exact-match previously exported TOPIQ scores
  and preserving the frozen version `pyiqa 0.1.16` and score-gap
  `0.16755089163780212`. All hashes/lengths are checked before model
  initialization. This action **may download third-party weights** via
  `pyiqa.create_metric`; checkpoint identity and rights have *not* been
  independently approved for commercial/production redistribution.
  **Do not take permission to download/use weights for granted.** Keep
  scoring explicitly opt-in and strictly private/research-only.
- The output is a **new private score export**, bound to the captured
  scenario, proxy map and original frozen score export; separate from
  `private/topiq_nr-scores-600.json` and never overwriting benchmark data.
  No human labels, challenge ratings or previously consumed WI-0193
  held-out quality judgments are opened; no model tuning or reranking
  relative to archive percentiles.
- The runner then uses the **already implemented** constrained original
  Creative selection vs score-shadow comparator, with no promotion
  across contiguous equal-kind/equal-existing-relevance-score runs.
  Baseline eligibility and target size are unchanged. Very Low exclusion,
  automatic redundancy and combined policy remain disabled/NO-GO.
- When selections change, **optional** hash-verified local blinded
  visual reviewer with Finish & Report saves private aggregate votes;
  identical results never require a human review. Use a batch ID to
  keep distinct captured scenarios and all artifacts separate, immutable
  and resumable.

### One local preflight round (no inference)

After merging the bounded-scoring PR into `main`, run on Windows:

~~~powershell
cd "C:\Kod\codex\Photo Identity Indexer"
git switch main
git pull --ff-only origin main

Test-Path "private/wi0195-collection-shadow-scenarios-v1.json"
Test-Path "private/topiq_nr-scores-600.json"

# Keep the known working connection/root from WI-0194; never paste secrets
[bool]$env:PhotoIdentity__Postgres__ConnectionString
Test-Path $env:PhotoIdentity__ReviewProxyRoot
$env:PhotoIdentity__ReviewProxyProfileId = "jpeg-1600-q78"

.\tools\PhotoIdentity.PhotoQualityEvaluation\run_bounded_collection_shadow.ps1
~~~

Expect synthetic unit tests to pass, `wi0195-private-candidate-proxies-v1`
reporting the count of existing current review proxies, and
`wi0195-bounded-research-quality-scores-v1` with
`status=plan-only-no-inference`, `inferencePerformed=false`,
`reusedKnownTOPIQScores`, `additionalCandidatePhotosNeedingScores`
and `plannedBatchFitsHardCap`.
**Share only this aggregate JSON.** We will decide whether the bounded
private opt-in inference is warranted from its actual scale and risks.
Do not rerun the one-shot WI-0193 held-out quality experiment, edit
the old private score export, or attempt a 27k-photo rescore.

### Only after explicit decision: bounded scoring and optional review

If the operator personally accepts the third-party weights/rights
implications and wants a research-only run:

~~~powershell
.\tools\PhotoIdentity.PhotoQualityEvaluation\run_bounded_collection_shadow.ps1 `
  -Score -AcceptResearchOnlyThirdPartyWeights
~~~

This reuses `-BatchId first` private outputs, refuses to overwrite scores
or blind/audit files, and reports coverage and selection differences. If
at least one changed-selection scenario exists, optionally:

~~~powershell
.\tools\PhotoIdentity.PhotoQualityEvaluation\run_bounded_collection_shadow.ps1 -Review
~~~

The A/B viewer uses port **5187** and a genuine Finish & Report button,
which exits the server and prints aggregate preferences. This is an
evaluation of *a candidate research experience*, not a deployment
authorization or a guarantee of broad archive effectiveness.

### Optional diverse real scenarios (one batch, no new code)

Use the existing read-only Creative preview GET on one or two additional
existing Smart Collections or search-anchored Creative recipes. Each
captured JSON remains under `private/`. Import multiple previews into a
**new, separately named** scenario file:

~~~powershell
$python = Join-Path $env:LOCALAPPDATA "PhotoIdentity\iqa-venv\Scripts\python.exe"
& $python tools/PhotoIdentity.PhotoQualityEvaluation/collection_shadow_simulator.py import-creative-preview `
  --input private/wi0195-creative-preview.json `
  --input private/wi0195-second-creative-preview.json `
  --target-count 20 `
  --output private/wi0195-two-collection-scenarios.json

.\tools\PhotoIdentity.PhotoQualityEvaluation\run_bounded_collection_shadow.ps1 `
  -Scenarios private/wi0195-two-collection-scenarios.json -BatchId second
~~~

The 240-mapped/120-new-photo hard caps still apply across the batch.
A new `-BatchId` ensures the first review or score files are never
replaced. Don't manipulate cohort identities to chase a desired score.
If the bound is exceeded, report the failed gate and choose fewer
scenarios **before** collecting human preferences; do not manufacture
model accuracy or tune against already consumed held-out labels.

### Final milestone decision

After this ONE bounded-scoring phase, record one aggregate go/no-go/defer
recommendation for the *research-only* quality-preference experience.
Very Low auto-exclusion, visual automatic suppression and their combined
policy remain **NO-GO**. If inference rights or meaningful score coverage
are unavailable, **DEFER** quality integration honestly. M37 can then be
closed as an **evaluation with documented rejected/deferred candidates**;
it must not be depicted as fulfilling the original four-approved-policy
experiment. New production-weight licensing, domain transfer, persistence,
full-archive backfill and UI work go into *separate future milestones*.
