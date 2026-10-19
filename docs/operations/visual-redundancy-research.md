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

## Next checkpoints

1. Run the bounded catalogue export; inspect aggregate coverage, proxy bytes, comparison timings and known cross-date candidates. Do not infer archive-wide recall from a 500-photo sample. Expand only after measured resource review.
2. Measure full-corpus exact-search practicality, dHash leakage link recall, indexed CLIP coverage and true model/checkpoint provenance/size; assess licensed dedicated near-copy or visual-redundancy candidates.
3. Freeze and inspect the independent WI-0194 leakage-safe blind pair cohort before any human review; start calibration-only review, separately authorize held-out evaluation only after threshold policy freeze.
4. Report false-positive suppression errors and conservative precision uncertainty. A >=95% observed-precision point estimate alone does not constitute production approval.
5. Make a no-go/conditional research decision before considering WI-0195. No production collection/Slideshow changes in M37.
