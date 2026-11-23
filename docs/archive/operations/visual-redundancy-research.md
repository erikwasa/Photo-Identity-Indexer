# WI-0194 — archive-wide visual redundancy research

**Checkpoint 1: review-pair sampling protocol only.** No model selection, human pair review or archive-wide similarity export yet. This work is independent of the consumed WI-0193 human-quality held-out cohort: never reuse WI-0193 held-out labels.

## Labels and conservative decision

The human label is a **pair** decision: Redundant (normally only one wanted in a generated collection), Similar but keep both, Not similar, or Unsure. Exact file copies, transformed near copies and broad semantic resemblance remain distinct. Future automatic suppression needs >=95% observed precision together with conservative uncertainty reporting; false positives are more costly than retaining repetitive photos. Insufficient evidence means no-go or manual-only.

## Checkpoint 1 — offline blind-pair preparation

The standard-library Python file tools/PhotoIdentity.PhotoQualityEvaluation/redundancy_review_pairs.py accepts an **existing private** input manifest of photo identities and candidate similarity pairs. It DOES NOT compute hashes, CLIP embeddings or nearest neighbours; these are separate WI-0194 steps.

The private JSON input schema has five required keys: schemaVersion (1), protocol (wi0194-candidate-pairs-v1), sampleId (arbitrary private unique string), photos, pairs.

Each photos element has revisionId and a 64-hex-digit contentSha256; optional leakageGroup co-partitions known near duplicates.

Each pairs element has leftRevisionId, rightRevisionId, sourceKind (retrieved or random-negative); optional integer dhashDistance [0,64] and finite clipCosine [-1,1]. A retrieved pair must have at least one similarity measure. Unknown fields such as ratings/reviews, private paths and duplicate/reversed pairs are prohibited. Smaller dHash indicates more perceptual similarity; higher CLIP cosine suggests more shared visual/semantic content. The numeric scores do NOT establish redundancy labels.

The sampler unites known exact-content photos, supplied leakageGroup families and pairs with dHash distance <=4 before assigning a deterministic photo-disjoint hash split (approximately 75% calibration / 25% held-out at the family level). It never selects cross-partition pairs. Fixed bounded sample buckets: exact copy control, near-copy, high, medium, borderline, dHash/CLIP disagreement, and explicit random negatives. The bucket cutoffs are **research sampling heuristics only**: not precision evidence, calibrated thresholds or production policy. dHash<=4 grouping can overcluster; unlinked near copies can leak. Real candidate recall must be inspected before freezing any cohort.

Two **new private, exclusive-write** exports are produced. The blind manifest contains only pseudonymous pair IDs, revision references and label choices; it deliberately excludes scores, model provenance, bucket, partition and photo group. The private audit contains source fingerprints, bucket counts, photo partitions and evidence needed for a later controlled calibration. Console output contains aggregate counts only. Status is research-review-set-draft-not-frozen. No human pair labels may be accessed until a separate reviewer and freeze protocol exist.

### Synthetic-only local verification after merge

    cd "C:\Kod\codex\Photo Identity Indexer"
    git switch main
    git pull --ff-only origin main
    $python = Join-Path $env:LOCALAPPDATA "PhotoIdentity\iqa-venv\Scripts\python.exe"
    & $python -m unittest discover -s tools/PhotoIdentity.PhotoQualityEvaluation -p test_redundancy_review_pairs.py -v

The future CLI (NOT an instruction to run it on a real archive before candidate export and cohort review):

    & $python tools/PhotoIdentity.PhotoQualityEvaluation/redundancy_review_pairs.py `
      --input private/wi0194-neighbour-candidates.json `
      --blind-output private/wi0194-blind-pairs.json `
      --audit-output private/wi0194-pair-audit.json

Never commit or share private source, pair, score, review or audit exports. Preserve the independent WI-0194 held-out seal and do not tune after viewing held-out pair ratings.

## Checkpoint 2 — bounded current-catalogue candidate export (local 500/2,000/5,000 probes passed)

The new .NET command reads **current photo revisions** with existing durable review proxies from PostgreSQL. It verifies local proxy content hashes, computes the existing OpenCV 64-bit dHash, optionally reuses **exact-version** CLIP vectors already indexed for semantic search, and computes exact similarity neighbours without a date, moment, people, location or folder predicate. It never opens originals, triggers cloud hydration, downloads a model, generates new CLIP embeddings, runs a migration or changes the catalogue.

**Scope limits:** the safe default is **500** proxy-backed revisions, capped at **5,000**; this is only a deterministic archive-spread subset, not representative or archive-wide neighbour recall. Every within-subset dHash pair is examined, including all links with dHash Hamming distance at most four (for leakage protection only). Exact CLIP comparisons scan the indexed sampled corpus only for a bounded number of deterministic seeds. Neither scan proves accuracy or recall outside the sample. CLIP semantic similarity is not visual redundancy ground truth.

Use Windows PowerShell from the repository root, with the *existing* PhotoIdentity PostgreSQL connection and review-proxy root/profile environment settings used for the quality benchmark:

~~~powershell
git switch main
git pull --ff-only origin main
dotnet run --project tools/PhotoIdentity.PhotoQualityEvaluation -- redundancy export --output private/wi0194-neighbour-candidates.json --report private/wi0194-neighbour-metrics.json --max-photos 500 --neighbors 8
~~~

The command above intentionally tests dHash without CLIP. To include CLIP, first inspect the existing model ID, model SHA256 and preprocessing version values in the **authoritative** PostgreSQL photo_semantic_embeddings table; do not guess them or use a historical restore. Append all the following options to the same command:

~~~powershell
--clip-model-id "INDEXED_MODEL_ID" --clip-model-sha256 "EXACT_64_HEX_SHA256" --clip-preprocessing "INDEXED_PREPROCESSING_VERSION" --clip-seeds 100
~~~

All three provenance options must be provided together. CLIP indexing coverage in the sampled cohort is reported explicitly; zero coverage is not a model accuracy result. Existing CLIP generation timings and actual checkpoint size/license still require separate evidence before candidate selection.

Two **new, exclusively created** JSON files under private/ are produced: a candidate-pair manifest compatible with the checkpoint-1 blind sampler, and an aggregate measurements report. Outputs never overwrite existing files. The stdout report contains only aggregate counts, scan comparisons and timing, never identifiers or similarity values. The exported private pair evidence is **not** a human-reviewed set and **not** a frozen hold-out; inspect population/coverage and potential near-copy leakage before using the blind sampler. Never commit private exports.

### Local verification

~~~powershell
dotnet test tests/PhotoIdentity.Integration.Tests/PhotoIdentity.Integration.Tests.csproj --filter FullyQualifiedName~RedundancyCandidateExportTests
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
~~~

Share back only the aggregate stdout: proxy-backed population, sampled photos, CLIP coverage, scan counts, candidate-pair counts, timings, and whether known near-copies from different dates appear. Do not share paths, photo IDs, SQL credentials, full source manifests or per-pair scores. Real archive results have **not** been obtained in this checkpoint. No thresholds or production suppression authorized.


## Checkpoint 3 — guarded full-proxy-population exact feasibility probe

The 5,000-photo local benchmark reported 27,050 current proxy-backed photos, 100% CLIP-vector coverage, 12,497,500 dHash comparisons in 4.801 seconds, 499,900 exact CLIP comparisons in 0.821 seconds, 29,635 retrieved candidate pairs, and 49.564 seconds verifying and hashing proxies. This is enough to justify *one* guarded full-population experiment, not automatic suppression or ANN.

After merging the checkpoint-3 PR and updating main, run from your Windows PowerShell repo root with the same working PostgreSQL/review-proxy environment variables. This is opt-in and **not** part of normal application processing:

~~~powershell
git switch main
git pull --ff-only origin main
dotnet test tests/PhotoIdentity.Integration.Tests/PhotoIdentity.Integration.Tests.csproj --filter FullyQualifiedName~RedundancyCandidateExportTests

dotnet run --project tools/PhotoIdentity.PhotoQualityEvaluation -- redundancy export `
  --output private/wi0194-neighbours-full-candidates.json `
  --report private/wi0194-neighbours-full-metrics.json `
  --full-proxy-population `
  --neighbors 8 `
  --clip-model-id "openai/clip-vit-base-patch32" `
  --clip-model-sha256 "42b5dac8f0a73e45b1626ae7a69b41a4bc111c6fe1edafcceda8c2a5506dc6db" `
  --clip-preprocessing "clip-rgb-opencv-cubic-shortest-edge-center-crop-224-v1" `
  --clip-seeds 100
~~~

The new mode refuses `--max-photos` in combination with `--full-proxy-population`. It rejects inventory over 30,000 current proxy-backed photos, over 450 million exact dHash comparisons, or 16 GiB total declared proxy bytes **before image loading**. Stop the command if resources are constrained; never bypass these guards without another reviewed checkpoint. The command still reads and verifies every selected local proxy (potentially several GiB), exact-scans all dHash pairs, and scans CLIP for **only 100 deterministic seeds** against every compatible indexed vector. Allow enough disk for **new private output files**; existing exports are never overwritten.

Report back only the aggregate console metrics: current proxy-backed photo count, number sampled, CLIP coverage, total dHash/CLIP comparisons, candidate union/overlap/CLIP-only counts, strict dHash<=4 leakage links, proxy-verification and scan runtimes. If the result exits nonzero, report only the exception type printed (no credentials or private paths). **All-proxy-backed is not the same as all photos** when durable proxy coverage is incomplete.

Leave the private candidate JSON unopened for human review and do not run the blind sampler or calibrate thresholds until the leak-safe cohort protocol is separately reviewed. Even a full dHash scan and 100 CLIP seed searches do not prove visual redundancy or the unseeded CLIP recall of the archive.


## Checkpoint 4 — aggregate-only candidate/leakage preflight (after full-proxy evaluation)

The maintainer's current full-proxy-population probe succeeded: 27,050/27,050 current proxy-backed photo revisions, matching exact-fingerprint CLIP vectors on all 27,050, 365,837,725 exact dHash pairs scanned in 25.911 seconds, 2,704,900 exact CLIP seed comparisons in 4.514 seconds, 218.889 seconds proxy verification/hashing and 4.086 seconds PostgreSQL read. The union retrieved **162,829 candidate pairs** (162,092 dHash candidates plus 737 CLIP-only); the 100 CLIP seeds found 799 pairs, 62 overlapping dHash. All 2,495 dHash<=4 links are within the selected proxy-backed population; no claims are made about their human redundancy truth. The 200 random-negative controls are screened samples.

**Technical decision:** offline full-population exact dHash is practical; no ANN dependency currently justified. **Research decision:** neither dHash nor CLIP candidate retrieval has redundancy labels, precision or true copy-recall evaluation. The seed-only CLIP evidence does not prove complete CLIP archive retrieval. Next, inspect leakage families and review-bucket feasibility **without viewing or freezing human reviews**.

After merging checkpoint-4 preflight development, run the synthetic tests and aggregate-only analysis from the Windows PowerShell repository root:

~~~powershell
git switch main
git pull --ff-only origin main

$python = Join-Path $env:LOCALAPPDATA "PhotoIdentity\iqa-venv\Scripts\python.exe"
& $python -m unittest discover -s tools/PhotoIdentity.PhotoQualityEvaluation -p test_redundancy_candidate_preflight.py -v

& $python tools/PhotoIdentity.PhotoQualityEvaluation/redundancy_candidate_preflight.py `
  --input private/wi0194-neighbours-full-candidates.json
~~~

The preflight **reads only the existing private candidate manifest** and writes **no files**. It rejects extra fields such as labels and paths, and prints only aggregate counts:
- photo-disjoint calibration/held-out candidate inventory sizes, deterministic leakage-family size distribution, exact-content linked families;
- within/cross-partition candidate counts and warnings for cross-partition dHash distances **5–8**, **9–12**, or CLIP cosine **>=0.90** (sampling risk indicators **not** confirmed near-copy labels or exclusion thresholds);
- seven candidate sampling buckets per partition and quota shortfalls.

The CLI outputs `candidate-cohort-preflight-only-not-frozen` and explicitly records that human labels were not read, thresholds were not fit, no held-out evaluation was performed and no production rule was approved. Neither the input nor any pair-level scores, identifiers or content paths may be pasted into GitHub or ChatGPT. Share **only the aggregate stdout JSON**. If a command fails, report the sanitized exception type and do not run any other sampler. **Do not run `redundancy_review_pairs.py` or collect any human labels yet.** Preflight can flag linked or plausible near-copy hazards, but absence of hazards does not prove no leakage, as only retrieved pairs are visible. A separate cohort freeze/reviewer authorization remains required.

## Checkpoint 5 — leakage-family linkage sensitivity (development checkpoint, local evaluation pending)

The checkpoint-4 maintainer preflight passed all 4 synthetic tests and read the **existing private full-proxy candidate manifest** with 27,050 photos, 162,829 retrieved pairs and 200 screened random negatives. Its deterministic dHash<=4 photo-disjoint split had 20,149 calibration and 6,901 held-out photos, 25,107 leakage families (largest 37), and **0 exact-content or dHash<=4 linked pairs crossing partitions**. However **882 dHash5–8**, **3,867 dHash9–12**, and **1,596 CLIP cosine >=0.90** candidate pairs crossed that split; categories may overlap and are not confirmed near-copy or redundancy labels. The proposed held-out review quotas were short **41 borderline** and **25 random negatives**, while both partitions lacked exact-copy controls (**12 each**). Human review should not start on this basis.

The new `redundancy_partition_sensitivity.py` is an independent, **read-only** comparison of three predeclared photo-disjoint *leakage grouping* scenarios: exact-content/supplied-family plus linked dHash distances **<=4** (current baseline), **<=8**, or **<=12**. It does NOT change the maintained sampler, export new candidate pairs, assign redundancy labels, or select a production cutoff. It validates the entire private input using the existing review-sampler validator, then prints only aggregate group concentration, partition sizes, cross-boundary candidate risk indicators and per-bucket quotas. Because whole families get a deterministic partition assignment, any grouping expansion can move families between calibration and held-out; raw partition counts across scenarios are not unbiased model effectiveness comparisons.

After merging the PR, run from the repository root on Windows PowerShell:

~~~powershell
git switch main
git pull --ff-only origin main
$python = Join-Path $env:LOCALAPPDATA "PhotoIdentity\iqa-venv\Scripts\python.exe"

& $python -m unittest discover `
  -s tools/PhotoIdentity.PhotoQualityEvaluation `
  -p test_redundancy_partition_sensitivity.py -v

& $python tools/PhotoIdentity.PhotoQualityEvaluation/redundancy_partition_sensitivity.py `
  --input private/wi0194-neighbours-full-candidates.json
~~~

Share **only the aggregate JSON stdout**; do not share private paths, revision IDs, pair-score rows or the candidate JSON. If any test fails, stop before running the research diagnostic. There are **no output files**, labels, model inference, proxy hashing, originals or database operations. The 4/8/12 distances are partition-isolation **sensitivity bounds**, not suppression thresholds or proof that co-grouped photos are redundant. CLIP>=0.90 is reported as a potential residual boundary risk, but never automatically treated as family identity. Absence of observed cross-boundary edges cannot exclude unobserved near copies; the existing CLIP candidate retrieval still uses only 100 seeds.

**Do not run the blind review sampler, collect human labels, freeze a holdout, choose model thresholds, or modify collection behavior.** If sensitivity grouping forms giant components or cannot support balanced held-out quotas, revise the independent reviewer/cohort protocol in a later checkpoint before authorizing any labeling.

## Checkpoint 6 — opt-in research-candidate enrichment

The 27,050-photo partition sensitivity reported no safe new grouping policy: dHash<=4 largest family 37 and held-out borderline 19/60; dHash<=8 largest family 305 with the same quota gaps; dHash<=12 largest family **3,368** and held-out borderline 15/60. Held-out screened controls were 10/35 and real exact-content-copy controls 0 throughout. These cutoffs were family-linkage *stress tests*, not calibrated redundancy classification or automatic suppression decisions. No-go for freezing the proposed human review cohort.

Two *optional* existing-evidence exporter flags address **candidate supply only**: `--clip-borderline-per-seed 16` seeks at most 16 deterministic extra candidate pairs per CLIP query seed with cosine in [0.62, 0.78) and dHash>24; `--random-negative-controls 800` attempts additional screened control candidates (dHash>=25 and indexed CLIP cosine<0.70). Neither kind is human-confirmed truth. Defaults remain unchanged without flags. CLIP 100-seed coverage is not full-archive CLIP pair recall. Strict dHash<=4 group relationships are unaffected by added dHash>24 candidate pairs.

After merge, run on Windows PowerShell from the repository root with the previously working PostgreSQL connection and review-proxy environment:

~~~powershell
git switch main
git pull --ff-only origin main
dotnet test tests/PhotoIdentity.Integration.Tests/PhotoIdentity.Integration.Tests.csproj --filter FullyQualifiedName~RedundancyCandidateExportTests

dotnet run --project tools/PhotoIdentity.PhotoQualityEvaluation -- redundancy export `
  --output private/wi0194-full-enriched-candidates.json `
  --report private/wi0194-full-enriched-metrics.json `
  --full-proxy-population `
  --neighbors 8 `
  --clip-model-id "openai/clip-vit-base-patch32" `
  --clip-model-sha256 "42b5dac8f0a73e45b1626ae7a69b41a4bc111c6fe1edafcceda8c2a5506dc6db" `
  --clip-preprocessing "clip-rgb-opencv-cubic-shortest-edge-center-crop-224-v1" `
  --clip-seeds 100 `
  --clip-borderline-per-seed 16 `
  --random-negative-controls 800

$python = Join-Path $env:LOCALAPPDATA "PhotoIdentity\iqa-venv\Scripts\python.exe"
& $python tools/PhotoIdentity.PhotoQualityEvaluation/redundancy_candidate_preflight.py `
  --input private/wi0194-full-enriched-candidates.json
~~~

The full export will again read and validate **every existing local proxy** before exact matching, so do not run when resources are constrained. It preserves current 30k-photo / 450-million-pair / 16-GiB guards, writes new private-only files and never changes photos, search, collections, database, models or cloud hydration. Stop on test or export failures. Share **only both aggregate JSON stdout reports**, not either private manifest, photo IDs, per-pair scores or credentials. Expect no guarantee of quota completion because held-out controls require both endpoints within that partition and the existing 100 CLIP seeds may not discover sufficient borderline pairs.

Even if quotas improve, **do not run the blind sampler, inspect held-out review labels, freeze a cohort or calibrate suppression thresholds**. Genuine exact-copy controls remain absent; never fabricate them. Additional independent leakage and sampling-protocol review is required.


## Checkpoint 7 — held-out risk quarantine sensitivity (aggregate-only, no review)

The maintainer's merged checkpoint-6 enriched run succeeded: 27,050/27,050 current proxies and compatible indexed CLIP vectors; 164,381 retrieved pairs, 800 screened random controls, 1,552 new measured borderline pairs. The original strict dHash<=4 photo-disjoint families and candidate leakage warnings are unchanged. The held-out **borderline** category increased **19 to 129** (quota 60), and screened **random-negative** candidates increased **10 to 51** (quota 35). Genuine exact-content copies are absent; **no source-archive exact-copy controls are available**. Candidate counts are not human redundancy labels.

The remaining question is whether keeping a held-out cohort isolated from *observed* cross-partition near-copy risk candidates would leave enough unlabelled review cases. `redundancy_heldout_quarantine_preflight.py` compares four read-only sensitivity scenarios on the **same unchanged existing dHash<=4 split**:

1. No held-out quarantine (baseline).
2. Exclude held-out photos incident to observed cross-partition dHash5–8 candidate pairs.
3. Exclude held-out photos incident to observed cross-partition dHash5–12 candidate pairs.
4. Exclude held-out photos incident to observed dHash5–12 **or CLIP cosine >=0.90** cross-partition candidate pairs.

The tool does **not** write a manifest or alter/approve any review set. Output is aggregate counts: held-out photos excluded/retained, within-held-out candidate pair supply and per-bucket eligibility/shortfalls, and risk-edge counts. No actual cross-boundary photo identities, paths or pair scores are printed. The cutoffs are **research-only exclusion sensitivities**, not proven duplicate cutoffs, a validated leakage-safe protocol, or suppression thresholds. High CLIP cosine often indicates semantic overlap rather than copy identity; selectively removing those photos can create held-out selection bias. Missing candidate edges remain unknown, as 100 CLIP query seeds cannot establish archive recall.

After merging the checkpoint-7 PR, run from repository root with the same private **enriched candidate manifest** (no proxy reads, no database access):

~~~powershell
git switch main
git pull --ff-only origin main
$python = Join-Path $env:LOCALAPPDATA "PhotoIdentity\iqa-venv\Scripts\python.exe"

& $python -m unittest discover `
  -s tools/PhotoIdentity.PhotoQualityEvaluation `
  -p test_redundancy_heldout_quarantine_preflight.py -v

& $python tools/PhotoIdentity.PhotoQualityEvaluation/redundancy_heldout_quarantine_preflight.py `
  --input private/wi0194-full-enriched-candidates.json
~~~

**Share only the aggregate JSON stdout**. Never share private manifests, photo/revision identifiers, scores or archive credentials. Stop if tests fail. This does not freeze a cohort or collect human review; do not run `redundancy_review_pairs.py`, tune a model, assign held-out labels, or change production suppression. Preserve the private earlier manifests for audit/reproducibility. WI-0194 remains in progress, WI-0195 blocked.


## Checkpoint 8 — calibration-only similarity-risk pilot feasibility (no review)

The maintainer's private **enriched** 27,050-photo/165,181-pair cohort quarantine preflight found:
- Unmodified dHash<=4 split: 6,901 held-out photos; 11,843 within-held-out candidate pairs; 332 disagreement cases.
- Removing held-out photos incident to cross-partition dHash5–8 risks removes 470 photos but leaves only **38** disagreement candidates (quota 50).
- Removing dHash5–12 exposed photos removes 1,689 photos and leaves **7** disagreements.
- Removing dHash5–12 OR high-CLIP exposed photos removes 2,055 photos (29.8%) and leaves only **5** disagreements, 51 borderline (need 60) and 23 screened negatives (need 35).

Thus **blanket quarantine fails** quota/representativeness safeguards. This is not evidence of actual near-copy identity, and full CLIP neighbour recall remains unmeasured. Earlier <=8/12 global family grouping also formed giant components of 305/3,368 photos. No human-review cohort may be frozen yet.

**Next isolated diagnostic:** The new `redundancy_calibration_pilot_preflight.py` reuses the existing private enriched candidate manifest and strict baseline membership, but counts/chooses hypothetical pilot candidates **only where both photo endpoints are in calibration**. It reports aggregate eligibility in nine mutually exclusive measured-similarity categories, with fixed bounded illustrative quotas and a maximum of two pair occurrences per calibration photo. No blind review manifest is created; no photos, pair IDs or individual scores appear in output. Real exact-content-copy candidates, if any, have a proposed pilot quota of **zero** rather than fabricated duplicates. This diagnostic is *not* authority to view or label any photos.

After merging the PR, run from Windows PowerShell repository root:

~~~powershell
git switch main
git pull --ff-only origin main
$python = Join-Path $env:LOCALAPPDATA "PhotoIdentity\iqa-venv\Scripts\python.exe"

& $python -m unittest discover `
  -s tools/PhotoIdentity.PhotoQualityEvaluation `
  -p test_redundancy_calibration_pilot_preflight.py -v

& $python tools/PhotoIdentity.PhotoQualityEvaluation/redundancy_calibration_pilot_preflight.py `
  --input private/wi0194-full-enriched-candidates.json
~~~

Share **only the aggregate JSON stdout**. Stop if tests fail. This runs on existing local JSON: no full archive scan, database/proxy access, model download, originals/cloud hydration, file writes, human labels, split freeze or product change. Do not run `redundancy_review_pairs.py`, solicit any reviewer labels, or open held-out photos as part of a research set. The subsequent review protocol requires an explicit separate decision on privacy, pilot authorization, reviewer workflow, absent exact-copy controls, and the isolation of a truly independent held-out evaluation. WI-0194 remains in progress, WI-0195 blocked.


## Bundled WI-0194 calibration-pilot workflow — one local verification round

The archive-wide and candidate-enrichment phases are already complete. In response to operator feedback, the next development release includes the **entire calibration-only pilot toolkit**: deterministic blinded pair selection, read-only durable-proxy mapping, a local side-by-side browser reviewer with autosave/resume, validation tests, and a bound aggregate analysis. There is **no further required one-command research checkpoint between each tool**.

**Prerequisites:** merged integrated-pilot PR on `main`; the existing private `private/wi0194-full-enriched-candidates.json` (27,050 photos / 165,181 pairs), the known working PostgreSQL connection, the local review proxy root/profile `jpeg-1600-q78`, and the existing `%LOCALAPPDATA%\PhotoIdentity\iqa-venv\Scripts\python.exe`. The private source data, human labels and images must remain local/untracked. Do not regenerate or replace either full-archive candidate manifest.

**One-shot preparation and code verification** (safe; this **does not** start human review or access held-out labels):

~~~powershell
cd "C:\Kod\codex\Photo Identity Indexer"
git switch main
git pull --ff-only origin main

.\tools\PhotoIdentity.PhotoQualityEvaluation\run_redundancy_calibration_pilot.ps1
~~~

The script runs synthetic pilot privacy, deterministic pairing, label-binding, proxy-integrity and resume tests. Then it **only once** creates three new private files, reusing them on later runs:

- `private/wi0194-calibration-pilot-blind-v1.json`: exactly 200 unlabelled calibration-calibration pairs, randomized/blinded order, two revision IDs each, **no scores, strata, model or partition**;
- `private/wi0194-calibration-pilot-audit-v1.json`: separate private source/stratum evidence and fingerprints, no held-out pairs;
- `private/wi0194-calibration-pilot-proxies-v1.json`: exact-pilot-bound, verified current review-proxy metadata from a **read-only** PostgreSQL lookup for only the ~382 selected calibration photos. Actual proxy SHA and lengths are checked when images are requested by the reviewer.

The preparation result should report approximately **200 calibration-only pilot pairs / 382 distinct calibration photos / 0 held-out**, provided the same current archive is available. If the CLI reports that proxies have become missing or stale, do not silently substitute or regenerate originals: stop and report the error type. Prepared private files cannot be overwritten; the script refuses partial file pairs, and the reviewer rejects incompatible fingerprints. Files are never uploaded or committed. Preparation **does not** constitute authorization to freeze the held-out study.

**Optional local calibration-only visual pilot** (after deciding to collect human calibration labels; no held-out evaluation):

~~~powershell
.\tools\PhotoIdentity.PhotoQualityEvaluation\run_redundancy_calibration_pilot.ps1 -Review
~~~

This opens a loopback `127.0.0.1:5184` side-by-side reviewer. You may choose one of four blinded human labels: **Redundant** (normally select one), **Similar but keep both**, **Not similar**, and **Unsure**. Use keys 1–4 or buttons; optional notes; navigate or skip to next unreviewed. The reviewer displays neither source path, dHash, CLIP score, model origin, stratum, calibration/held-out marker nor archive-quality study content. The images are **existing immutable-hash-verified local JPEG proxies only**. The first saved review creates `private/wi0194-calibration-pilot-labels-v1.json`; subsequent reviews use atomic file replacement. Stop with Ctrl+C and rerun the same `-Review` command whenever convenient; prior labels and notes are validated and resumed. The default command without `-Review` never starts labeling.

After reviews, this same script prints a sanitized **calibration-only** report by candidate stratum. It can also be run directly:

~~~powershell
$python = Join-Path $env:LOCALAPPDATA "PhotoIdentity\iqa-venv\Scripts\python.exe"
& $python tools/PhotoIdentity.PhotoQualityEvaluation/redundancy_pilot_package.py report `
  --blind private/wi0194-calibration-pilot-blind-v1.json `
  --audit private/wi0194-calibration-pilot-audit-v1.json `
  --labels private/wi0194-calibration-pilot-labels-v1.json
~~~

Only **aggregate stdout** may be shared: number reviewed/remaining; bucket counts of redundant, similar-keep-both, not-similar and unsure; descriptive intervals. Never paste/commit private manifests, proxy maps, review labels/notes, photo identities or individual scores. **Calibration does not establish held-out precision/recall, an accepted dedicated redundancy model or a production suppression threshold.** Prior high-CLIP similarity might be semantic, unobserved near duplicates remain unknown, and the baseline dHash<=4 split is *not* frozen as leakage-safe. Genuine exact-copy controls are unavailable; no synthetic duplicates are passed off as real archive controls. The already-consumed WI-0193 held-out quality labels are out of scope.

After this **single bundled operator round**, evaluate the meaningful human redundancy outcomes and decide whether to pursue a separate comprehensive independent held-out/model phase, or document **NO-GO / manual-only / defer** on automatic suppression and move to milestone disposition. WI-0195 remains blocked until an actual accepted policy or explicit milestone no-go decision; no Search/Smart/Creative/Slideshow semantics are changed here.


## Bundled independent validation phase — after 200 calibration reviews

The maintainer finished the complete **200/200** labelled calibration-only pilot from PR #616 (382 distinct calibration photos, 0 held-out photo labels). The *selected* source buckets had 82 Redundant, 17 Similar-keep-both and 101 Not similar overall. In sampled buckets: dHash0–4 **23/24 Redundant**, dHash5–8 **23/24**, dHash9–12 **8/24**, high CLIP other **18/20**, dHash-far/CLIP-high disagreement **9/20**, close-dHash/CLIP-low **0/20** and neither CLIP-mid borderline nor screened negative controls contained any Redundant cases. These are **nonrepresentative, photo-correlated calibration labels**, not a precision/recall estimate and **not** grounds for production suppression. Global dHash<=8 or <=12 *transitive families* were already rejected after 305/3,368-photo components. All original exact-content-copy controls were absent.

This PR bundles the next independent study **without additional single-command development checkpoints**. It establishes a *new frozen-before-labels* held-out candidate list, with strict guard:

- Reuses the exact same private enriched candidate archive and existing `private/wi0194-calibration-pilot-blind-v1.json`. It **never opens calibration labels, notes or private WI-0193 quality-study labels**. The calibration blind contains the 382 already-reviewed photo endpoints.
- Retains only pairs where both endpoints are in the original dHash<=4 **held-out** membership. A held-out photo is excluded if a *previously retrieved cross-partition edge* connects it to any of those **382 calibration-pilot photos** with dHash distance **5–12** or CLIP cosine **>=0.90**. Those cutoffs are **predeclared guard sensitivities**, not confirmed near-copy labels or classification policies. Stronger unobserved leakage remains possible because dHash neighbour retrieval is bounded and CLIP retrieval used only 100 seeds.
- Stops **before creating review files** if the observed-risk quarantine exceeds 20% of held-out photos, or if the diversity-capped held-out review pool has fewer than 100 pairs. Sampling is deterministic using a new independent seed with the **nine predeclared candidate categories** and at most two pair appearances per photo. Individual category quotas may be smaller than the 200-pair calibration pilot; report those shortfalls rather than manufacturing cases. No exact-content controls are invented.
- On a passing gate, writes only **new, exclusive-write private** blind and audit outputs and binds them by SHA-256. A read-only .NET proxy locator **requires the separate frozen held-out audit** before mapping the selected revisions. The loopback browser refuses held-out photos unless invoked with explicit `--heldout-validation` (the local entry point adds this only for the `-Review` switch).
- Held-out labels live in a **distinct private file** and are resumable. Aggregate-only reporting produces per-bucket Redundant / Similar-keep-both / Not similar / Unsure counts and descriptive Wilson bounds; unlike calibration, it must not be consulted to change the candidate grouping, evaluate another cutoff/model on the same data, or retrospectively replace the held-out cohort. No suppression threshold is frozen and no production policy is approved.

**After CI passes and merging the full bundled PR, verify/prep once** in Windows PowerShell from the repository root:

~~~powershell
cd "C:\Kod\codex\Photo Identity Indexer"
git switch main
git pull --ff-only origin main
$env:PhotoIdentity__ReviewProxyProfileId = "jpeg-1600-q78"

.\tools\PhotoIdentity.PhotoQualityEvaluation\run_redundancy_independent_validation.ps1
~~~

This runs synthetic regression tests **and** the real candidate-only leakage gate, then resolves current durable local proxies (read-only DB; only selected photos, not a new 27k scan). **No held-out image is opened for review, and no human label is saved by default.** If the guard fails, stop; keep private manifests and share the sanitized failure type only. If it passes, the stdout includes aggregate frozen cohort size, held-out photos quarantined, selected bucket counts, and proxy matches. Share **only that aggregate output** before authorizing the independent review. This is the single planned local verification round for the new phase.

**When ready to label the guarded held-out cohort**, run the same entry point with `-Review`:

~~~powershell
.\tools\PhotoIdentity.PhotoQualityEvaluation\run_redundancy_independent_validation.ps1 -Review
~~~

This opens a separate loopback reviewer on `http://127.0.0.1:5185`. Four blind labels and notes are identical to the calibration pilot. Ctrl+C stops; re-running resumes. The script prints an aggregate report after the reviewer exits, or can be run without `-Review` to report saved progress. Do not open calibration labels for tuning, upload private held-out manifests/individual scores or manually alter the frozen review population after any held-out labels are collected. No automatic collection/Slideshow changes are made.

**Interpretation gate:** This is still a *stratified, observed-risk-filtered* held-out descriptive experiment, not unbiased whole-archive automatic-suppression precision or a verified production threshold. A 95% point estimate on ~24 correlated examples would be too weak; any final recommendation must explicitly discuss photo-level dependence, selection bias, missed retrieval, dedicated-model/license/runtime uncertainty, and false-positive harm. If evidence remains inadequate, record a clear **no-go/defer** for automatic suppression rather than extending this micro-checkpoint cycle. WI-0195 depends on an accepted or documented no-go disposition and is not automatically unblocked by this experiment.

## Next checkpoints

1. Run the bounded catalogue export; inspect aggregate coverage, proxy bytes, comparison timings and known cross-date candidates. Do not infer archive-wide recall from a 500-photo sample. Expand only after measured resource review.
2. Measure full-corpus exact-search practicality, dHash leakage link recall, indexed CLIP coverage and true model/checkpoint provenance/size; assess licensed dedicated near-copy or visual-redundancy candidates.
3. Freeze and inspect the independent WI-0194 leakage-safe blind pair cohort before any human review; start calibration-only review, separately authorize held-out evaluation only after threshold policy freeze.
4. Report false-positive suppression errors and conservative precision uncertainty. A >=95% observed-precision point estimate alone does not constitute production approval.
5. Make a no-go/conditional research decision before considering WI-0195. No production collection/Slideshow changes in M37.
