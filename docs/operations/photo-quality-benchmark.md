# Representative photo-quality benchmark

WI-0192 replaces the deterministic WI-0182 convenience sample with a stratified benchmark for M37. This benchmark is still evaluation-only: it does not persist quality scores, change collection/search behavior, hydrate originals or treat quality as canonical archive truth.

## Checkpoint 1: generate the representative core

Use the maintained PostgreSQL catalogue and durable review-proxy profile:

~~~powershell
$env:PhotoIdentity__Postgres__ConnectionString = "<maintained catalogue connection string>"
$env:PhotoIdentity__ReviewProxyRoot = "<review proxy root>"
$env:PhotoIdentity__ReviewProxyProfileId = "jpeg-1600-q78"

dotnet run --project tools/PhotoIdentity.PhotoQualityEvaluation -- benchmark `
  --output private/photo-quality-benchmark.json `
  --representative-count 400 `
  --challenge-reserve 200
~~~

Pass `--force` only when intentionally regenerating the private export. The benchmark selection itself is deterministic for a fixed current catalogue/profile/policy, while `generatedAtUtc` naturally changes.

The output contains private source keys, derivative paths and future human labels. Keep it under `private/`, `data/`, `staging/` or an absolute path outside the repository. Do not commit it.

## Representative selection policy

`m37-stratified-marginal-balance-v1` selects only current nondeleted **photo** revisions that already have the requested durable proxy. It balances the selected marginal distribution across:

- capture decade, using extracted local capture time first and a year-prefixed source segment only as fallback;
- camera/device family from persisted camera make/model, with an explicit unknown bucket;
- media family (JPEG, HEIC/HEIF, PNG, DNG/raw, WebP, other);
- original-pixel resolution bucket;
- portrait/landscape/square/unknown orientation from original dimensions; and
- detected-face count bucket (none, one, multiple).

This is intentionally different from WI-0182's stable-hash-only slice. The export includes both population and representative coverage counts so the maintainer can inspect whether important archive strata are actually present before human review starts.

The policy does not infer indoor/night semantics from filenames or camera metadata. The reserved challenge tranche will cover hard visual/model cases separately rather than pretending those semantics are catalogue facts.

## Frozen calibration / held-out split

The selected representative core is split using `m37-calibration60-heldout40-leakage-groups-v1`.

Before assigning the 60/40 split, the tool verifies the selected proxy bytes and computes the existing `opencv-dhash64-9x8-gray-v1` fingerprint. Revisions with identical content SHA-256, and selected photos connected by strict dHash Hamming distance <= 4, are kept in the same partition.

That dHash step is only a leakage-control heuristic. It is **not** the M37 visual-redundancy evaluation and does not create production duplicate/similarity evidence.

The split is frozen by the private benchmark manifest. WI-0193 must tune candidate quality-model calibration only on the calibration partition and use the held-out partition for the final evaluation.

## Challenge reserve

Checkpoint 1 records a default reserve of 200 challenge photos but leaves `challengeCount = 0`. A later WI-0192 checkpoint will add challenge selection (including rare/edge cases and candidate-model disagreement support) without removing or silently replacing the representative core.

## Human labels

The manifest already reserves blank fields for:

- quality level;
- `personallyImportant`; and
- notes.

Do not fill these by inspecting model scores. The next checkpoint adds the blinded resumable review surface and defines the exact Very Low / Low / Normal / High / Very High / Unsure review flow.

## Privacy and safety

- Originals remain immutable and are not hydrated for benchmark generation.
- Only existing durable review proxies are read.
- The production catalogue is not mutated.
- No private source key, proxy path, image, model output or human label is committed.
- A Very Low label is evaluation evidence only; it does not delete, hide or exclude anything in WI-0192.


## Checkpoint 2: blinded review and challenge imports

Start the local browser reviewer after generating the representative core:

~~~powershell
dotnet run --project tools/PhotoIdentity.PhotoQualityEvaluation -- benchmark review `
  --input private/photo-quality-benchmark.json
~~~

The reviewer binds only to `127.0.0.1` and uses a per-process random session token for API writes. It displays the review proxy plus the review controls, but intentionally does **not** expose source path, archive stratum, calibration/held-out partition, candidate/model reasons, or representative/challenge membership.

Keyboard shortcuts:

- `1` Very Low
- `2` Low
- `3` Normal
- `4` High
- `5` Very High
- `6` Unsure
- `P` toggles the separate personally-important flag
- Left/Right navigate
- `Ctrl+Enter` re-saves the current label after editing notes

Each label save writes the private manifest atomically. The first save creates `<manifest>.before-review.bak`; subsequent saves do not overwrite that backup. Restarting the command resumes from the labels already stored in the manifest. Review order is a deterministic benchmark-specific SHA-256 shuffle, so manifest order cannot reveal whether the current item is part of the representative or challenge tranche.

The labels mean:

- **Very Low** — technically/presentationally poor enough that you normally would not want the photo selected automatically. This is evaluation evidence, not deletion.
- **Low** — clearly inferior but still potentially usable.
- **Normal** — ordinary acceptable archive photo.
- **High** — clearly better than normal and desirable in presentation.
- **Very High** — exceptional presentation quality.
- **Unsure** — you cannot confidently place the photo.

Judge image/presentation quality, not whether you like the people, event, place, or memory. `personallyImportant` stays separate for exactly that reason.

### Append challenge photos without changing the representative core

Checkpoint 2 defines a private interchange format for later challenge generators. A challenge file has schema version 1, the benchmark ID, a versioned `selectionPolicy`, and full private photo/proxy metadata for candidate revisions. Candidate reasons can identify rare strata, likely failure modes, score extremes, or model disagreement.

Append such a file with:

~~~powershell
dotnet run --project tools/PhotoIdentity.PhotoQualityEvaluation -- benchmark add-challenges `
  --input private/photo-quality-benchmark.json `
  --candidates private/photo-quality-challenges.json
~~~

The command rejects benchmark-ID mismatches, duplicate/overlapping revisions, and additions beyond the reserved challenge count. Added photos are forcibly marked `sampleKind=challenge` and `partition=challenge`; any supplied human labels are cleared. The frozen representative calibration/held-out counts and membership do not change. A one-time `<manifest>.before-challenge.bak` is created.

Challenge photos are diagnostic. They can improve coverage of hard/model-disagreement cases without contaminating the primary representative held-out result.

The final WI-0192 checkpoint will generate the initial maintained-archive challenge candidate file and run the private coverage/pilot acceptance. Later model-disagreement candidates from WI-0193 can use the same import contract without replacing the representative core.


## Checkpoint 3: generate the initial maintained-archive challenge tranche

The first challenge tranche deliberately reuses only explainable evidence already available before WI-0193. It scans current non-core photos with durable review proxies and selects across:

- WI-0182 candidate technical reasons;
- bottom/top 2% whole-proxy sharpness;
- bottom/top 2% mean luminance;
- top 2% shadow/highlight clipping;
- bottom 2% luminance contrast;
- genuinely reduced proxy resolution when that percentile is below full-size;
- rare capture-era/media/resolution/device strata; and
- missing capture-era/camera metadata.

These are **challenge-selection heuristics**, not quality labels. They are hidden during human review and must not be interpreted as evidence that a selected photo is bad.

Generate the initial private challenge file:

~~~powershell
dotnet run --project tools/PhotoIdentity.PhotoQualityEvaluation -- benchmark generate-challenges `
  --input private/photo-quality-benchmark.json `
  --output private/photo-quality-challenges.json `
  --count 180
~~~

The command analyzes the non-core durable proxies, prints progress every 500 photos, excludes revisions and exact content hashes already present in the benchmark, and uses deterministic round-robin selection across challenge-reason buckets.

The default is **180**, not the full 200-slot reserve. This produces an initial benchmark of about 580 photos while retaining 20 diagnostic slots for later WI-0193 model-disagreement cases. That is an intentional documented exception to the approximate 600-photo target rather than silently consuming all challenge capacity before candidate learned models exist.

Append the generated tranche:

~~~powershell
dotnet run --project tools/PhotoIdentity.PhotoQualityEvaluation -- benchmark add-challenges `
  --input private/photo-quality-benchmark.json `
  --candidates private/photo-quality-challenges.json
~~~

## Privacy-safe aggregate report

Inspect benchmark composition and review progress without printing source keys or proxy paths:

~~~powershell
dotnet run --project tools/PhotoIdentity.PhotoQualityEvaluation -- benchmark report `
  --input private/photo-quality-benchmark.json
~~~

The report includes representative/challenge counts, calibration/held-out counts, broad capture/media/resolution/orientation/face coverage, camera-metadata coverage, review-level counts and challenge-reason counts. It intentionally reports only the number of distinct camera models rather than their names.

## Maintainer acceptance sequence

After checkpoint 3 is merged into `features/m37`:

1. Generate a fresh 400-photo representative benchmark with a 200-photo challenge reserve.
2. Run `benchmark report` and inspect whether the representative coverage looks plausible for the maintained archive.
3. Generate and append the default 180-photo challenge tranche.
4. Run `benchmark report` again and confirm the final initial benchmark is about 580 photos and the challenge reasons are diverse rather than dominated by one degenerate signal.
5. Start the blinded reviewer and review approximately **30 photos** as a pilot. Stop with Ctrl+C; all labels are already saved.
6. Re-run `benchmark report` and confirm review progress/resume behavior.
7. If the sample visibly still misses a major archive class, do not complete WI-0192: record the gap and adjust the sampling/challenge policy first.
8. If coverage and the blinded pilot are acceptable, continue the remaining human review when useful for WI-0193 and record WI-0192 maintainer acceptance. The 20 reserved challenge slots remain available for later model-disagreement additions.

WI-0192 can close after this maintained-archive coverage/pilot acceptance. Completing all ~580 human ratings is not required merely to close the benchmark/tooling work item; WI-0193 consumes and extends the benchmark as its model comparison proceeds.


## WI-0193 checkpoint 1: score candidates without opening the held-out labels

WI-0193 reuses the frozen WI-0192 benchmark but separates **candidate inference** from **human-label evaluation**. Candidate score files are private working artifacts and must stay under `private/`, `data/`, `staging/` or outside the repository.

Generate the fixed WI-0182 technical baseline over the complete benchmark:

~~~powershell
$env:PhotoIdentity__ReviewProxyRoot = "<review proxy root>"

dotnet run --project tools/PhotoIdentity.PhotoQualityEvaluation -- benchmark quality-eval baseline `
  --input private/photo-quality-benchmark.json `
  --output private/wi0182-quality-scores.json
~~~

The baseline verifies each proxy SHA-256 before analysis and records `proxy-technical-quality-v1` reason evidence, per-photo runtime, median/p95 runtime and exact benchmark identity. Its score is deliberately simple: `100 - 10 * candidate reason count`. It exists to make WI-0182 measurable beside learned candidates; it is not a proposed production rating.

Run the calibration-only report:

~~~powershell
dotnet run --project tools/PhotoIdentity.PhotoQualityEvaluation -- benchmark quality-eval calibration-report `
  --input private/photo-quality-benchmark.json `
  --scores private/wi0182-quality-scores.json `
  --output private/wi0182-quality-calibration.json
~~~

The report uses only representative photos whose frozen partition is `calibration` and whose blinded label is one of Very Low / Low / Normal / High / Very High. It excludes challenge photos, `Unsure` labels and every held-out human label.

Candidate score JSON schema version 1 is shared by later learned-model runners. Each file must:

- match the benchmark ID and contain exactly one row for every benchmark revision;
- state candidate/model version, framework, license/source, preprocessing, score semantics, model size and execution provider;
- declare whether a higher raw score means better quality;
- record per-photo inference milliseconds;
- record a finite raw score or an explicit bounded error code, never silently omit failed photos.

Calibration reports include Spearman rank correlation, pairwise ordering for human labels separated by at least two tiers, inference failures and a diagnostic Very Low threshold scan. The threshold scan reports observed precision plus a 95% Wilson lower bound. A result such as 1/1 or 2/2 at 100% observed precision is therefore visible as weak evidence rather than being mistaken for a safe automatic-exclusion policy.

There is intentionally **no held-out report command yet**. Add that only after candidate selection and the ordinal/Very-Low calibration policy are frozen from calibration evidence. This keeps the final held-out result a one-shot decision rather than an iterative tuning surface.

## WI-0193 checkpoint 2: learned no-reference IQA runner

An optional **Python 3.12+** evaluation-only runner is provided in
`tools/PhotoIdentity.PhotoQualityEvaluation/learned_iqa.py`.
It invokes the third-party [IQA-PyTorch](https://github.com/chaofengc/IQA-PyTorch)
`pyiqa` API using exactly three initial candidate names:
`topiq_nr`, `musiq`, and `arniqa` (default KonIQ versions).
These are hypotheses for measurement, **not** accepted production quality models.

In a separate virtual environment (PowerShell):

~~~powershell
py -3.12 -m venv .venv-iqa
.\.venv-iqa\Scripts\Activate.ps1
python -m pip install --upgrade pip
# Choose the compatible, documented PyTorch CPU/CUDA installation for this machine first.
python -m pip install pyiqa
python -m unittest discover -s tools/PhotoIdentity.PhotoQualityEvaluation -p test_learned_iqa.py
~~~

The package and model checkpoints are third-party dependencies; installing or
loading a model may download weights. Review the package and checkpoint licenses,
storage, network policy, memory requirements and trust of downloaded weights
before running the command. This PR does **not** vendor or pin their checkpoint
bytes, nor does it add a Python or GPU dependency to the packaged Photo Identity app.

Run the candidate separately for each metric, using the existing benchmark and
proxy root (replace `topiq_nr` with `musiq` and `arniqa` for the other two):

~~~powershell
python tools/PhotoIdentity.PhotoQualityEvaluation/learned_iqa.py `
  --input private/photo-quality-benchmark.json `
  --proxy-root "$env:PhotoIdentity__ReviewProxyRoot" `
  --output private/topiq_nr-scores.json `
  --metric topiq_nr --device cpu --accept-third-party-weights

dotnet run --project tools/PhotoIdentity.PhotoQualityEvaluation -- benchmark quality-eval calibration-report `
  --input private/photo-quality-benchmark.json `
  --scores private/topiq_nr-scores.json `
  --output private/topiq_nr-calibration.json
~~~

For smoke testing, add `--limit 10` with a unique private output filename.
Limited runs intentionally **cannot** pass the full-coverage validator in the
calibration-report command.

The runner validates all selected proxy SHA-256 digests before model loading,
rejects traversal and linked/junction paths, reports bounded inference errors
without exposing private source paths, and refuses to overwrite outputs. It
exports raw model scores (higher-is-better) and runtime metrics, not human
labels, partition names or raw images. Model package version, preprocessing and
device are recorded, but exact checkpoint hash, footprint, license permission,
and hardware-specific deployability are **not yet established**. Do not
interpret these local outputs as a calibrated go/no-go decision.

Before the final WI-0193 candidate decision: record exact package/weight hashes
and size, model/license terms, host CPU/GPU/runtime evidence, fill remaining
20 reserved model-disagreement challenges, complete enough blinded ratings, and
run the bounded proxy-vs-original comparison. Then freeze calibration and
evaluate held-out exactly once. No production score persistence or automatic
exclusion is added here.



## WI-0193 checkpoint 3: compare scored candidates without consulting held-out labels

Once the three complete private score exports exist (and optionally the WI-0182 baseline), compare them using the standalone, standard-library-only candidate audit:

~~~powershell
python -m unittest discover -s tools/PhotoIdentity.PhotoQualityEvaluation -p test_candidate_audit.py

python tools/PhotoIdentity.PhotoQualityEvaluation/candidate_audit.py `
  --input private/photo-quality-benchmark.json `
  --scores private/wi0182-quality-scores.json private/topiq_nr-scores.json private/musiq-scores.json private/arniqa-scores.json
~~~

The command prints an **aggregate-only** JSON report. It validates that every score file belongs to the same frozen benchmark and contains one finite score or explicit error for every revision. It reports reviewed representative-calibration coverage, within-calibration Spearman agreement to human labels, inter-candidate rank agreement over shared reviewed calibration photos, candidate failures, runtime and whether the current version/model-size metadata is complete.

**No held-out or challenge human label is accessed.** In particular, do not use the output to select a final threshold using held-out evidence; the model/calibration decision still needs its formal freeze. Strong inter-model agreement is not proof of good quality. The audit does not compute archive-wide model disagreement or fill the remaining 20 private challenge slots; that requires candidates scored on previously unselected archive photos.

The resulting JSON output contains aggregate metrics and candidate identifiers only. Keep private input score exports and any per-photo analysis outside Git. A meaningful model comparison also requires enough calibration photos to be labelled and successful inference for each candidate.

## WI-0193 checkpoint 4: maintained-archive evidence and paired uncertainty

The maintainer has now scored the frozen 580-photo benchmark with all four
candidates; every candidate completed without an inference failure. There are
238 usable **calibration representative** ratings, including just 3 Very Low
examples. Aggregate findings and the conservative **no automatic exclusion**
assessment are recorded in
[`photo-quality-model-evidence-2026-10-17.md`](photo-quality-model-evidence-2026-10-17.md).
Do **not** commit the private benchmark or per-photo score JSONs.

Run the new standard-library-only paired bootstrap on the two leading
calibration candidates (PowerShell, from the repository root):

~~~powershell
python -m unittest discover -s tools/PhotoIdentity.PhotoQualityEvaluation -p test_paired_calibration_confidence.py

python tools/PhotoIdentity.PhotoQualityEvaluation/paired_calibration_confidence.py `
  --input private/photo-quality-benchmark.json `
  --scores private/topiq_nr-scores.json private/musiq-scores.json `
  --left pyiqa-topiq_nr-0.1.16 `
  --right pyiqa-musiq-0.1.16 `
  --repetitions 1000 `
  --seed 20261017
~~~

Bootstrap resampling is at the **photo level**, not at the pair level:
multiple comparison pairs share images and must not be treated as independent.
It emits no source keys or per-photo details and never reads held-out/challenge
human labels. In this maintained-archive calibration, the 95% interval for
MUSIQ-minus-TOPIQ Spearman is approximately [-0.055, +0.081], and for pairwise
accuracy [-5.9, +3.7] percentage points. Both include zero; the difference
is not a defensible deployment choice.

Remaining work: checkpoint hashes and licenses, local image/model setup
provenance, 20 **new non-benchmark** disagreement-challenge candidates,
paired original-vs-proxy inference, frozen calibration decision, and one-shot
held-out evaluation. The scored 580-photo benchmark alone cannot fill new
challenge slots because all its revisions are already selected.

## WI-0193 checkpoint 4: capture local checkpoint provenance

The maintainer's initial learned-model exports explicitly report
`weights-not-yet-pinned`, and model size/license information remains unknown.
The new **standard-library-only** tool records SHA-256 and byte sizes for
**explicitly supplied** downloaded checkpoint files; it does not connect to
Hugging Face or load models.

Locate candidate weight files on Windows. Some checkpoints are in the torch
cache; others may be in the Hugging Face cache:

~~~powershell
$torchCache = Join-Path $env:USERPROFILE ".cache\torch\hub\pyiqa"
$hfCache = Join-Path $env:USERPROFILE ".cache\huggingface\hub"

foreach ($dir in @($torchCache, $hfCache)) {
    if (Test-Path $dir) {
        Get-ChildItem -LiteralPath $dir -Recurse -File |
          Where-Object { $_.Extension -in @(".pth", ".pt", ".safetensors", ".bin") } |
          Select-Object FullName, Length
    }
}
~~~

Locate the **actual** three files loaded by `pyiqa` and replace the paths
below, using your existing Python venv. A downloaded file is *not* proof
that a particular candidate loaded that file: use the model load logs
and third-party model configuration to verify the mapping.

~~~powershell
& $python tools/PhotoIdentity.PhotoQualityEvaluation/checkpoint_provenance.py `
  --checkpoint "topiq_nr=C:\actual\cache\topiq-weights.pth" `
  --checkpoint "musiq=C:\actual\cache\musiq-weights.pth" `
  --checkpoint "arniqa=C:\actual\cache\arniqa-weights.pth" `
  --output private/iqa-checkpoint-provenance.json

& $python -m unittest discover -s tools/PhotoIdentity.PhotoQualityEvaluation -p test_checkpoint_provenance.py
~~~

Keep the private inventory and the checkpoint bytes outside Git. The inventory
contains checkpoint file **names**, hashes and sizes, without the parent
filesystem paths. It records the local Python and installed `pyiqa` package
versions, but does not determine independent third-party weight licenses or
whether a specific model actually used that checkpoint. Resolve these
questions separately before any production deployment decision.

## WI-0193 checkpoint 5: select remaining 20 new disagreement challenges

This evaluation-only workflow finds **new photos outside the frozen benchmark**
before comparing TOPIQ and MUSIQ. The original 580 photo candidate scores
cannot populate the remaining 20 slots because their revisions are already
in the benchmark. We first generate a **bounded, technical/rarity-diverse**
non-benchmark screening pool. The final 20 are selected by model *rank
disagreement within that pool*, **not** by their human labels. This is a
diagnostic pool-screening procedure, not proof that these are the 20 most
disputed photos across the entire archive.

### Step 1: generate a non-benchmark candidate pool

From the repository root, with maintained PostgreSQL connection, review proxy
root and profile already configured as for WI-0192:

~~~powershell
$env:PhotoIdentity__ReviewProxyProfileId = "jpeg-1600-q78"

dotnet run --project tools/PhotoIdentity.PhotoQualityEvaluation -- benchmark generate-challenges `
  --input private/photo-quality-benchmark.json `
  --output private/wi0193-disagreement-pool.json `
  --count 160 `
  --pool-only
~~~

**Do not import this file** with `benchmark add-challenges`: it contains
up to 160 screened candidates, while the benchmark reserve has only 20
remaining slots. The generator verifies proxy bytes, excludes existing
benchmark revision IDs and content hashes, and selects deterministically
across technical-extreme/rarity buckets. It also deduplicates pool candidates
by original content hash.

### Step 2: score the new screening pool

Use the existing optional Python/pyiqa virtual environment from checkpoint 2.
Scoring is CPU-capable, explicitly consents to third-party weights, verifies
each frozen proxy SHA-256, and writes **new private** score exports.

~~~powershell
& $python tools/PhotoIdentity.PhotoQualityEvaluation/disagreement_challenges.py score `
  --input private/photo-quality-benchmark.json `
  --pool private/wi0193-disagreement-pool.json `
  --proxy-root "$env:PhotoIdentity__ReviewProxyRoot" `
  --metric topiq_nr --device cpu `
  --output private/wi0193-pool-topiq-scores.json `
  --accept-third-party-weights

& $python tools/PhotoIdentity.PhotoQualityEvaluation/disagreement_challenges.py score `
  --input private/photo-quality-benchmark.json `
  --pool private/wi0193-disagreement-pool.json `
  --proxy-root "$env:PhotoIdentity__ReviewProxyRoot" `
  --metric musiq --device cpu `
  --output private/wi0193-pool-musiq-scores.json `
  --accept-third-party-weights
~~~

The pool scoring export uses `inputKind=review-proxy-disagreement-pool` and
is **not** a regular full-benchmark score export. Do not feed it to the
calibration audit as a replacement for the original 580-photo export.

### Step 3: select the 20 most disputed pool items

~~~powershell
& $python -m unittest discover -s tools/PhotoIdentity.PhotoQualityEvaluation -p "test_disagreement_challenges.py"

& $python tools/PhotoIdentity.PhotoQualityEvaluation/disagreement_challenges.py select `
  --input private/photo-quality-benchmark.json `
  --pool private/wi0193-disagreement-pool.json `
  --topiq-scores private/wi0193-pool-topiq-scores.json `
  --musiq-scores private/wi0193-pool-musiq-scores.json `
  --count 20 `
  --output private/wi0193-disagreement-challenges.json
~~~

The selector validates the matching benchmark IDs, expected model names,
pool score input kinds and full finite-score coverage. It rejects any
inference failure rather than silently steering selection toward scored
items. Selection uses the *absolute difference of within-pool percentile
ranks*, with a stable SHA-256 tie-breaker, and never reads human labels from
any benchmark partition. It leaves all selected human review fields blank.
Outputs contain private source/proxy metadata and must not be committed.

### Step 4: import only when ready to extend score exports

The challenge importer is already implemented:

~~~powershell
dotnet run --project tools/PhotoIdentity.PhotoQualityEvaluation -- benchmark add-challenges `
  --input private/photo-quality-benchmark.json `
  --candidates private/wi0193-disagreement-challenges.json

dotnet run --project tools/PhotoIdentity.PhotoQualityEvaluation -- benchmark review `
  --input private/photo-quality-benchmark.json
~~~

**IMPORTANT:** Once 20 cases are imported, the benchmark will contain 600
photos. The four original candidate score exports cover **580** photos and
will no longer pass the full-benchmark coverage check against the extended
manifest. Keep a private backup of the 580-photo manifest/exports and do not
mistake the resulting validation rejection for a model failure. A follow-up
checkpoint will provide score-export extension/reconciliation for the 20
new photos before final held-out evaluation. It is reasonable to postpone
this import until that checkpoint. The 240 calibration and 160 held-out
representative photos remain frozen in either case. Do not change the
calibration policy after inspecting the blinded challenge ratings.

As everywhere in WI-0193: no quality scores are persisted to production,
no normal photos are automatically excluded, and no held-out human labels
are read during candidate screening/selection.

## WI-0193 checkpoint 6: extend the four private candidate exports after challenge import

This supersedes checkpoint 5's warning that the newly imported 20 cases cannot
be reconciled. The `reconcile_quality_scores.py` tool now strictly extends
the original 580-photo TOPIQ/MUSIQ exports from their already-scored 160-photo
**screening pool**, with no redundant 580-photo model run. ARNIQA can score
**only the selected 20**; the cheap WI-0182 baseline is re-run on all 600.
The resulting four 600-photo exports again satisfy the benchmark-bound
`calibration-report`/`candidate_audit.py` coverage contract.

**Sequence matters.** Pool generation and both pool scoring runs (checkpoint 5
steps 1–3) **must happen before import**. The pool scorer rejects importing
an overlapping pool. The frozen representative core must not be regenerated.

### Step 4 (revised): preserve the 580-photo private benchmark and import 20

From PowerShell in the repo root, after generating
`private/wi0193-disagreement-challenges.json` in checkpoint 5:

~~~powershell
$before = "private/photo-quality-benchmark-before-disagreement.json"
if (Test-Path $before) {
    throw "Snapshot already exists: verify it is the PRE-IMPORT 580-photo benchmark"
}
Copy-Item -LiteralPath "private/photo-quality-benchmark.json" -Destination $before

dotnet run --project tools/PhotoIdentity.PhotoQualityEvaluation -- benchmark add-challenges `
  --input private/photo-quality-benchmark.json `
  --candidates private/wi0193-disagreement-challenges.json

dotnet run --project tools/PhotoIdentity.PhotoQualityEvaluation -- benchmark report `
  --input private/photo-quality-benchmark.json
~~~

Confirm the report says **600 total**, **400 representative**,
**240 calibration**, **160 held-out**, and **200 challenges**. Do not use
`photo-quality-benchmark.json.before-challenge.bak` as your pre-import snapshot:
a previous challenge import may already have created that one-time backup.
The explicit `$before` snapshot above is mandatory.

### Step 5: score ARNIQA on only the 20 newly imported proxies

~~~powershell
& $python tools/PhotoIdentity.PhotoQualityEvaluation/reconcile_quality_scores.py score-selected `
  --original-benchmark private/photo-quality-benchmark-before-disagreement.json `
  --benchmark private/photo-quality-benchmark.json `
  --selected private/wi0193-disagreement-challenges.json `
  --proxy-root "$env:PhotoIdentity__ReviewProxyRoot" `
  --metric arniqa --device cpu `
  --output private/wi0193-selected-arniqa-scores.json `
  --accept-third-party-weights
~~~

`score-selected` verifies the two benchmark snapshots and imported challenge
file before model loading, hashes the selected proxy bytes, and uses the
existing optional `pyiqa` runner. The result is a private selected-only score
export, **not** a complete candidate export.

### Step 6: extend TOPIQ/MUSIQ/ARNIQA into new 600-photo exports

~~~powershell
foreach ($metric in @("topiq_nr", "musiq")) {
    & $python tools/PhotoIdentity.PhotoQualityEvaluation/reconcile_quality_scores.py extend `
      --original-benchmark private/photo-quality-benchmark-before-disagreement.json `
      --benchmark private/photo-quality-benchmark.json `
      --selected private/wi0193-disagreement-challenges.json `
      --pool private/wi0193-disagreement-pool.json `
      --scores "private/$metric-scores.json" `
      --supplement "private/wi0193-pool-$($metric -replace '_nr', '')-scores.json" `
      --output "private/$metric-scores-600.json"
}

& $python tools/PhotoIdentity.PhotoQualityEvaluation/reconcile_quality_scores.py extend `
  --original-benchmark private/photo-quality-benchmark-before-disagreement.json `
  --benchmark private/photo-quality-benchmark.json `
  --selected private/wi0193-disagreement-challenges.json `
  --scores private/arniqa-scores.json `
  --supplement private/wi0193-selected-arniqa-scores.json `
  --output private/arniqa-scores-600.json
~~~

The original candidate files remain untouched; the new exports retain the
same candidate/version/provider descriptors. If Python/pyiqa or the model
checkpoint changed between the initial 580 and supplemental 20 scores, the
reconciliation correctly refuses to combine them: restore the compatible
environment or re-score with a documented version instead.

The reconciled `runtime.totalElapsedMilliseconds` is **the sum of per-photo
latencies**, not measured model-loading-inclusive wall time. The median and
p95 inference values are recomputed over the complete 600-photo sequence.

### Step 7: refresh the inexpensive technical baseline

~~~powershell
dotnet run --project tools/PhotoIdentity.PhotoQualityEvaluation -- benchmark quality-eval baseline `
  --input private/photo-quality-benchmark.json `
  --output private/wi0182-quality-scores-600.json
~~~

### Step 8: validate all four full score exports

~~~powershell
& $python -m unittest discover -s tools/PhotoIdentity.PhotoQualityEvaluation -p "test_reconcile_quality_scores.py"

& $python tools/PhotoIdentity.PhotoQualityEvaluation/candidate_audit.py `
  --input private/photo-quality-benchmark.json `
  --scores private/wi0182-quality-scores-600.json private/topiq_nr-scores-600.json private/musiq-scores-600.json private/arniqa-scores-600.json

dotnet run --project tools/PhotoIdentity.PhotoQualityEvaluation -- benchmark review `
  --input private/photo-quality-benchmark.json
~~~

Only the final **aggregate** audit and benchmark reports should be shared
externally; keep the original and expanded private manifest, screening pool,
challenge import, all per-photo score exports and checkpoint files outside Git.
Review the 20 newly imported challenges blindly; their labels are diagnostic
and **must not** tune a model or threshold using held-out or challenge ratings.
The challenge reserve is fully used after import; a separate selection policy
must be justified before altering it again.

No production quality evidence, collection ranking, exclusion or held-out
decision is changed by this checkpoint.

## WI-0193 checkpoint 7: original-versus-review-proxy paired study

The maintainer reports the 600-photo blinded benchmark review completed.
Private review-state inspection of **calibration and challenge only** confirms
240/240 calibration and 200/200 challenge ratings recorded, including all 20
new model-disagreement cases. The 20 new cases have 7 Normal, 7 High and
6 Unsure ratings. **Do not use these challenge ratings to select models or
thresholds.** The representative held-out review values have deliberately
not been read as part of this checkpoint.

Before any final candidate/threshold freeze, test whether the learned quality
scores change when the same photo is supplied at its original decoded pixel
size rather than through its fixed `jpeg-1600-q78` review proxy. The
new `original_proxy_study.py` selects 50–100 photos **only** from the
calibration and challenge partitions. It is a diagnostic
**format-balanced sample, not a population-representative estimate**. The
selection algorithm never accesses human review labels and rejects held-out
photos.

### Step 1: select an immutable 80-photo sample

From PowerShell in the repository root on `features/m37`:

~~~powershell
$python = Join-Path $env:LOCALAPPDATA "PhotoIdentity\iqa-venv\Scripts\python.exe"

& $python -m unittest discover -s tools/PhotoIdentity.PhotoQualityEvaluation -p "test_original_proxy_study.py"

& $python tools/PhotoIdentity.PhotoQualityEvaluation/original_proxy_study.py select `
  --input private/photo-quality-benchmark.json `
  --count 80 `
  --output private/wi0193-original-proxy-selection.json
~~~

This is deterministic, with approximately even representation across
JPEG/HEIC/PNG/RAW as available, and yields a private manifest containing
source keys/IDs and SHA-256 fingerprints. **Do not commit it**. Do not choose
a different sample after viewing its model-score results.

### Step 2: prepare optional ORIGINAL decoding dependencies

Use the same Python/pyiqa virtual environment as for scoring the benchmark,
and an **explicitly configured** path to your actual photo-original source
tree. `sourceKey` is resolved relative to this tree, for example
`2025/02/filename.jpg`. It must point to the original image bytes,
**not** a separate proxy/thumbnail root. The study refuses missing,
repointed, symlink/junction-redirected, oversized or SHA-mismatched original
files. It does not modify originals.

~~~powershell
$env:PhotoIdentity__OriginalPhotoRoot = "C:\YOUR\PHOTO\ORIGINALS\ROOT"
Test-Path $env:PhotoIdentity__OriginalPhotoRoot
Test-Path $env:PhotoIdentity__ReviewProxyRoot

& $python -m pip install pillow-heif rawpy
~~~

Installing `pillow-heif` and `rawpy` is optional third-party code with
codec, native-library and licensing considerations; inspect and accept their
licenses before installation. You can instead select a JPEG/PNG-only study
with `--families jpeg png` if local HEIC/RAW decoding is unavailable, but
that **must** be labelled JPEG/PNG-only and cannot establish HEIC/RAW
stability. The initial 80-photo default intends to cover all four families.

The original decoder renders a lossless temporary RGB PNG for model input.
JPEG/PNG/HEIC use Pillow with EXIF orientation handling; `pillow-heif`
enables HEIC 8-bit decoding. RAW/DNG uses `rawpy` 8-bit demosaicing with
camera white balance and no automatic brightening. These policies may
**differ from Photo Identity's production decoding**, tone mapping, embedded
HEIC color handling, or the review proxy encoder, and these differences
must be interpreted as an evaluation confounder—not as a clean measure of
downscaling alone. Temporary RGB renders are removed after scoring each pair.

### Step 3: score the SAME 80 original/proxy pairs with both leaders

~~~powershell
foreach ($metric in @("topiq_nr", "musiq")) {
    & $python tools/PhotoIdentity.PhotoQualityEvaluation/original_proxy_study.py score `
      --input private/photo-quality-benchmark.json `
      --selection private/wi0193-original-proxy-selection.json `
      --original-root "$env:PhotoIdentity__OriginalPhotoRoot" `
      --proxy-root "$env:PhotoIdentity__ReviewProxyRoot" `
      --metric $metric --device cpu `
      --output "private/wi0193-original-proxy-$metric.json" `
      --accept-third-party-weights

    if ($LASTEXITCODE -ne 0) { throw "Original/proxy scoring failed: $metric" }
}
~~~

Both source and review proxy bytes are hash-checked **before** model loading.
Per-photo errors are recorded by a generic error code without exception
messages/paths; they are never silently counted as successful comparisons.
The source/original score is computed on losslessly rendered full-size
decoded pixels; proxy inference uses the already stored `jpeg-1600-q78`.
These scores are a **new paired experiment**, not replacements for the
600-photo benchmark candidate exports.

### Step 4: print aggregate stability reports

~~~powershell
foreach ($metric in @("topiq_nr", "musiq")) {
    & $python tools/PhotoIdentity.PhotoQualityEvaluation/original_proxy_study.py report `
      --scores "private/wi0193-original-proxy-$metric.json"
}
~~~

Check both reports have **sampleCount=80, scoredPairs=80,
failures=0, complete=true** before interpreting them. The aggregate includes
overall and per-media-family original/proxy score rank correlation, median
absolute score shift, median signed proxy-minus-original shift and top
quartile overlap. Larger rank shifts may signal risk in transferring a model
measured on proxies to normal original-resolution photos, but neither
correlation nor small shifts alone establishes safe deployment or exclusion.

If either model has missing/unsupported original files or a decoding
failure, do not substitute photos after seeing scores to get a favorable
result. Preserve the private error export and report only the generic
failure count. Additional codec or source-root development may be required.

**Privacy and evaluation gates:** Only share aggregate console JSON results.
Do not share/commit the private 600-photo benchmark, review fields, source
keys, original images, temporary renders or paired per-photo score JSONs.
No challenge/held-out labels are used for model/threshold selection. This
experiment does not freeze calibration, endorse an exclusion policy, or
authorize the final one-shot held-out comparison.

## WI-0193 checkpoint 7 safety follow-up: progress and memory-bounded original scoring

A maintainer Windows run of the initial full-resolution TOPIQ/MUSIQ
`original_proxy_study.py score` consumed nearly all RAM and saturated
disk paging for approximately one hour. The previous runner printed no
per-photo progress and saved its private result **only after an entire
model's batch completed**. At that utilization, cancelling with Ctrl+C
is reasonable; originals and the 600-photo benchmark are read-only.
Completed per-model export files are preserved, while unfinished
per-model work needs restarting. Do not blindly repeat the same
unbounded two-model command.

The runner now reports model initialization and *each completed pair*
with status and elapsed seconds, without private paths or photo IDs.
It additionally supports these **explicit opt-in safeguards**:

- `--probe-pairs N` for **1–10** first-pair diagnostic inference runs.
  A probe writes its own **new private file** and reports
  `isProbe=true`, `complete=false`. A probe cannot be used as a
  50–100 photo acceptance result.
- `--max-original-long-edge 2560` bounds the RGB original **model
  input** to at most 2560 pixels on the long edge, while the review
  proxy remains 1600 pixels. This is *not full-resolution inference*:
  explicitly report the cap and interpret the results as
  2560px-original-render-versus-proxy sensitivity. The original
  decode (especially RAW demosaicing) may still temporarily use
  considerable memory. Omit the option only on adequately provisioned
  hardware or in a separate constrained experiment.

### Recommended Windows restart after cancelling the resource-heavy run

Verify which complete model exports already exist, **without reading their
private per-photo rows into chat**:

~~~powershell
foreach ($metric in @("topiq_nr", "musiq")) {
    $file = "private/wi0193-original-proxy-$metric.json"
    if (Test-Path -LiteralPath $file) {
        $report = & $python tools/PhotoIdentity.PhotoQualityEvaluation/original_proxy_study.py report `
          --scores $file
        "Completed result present for $metric (review aggregate report above)"
    } else {
        "No completed result for $metric"
    }
}
~~~

First, run a two-pair **bounded diagnostic** with a distinct private
output filename:

~~~powershell
& $python tools/PhotoIdentity.PhotoQualityEvaluation/original_proxy_study.py score `
  --input private/photo-quality-benchmark.json `
  --selection private/wi0193-original-proxy-selection.json `
  --original-root "$env:PhotoIdentity__OriginalPhotoRoot" `
  --proxy-root "$env:PhotoIdentity__ReviewProxyRoot" `
  --metric topiq_nr --device cpu `
  --probe-pairs 2 `
  --max-original-long-edge 2560 `
  --output private/wi0193-original-proxy-topiq-probe-2560.json `
  --accept-third-party-weights
~~~

Expect two `pair N/2` console updates. The report must say
`isProbe=true`, `sampleCount=2`, `plannedPairs=80`,
`maxOriginalLongEdge=2560`, `complete=false`. Inspect RAM and disk
usage during the probe. Only if that remains tolerable should you
attempt an 80-photo **2560px-bounded** run for TOPIQ and then MUSIQ
separately. Use distinct filenames
`private/wi0193-original-proxy-topiq_nr-2560.json` and
`private/wi0193-original-proxy-musiq-2560.json`; original export files
are never overwritten. The script prints each pair's progress, but
results still commit only at completion of the per-model batch.

A completed full-resolution result and a capped-resolution result are
**different experimental conditions** and should never be compared
as if they were the same. Record memory, score throughput, decoder
failures and the explicit input cap. If the probe itself pushes RAM
near 100% and causes sustained paging, stop and reconsider the
experiment rather than increasing the number of photos.

## WI-0193 checkpoint 8: diagnostic follow-up to resource-heavy original study

The maintainer's 2026-10-17 **uncapped original-pixel** study ran for
approximately three hours with high CPU, saturated RAM/disk and paging.
The first model, TOPIQ NR, **finished the entire 80-photo loop** and wrote
its result; however only **63/80** paired photo scores succeeded, and
**17/80** reported the legacy generic `decode-or-inference-failed` error.
The second model, MUSIQ, was explicitly interrupted during its transformer
inference and wrote **no completed per-model result**.

TOPIQ's *successful-only* summary was: original/proxy Spearman **0.62356**,
median proxy-minus-original score **+0.10410**, median absolute shift
**0.10410**, and top-quartile overlap **0.25**. Valid pairs by format were
JPEG 17, HEIC 20, PNG 20 and RAW 6. These figures are **not a complete
or population-representative result**; missingness is concentrated in
JPEG/RAW, and independent source decoders may change color/tone/orientation.
The 600-photo calibration results and no-automatic-exclusion policy are
unchanged. This observation does not establish any model's accuracy on
originals, nor a need to re-run an unbounded MUSIQ comparison.

### Find the completed TOPIQ export from the correct working directory

The old scoring run started from the repository root, but the later file
presence check ran from the operator's Windows user-profile directory.
The output path `private/wi0193-original-proxy-topiq_nr.json` is relative
to **the working directory of the scoring process**, not the directory
holding `original_proxy_study.py`. The successfully printed JSON
summary proves TOPIQ returned from its scoring function, which writes
its output before reporting.

~~~powershell
$repo = "C:\Kod\codex\Photo Identity Indexer"
$python = Join-Path $env:LOCALAPPDATA "PhotoIdentity\iqa-venv\Scripts\python.exe"
$path = Join-Path $repo "private\wi0193-original-proxy-topiq_nr.json"

Test-Path -LiteralPath $path
if (Test-Path -LiteralPath $path) {
    & $python (Join-Path $repo "tools\PhotoIdentity.PhotoQualityEvaluation\original_proxy_study.py") report `
      --scores $path
}
~~~

**Do not re-run TOPIQ** just to recover the aggregate from its existing
output. The revised `report` command now also displays
`attemptsByMediaFamily`, `failuresByMediaFamily`, and
`failuresByStage`. It can reconstruct **format-level failure counts**
from the *existing* TOPIQ private JSON, without decoding anything or
running any model. Old generic `decode-or-inference-failed` codes cannot
retrospectively reveal the exact failing processing stage. New runs
distinguish `decode-failed`, `original-inference-failed`,
`proxy-inference-failed`, and `nonfinite-score-failed`
**without including any error message, source filename or path**.

The unauthenticated Hugging Face warning is nonfatal when the checkpoint
loads from cache; a `multiprocess.resource_tracker` shutdown warning
does not explain the earlier 17 paired failures. The MUSIQ traceback
ends with the operator's `KeyboardInterrupt`, not evidence that
MUSIQ's quality metrics failed.

### Decision

Do not initiate another unbounded three-hour CPU run. The 600-photo
proxy inference exports still have zero failures; this is a **separate
full-resolution/decoder stress test**. Inspect the aggregate failure
breakdown, retain all private exports and consider a bounded 2-pair
probe from PR #565 before selecting a different local study protocol.
Using the existing Azure T4 caption GPU would require a dedicated
pyIQA/CUDA container and privacy/architecture authorization beyond
the 480×320-thumbnail captioning exception; it is not yet an approved
drop-in replacement. The final held-out labels remain untouched.

## WI-0193 checkpoint 9: diagnose 17 failed original pairs without inference

The aggregate TOPIQ report from the completed unbounded 80-photo run confirms:
**JPEG 4/21 failed**, **RAW 13/19 failed**, and **HEIC 0/20** and
**PNG 0/20** failed. Only 63/80 original/proxy comparisons succeeded.
The old score file has `decode-or-inference-failed` for all 17 and
**cannot distinguish decoding problems from CPU-memory/model failures**.
Retain this evidence; do not rerun the three-hour unbounded models.

The opt-in `original_proxy_metadata_triage.py` script examines **original
file metadata only** for these 17 failed pairs. It verifies the complete
frozen selected benchmark/score ordering without touching held-out or
human-review labels, and validates each original's source SHA-256 before
opening its header. It then obtains JPEG/HEIC/PNG dimensions via Pillow,
and RAW dimensions via `rawpy`, **without pixel rendering or quality
model inference**. This remains much lighter than full-resolution scoring,
but reading RAW file metadata can still require some memory.

From the repository root on the latest `features/m37`, with your original
root environment variable set:

~~~powershell
$python = Join-Path $env:LOCALAPPDATA "PhotoIdentity\iqa-venv\Scripts\python.exe"

& $python -m unittest discover -s tools/PhotoIdentity.PhotoQualityEvaluation -p "test_original_proxy_metadata_triage.py"

& $python tools/PhotoIdentity.PhotoQualityEvaluation/original_proxy_metadata_triage.py `
  --input private/photo-quality-benchmark.json `
  --selection private/wi0193-original-proxy-selection.json `
  --scores private/wi0193-original-proxy-topiq_nr.json `
  --original-root "$env:PhotoIdentity__OriginalPhotoRoot" `
  --scope failed
~~~

Expect `selectedPairs=80`, `previousFailures=17`, and
`metadataChecked=17` if source integrity is intact. The report contains
**only aggregate counts and megapixel/file-size statistics grouped by
media family and previous success/failure status**. No filenames, source
keys, revisions, scores or human labels are printed. A missing/modified
original is an integrity hard stop, not an inferred quality-model failure.
Individual header-readability errors are counted as `headerUnavailable`
without exposing exception strings or per-file information.

If necessary for size comparisons, opt into `--scope all` to examine
**all 80 original headers**, comparing successful and failed photo size
distributions under the same immutable selection. Do **not** include or
export per-photo identities with the aggregate report.

**Interpretation:** a readable image header does not demonstrate
successful full RAW/JPEG pixel decoding; an original whose metadata is
large may stress both decoder and model inference. This tool cannot
retrospectively recover the precise stage of the prior 17 failures.
Do not replace/swap images, relabel `Unsure` or consult held-out data
to improve the result. Avoid initiating further full-resolution CPU
inference until a safe bounded pilot or separately approved GPU
evaluation is justified.

## WI-0193 checkpoint 10: settle the original/proxy processing path

The final `--scope all` metadata-only diagnostic inspected headers for
all **80/80** original photos with **zero unreadable headers**. Exactly
**17/17** originals at roughly 49–50 megapixels failed in the earlier
uncapped original-versus-proxy TOPIQ experiment (RAW 13, JPEG 4).
Exactly **63/63** lower-resolution originals scored successfully (up to
24.47 MP, across JPEG, HEIC, PNG and RAW). There were no tested originals
between 24.47 and 48.77 MP. The old errors combine decoding and
inference, so this is not evidence of a specific codec defect or a
precise safe megapixel threshold. The 63-success rank correlation
and 25% top-quartile overlap are incomplete/decoder-confounded.

**Operator decision:** stop full-original CPU quality-model evaluation
for WI-0193 and preserve the `jpeg-1600-q78` proxy as the sole input to
model calibration and eventual one-shot held-out assessment. The
existing 600-photo proxy exports are complete and require **no
rescoring**. There is no action required from the maintainer for
original-image analysis. Keep all exports and the frozen benchmark
unchanged; do not rerun the 80-image originals study on CPU or
upload originals to the Azure caption bridge.

Proceed instead to weight-checkpoint provenance from checkpoint 4:
verify downloaded weight file SHA-256 and byte size, candidate-to-file
mapping, license for the **checkpoint itself**, plus PyTorch/pyIQA
versions. Never assume the pyiqa package license extends to the model
weight file. Then pre-register a calibration-only ordinal policy or
explicit model no-go before looking at held-out human ratings.
The very low automatic exclusion criterion remains a separate no-go.

The `--max-original-long-edge` option resizes after original decoding,
so it may reduce inference memory but cannot guarantee RAW decoding
will stay below a memory limit.

## WI-0193 checkpoint 11: locally inventory model weights and prepare calibration-only draft

The completed 80-photo original-pixel study identified a strong
resolution-related processing limitation; the project now retains the
immutable `jpeg-1600-q78` proxy as the WI-0193 candidate/held-out input.
**Do not repeat full-original CPU inference.** Model selection still
requires independent weight licensing, model-to-weight identity,
calibration mapping and held-out evidence.

The next utilities run entirely locally and **do not import PyTorch,
load weights, score pixels, or inspect held-out/challenge human labels**.

### Step 1: inventory cached model weights without choosing a model

From PowerShell in the checked-out `features/m37` repository:

~~~powershell
$python = Join-Path $env:LOCALAPPDATA "PhotoIdentity\iqa-venv\Scripts\python.exe"
$cache = Join-Path $env:USERPROFILE ".cache\torch\hub\pyiqa"

& $python -m unittest discover -s tools/PhotoIdentity.PhotoQualityEvaluation -p "test_checkpoint_provenance.py"

& $python tools/PhotoIdentity.PhotoQualityEvaluation/checkpoint_provenance.py `
  --cache-root "$cache" `
  --output private/wi0193-cache-checkpoints.json
~~~

The scan hashes local `.pth`, `.pt`, `.ckpt` and `.safetensors`
artifacts and records PyTorch/pyIQA versions. It never contacts Hugging
Face or other services. The private output contains **basename-only**
checkpoint file names, sizes, SHA-256 digests and **unverified name
hints** (e.g. `cfanet` for `topiq_nr` and `musiq` for MUSIQ).
A cache scan can include other model weights. It does **not** establish
what `pyiqa.create_metric()` actually loaded. Retain the full local
report under `private/`; only share model names, hashes, sizes and
aggregate version counts, never absolute private paths.

To inspect the candidate hints without disclosing your cache directory:

~~~powershell
$inventory = Get-Content private/wi0193-cache-checkpoints.json -Raw |
  ConvertFrom-Json
$inventory.artifacts |
  Select-Object artifactName,candidateHints,sha256,sizeBytes |
  Format-Table -AutoSize
$inventory.packageVersions | Format-List
~~~

### Step 2: explicitly identify checkpoint files (manual verification)

After verifying the actual TOPIQ/MUSIQ/ARNIQA file-to-model mapping
from the local `pyiqa` output/configuration and recording each model
weight's **independent license**, create an explicit inventory. A
suggested filename based only on the cache hint is not sufficient
proof. For instance, only if your local model-loading evidence
confirms the paths:

~~~powershell
& $python tools/PhotoIdentity.PhotoQualityEvaluation/checkpoint_provenance.py `
  --checkpoint "topiq_nr=$(Join-Path $cache 'cfanet_nr_koniq_res50-9a73138b.pth')" `
  --checkpoint "musiq=$(Join-Path $cache 'musiq_koniq_ckpt-e95806b9.pth')" `
  --output private/wi0193-explicit-checkpoints.json
~~~

These particular filenames appeared in prior maintained-machine logs;
they must be matched to the **model that actually loaded them**, rather
than accepted because the names resemble a candidate. Add ARNIQA
using an explicit third `--checkpoint` only when its actual path is
confirmed. This command hashes the files but **does not mark
checkpoint licensing or mapping as verified**.

### Step 3: produce exploratory calibration-only score mappings

Choose one **existing full 600-photo proxy candidate export** at a time;
no new inference is performed:

~~~powershell
& $python -m unittest discover -s tools/PhotoIdentity.PhotoQualityEvaluation -p "test_calibration_policy_draft.py"

& $python tools/PhotoIdentity.PhotoQualityEvaluation/calibration_policy_draft.py `
  --input private/photo-quality-benchmark.json `
  --scores private/musiq-scores-600.json `
  --checkpoint-evidence private/wi0193-explicit-checkpoints.json `
  --output private/wi0193-musiq-policy-draft.json

& $python tools/PhotoIdentity.PhotoQualityEvaluation/calibration_policy_draft.py `
  --input private/photo-quality-benchmark.json `
  --scores private/topiq_nr-scores-600.json `
  --checkpoint-evidence private/wi0193-explicit-checkpoints.json `
  --output private/wi0193-topiq-policy-draft.json
~~~

The tool validates full score-export coverage, candidate identity and
immutable proxy input, and then accesses **only** known-tier representative
**calibration** human labels. Using calibration Low, Normal and High
median scores, it proposes two **fixed numeric raw-score** threshold
midpoints yielding a coarse **three-band** map:
`low-or-below`, `normal` and `high-or-above`.
It requires at least 10 reviewed images in each anchor tier and strict
monotone medians. If this fails, that candidate is not suitable for
this proposed mapping; **do not manually massage labels** or search
held-out data for thresholds.

The draft deliberately **does not** claim a five-tier ordinal model,
approve any candidate, certify a third-party license, establish
out-of-sample accuracy, or enable Very Low automatic exclusion.
Its `status=research-draft-not-frozen`,
`heldOutEvaluationAllowed=false`,
`productionModelApproved=false` and
`automaticVeryLowExclusionApproved=false` are mandatory guards.

Only share the **aggregate console JSON** from each candidate draft
and the checkpoint basename/hash/size/version inventory (plus any
independently verifiable license URL/text). Never upload/commit the
private full manifest, checkpoint file bytes or per-photo score export.
The proposed mapping and model must be reviewed and **explicitly
frozen in a separate checkpoint** before any one-time held-out
quality-label evaluation. An output under `private/` is not a sealed
or preregistered policy simply because it has fixed thresholds.

### Separate framework and model-weight licensing gate

The official `pyiqa 0.1.16` PyPI metadata declares
`PolyForm-Noncommercial-1.0.0`:
https://pypi.org/project/pyiqa/0.1.16/.
The upstream repository also mentions component-specific NTU S-Lab
terms: https://github.com/chaofengc/IQA-PyTorch.
Neither statement determines rights to deploy or redistribute any
specific TOPIQ, MUSIQ or ARNIQA pretrained **weight checkpoint**.
A hash and filename are proof of file identity, not license
permission. Independently record each checkpoint's rights and the
intended application use before any production model decision.

## WI-0193 checkpoint 12: cached model file roles and immediate draft commands

The maintainer's `wi0193-checkpoint-provenance-v2` **local-only**
inventory confirmed `pyiqa 0.1.16`, `torch 2.14.1`,
`torchvision 0.29.1`, `Pillow 13.0.0`, `rawpy 0.27.1`
and `pillow-heif 1.8.1`. The following SHA-256 values and byte
lengths identify cached artifacts without exposing local paths:

| Possible role | Cached filename | SHA-256 | Bytes |
| --- | --- | --- | ---: |
| TOPIQ NR KonIQ model | `cfanet_nr_koniq_res50-9a73138b.pth` | `9a73138beb3748dd6a83365610ac88fa1cd53225342c77fc9ed44a3be461354c` | 181196009 |
| MUSIQ KonIQ model | `musiq_koniq_ckpt-e95806b9.pth` | `e95806b9eae5f3814c410f574ba8e552362bd5bc63d758ed5b97860f5d6185aa` | 108610983 |
| ARNIQA KonIQ regressor only | `regressor_koniq10k.pth` | `af8f127aca38a8e1082e066b5ee93e533bf5f33aaf9c76b3c31526ef901919e1` | 19560 |

Model configuration/source references:
- TOPIQ NR uses `CFANet` with default KonIQ model
  `cfanet_nr_koniq_res50`;
- MUSIQ uses the `koniq10k` variant and default cached
  `musiq_koniq_ckpt-e95806b9.pth`;
- ARNIQA uses a separate learned encoder, KonIQ regressor and may
  initialize a torchvision ResNet50 pretrained backbone. The tiny
  regressor alone is **not full ARNIQA provenance**.

These upstream configuration/filename matches corroborate earlier
private model-load console messages but do not prove what each run
actually loaded or what pretrained weight use/distribution is
licensed to do. References:
https://iqa-pytorch.readthedocs.io/en/stable/_modules/pyiqa/default_model_configs.html ,
https://iqa-pytorch.readthedocs.io/en/stable/_modules/pyiqa/archs/arniqa_arch.html ,
https://github.com/miccunifi/ARNIQA .

### Optional: check PyTorch's second weight cache (no inference)

After this PR merges, from the repository root:

~~~powershell
$python = Join-Path $env:LOCALAPPDATA "PhotoIdentity\iqa-venv\Scripts\python.exe"
$pyiqaCache = Join-Path $env:USERPROFILE ".cache\torch\hub\pyiqa"
$torchCache = Join-Path $env:USERPROFILE ".cache\torch\hub\checkpoints"

$inputs = @(
  "--cache-root", $pyiqaCache,
  "--output", "private/wi0193-all-torch-caches.json"
)
if (Test-Path -LiteralPath $torchCache) {
  $inputs += @("--additional-cache-root", $torchCache)
}
& $python tools/PhotoIdentity.PhotoQualityEvaluation/checkpoint_provenance.py @inputs
~~~

The new scan distinguishes possible ARNIQA **encoder** checkpoint
`ARNIQA.pth`, possible KonIQ **regressor** `regressor_koniq10k.pth`,
and generic possible ResNet50 backbone files, marking all roles as
unverified. It hashes local bytes without loading any models or
uploading images. No checkpoint found is not proof that a dependency
was absent when historical scoring happened; other Torch cache roots
and versions may be involved. Avoid concluding that the one regressor
is the whole deployed ARNIQA model.

### Now: create calibration-only TOPIQ/MUSIQ proposals (no inference)

The next step **does not require this optional additional scan**.
Using the already existing 600-photo scored exports, issue each
candidate's independent draft **once**, from the repository root:

~~~powershell
& $python tools/PhotoIdentity.PhotoQualityEvaluation/calibration_policy_draft.py `
  --input private/photo-quality-benchmark.json `
  --scores private/topiq_nr-scores-600.json `
  --output private/wi0193-topiq-calibration-draft.json

& $python tools/PhotoIdentity.PhotoQualityEvaluation/calibration_policy_draft.py `
  --input private/photo-quality-benchmark.json `
  --scores private/musiq-scores-600.json `
  --output private/wi0193-musiq-calibration-draft.json
~~~

The optional `--checkpoint-evidence` is omitted because a
cache-discovery inventory is **not** explicit actual-loaded-weight
evidence. The research drafts cannot and do not freeze a model,
authorize held-out inspection, enable automatic exclusion, or
establish weight licensing. Share only their **aggregate console JSON**
for review. Keep private draft JSONs, benchmark and per-photo score
files private. Workitem WI-0193 remains open pending an explicit
pre-registered candidate/mapping decision and a one-time held-out
analysis.

## WI-0193 checkpoint 13: compare the unapproved three-band calibration drafts

The maintainer produced two complete calibration-only drafts on
2026-10-18 using frozen `jpeg-1600-q78` proxy scores:

- TOPIQ: raw-score boundaries **0.4976193383336067** and
  **0.5708234310150146**, **89/238** correct three-band predictions
  (**37.39%** apparent accuracy).
- MUSIQ: boundaries **63.076544761657715** and
  **69.06219482421875**, **90/238** (**37.82%**).
- Always predicting **Normal** would score **154/238 = 64.71%**
  in-sample accuracy, but has zero ranking ability.

The Low-or-below/Normal/High-or-above true group sizes are
**41 / 154 / 43**. This imbalance makes raw accuracy alone
misleading: additionally inspect recall/precision and balanced
accuracy before interpreting results. The draft midpoints themselves
should **not** be frozen; don't tune to held-out ratings.

After this PR merges, compare the existing **private drafts only**:

~~~powershell
cd "C:\Kod\codex\Photo Identity Indexer"
$python = Join-Path $env:LOCALAPPDATA "PhotoIdentity\iqa-venv\Scripts\python.exe"
& $python tools/PhotoIdentity.PhotoQualityEvaluation/three_band_calibration_diagnostic.py `
  --left private/wi0193-topiq-calibration-draft.json `
  --right private/wi0193-musiq-calibration-draft.json
~~~

The command prints **only aggregate diagnostics**, including the
two candidate confusion matrices, actual versus predicted band
counts, per-class recall/precision/F1, balanced accuracy and
constant-majority baseline. The run **does not load PyTorch,
run inference, open image files, consult benchmark manifests,
or access held-out/challenge labels**. If the private drafts
were moved or renamed, supply their existing full paths.
It is safe to share the aggregate console JSON; keep both full
policy drafts under `private/` and never commit photo/source
identifiers or full scored exports.

The output is still **calibration-only descriptive evidence**.
Do not unseal the 160 held-out ratings, claim safe Very Low exclusion,
tune by trying many threshold variants on the same 238 photos,
or select a production model until an explicit next-stage decision
is reviewed.

## WI-0193 checkpoint 14: test whether score-gap abstention improves ranking

The maintainer's aggregate **three-band confusion** results rejected
both current absolute median-midpoint mappings: TOPIQ 37.4%
three-band accuracy, balanced accuracy 49.9%, Normal recall
36/154; MUSIQ 37.8%, balanced accuracy 48.5%, Normal recall
40/154. Both label **102/238** photos High-or-above, but only
25 TOPIQ / 21 MUSIQ predictions match the human High-or-above
band. These are poor absolute classifiers. The majority all-Normal
reference achieves 154/238 = 64.7% accuracy, but does not rank
photos.

A final, narrower research question remains: whether large learned
IQA score gaps distinguish **relative ordering** more reliably
than small gaps, while declining to express a preference on
ambiguous comparisons. This is not a per-photo absolute rating,
a production selection rule, or an exclusion policy.

After merging the checkpoint-14 PR, run once from the repo root:

~~~powershell
cd "C:\Kod\codex\Photo Identity Indexer"
$python = Join-Path $env:LOCALAPPDATA "PhotoIdentity\iqa-venv\Scripts\python.exe"

& $python tools/PhotoIdentity.PhotoQualityEvaluation/calibration_selective_ranking.py `
  --input private/photo-quality-benchmark.json `
  --scores private/topiq_nr-scores-600.json private/musiq-scores-600.json
~~~

This command uses **only the existing benchmark and two complete
600-photo candidate score exports**. It does not import PyTorch,
perform inference, decode photos or touch the 160 held-out human
ratings. For each of five deterministic photo-disjoint folds,
score-gap cutoffs are learned *without human labels* from the
other four folds' raw scores, using predeclared **0%, 50%, 75%
and 90%** difference quantiles. On validation-fold pairs only,
it reports the fraction of **all potential comparisons** where
the candidate expresses an ordering (coverage), plus correct
ordering among **different human tiers** and specifically those
separated by **at least two tiers**. The latter conditional subset
is an evaluation metric, not a deployable label-dependent gate.

For review, the default CLI prints **aggregate candidate results**
and omits individual fold thresholds. Add `--fold-details` only
if checking fold variability; these are still aggregated without
photo IDs. Share only the resulting console JSON. The validator
fails closed on incomplete candidate benchmark coverage, unexpected
proxy profile, insufficient calibration samples or broken score
direction. Exact SHA duplicates remain in one fold, but near
duplicates can still cross folds. Pairs share underlying photos,
so counting them as independent observations exaggerates certainty.

**Do not** choose a candidate or cutoff purely from the best row,
imply these results are held-out validation, make a deployment
decision before independently verifying pretrained weights and
license terms, or reintroduce automatic Very Low exclusion.
An explicit pre-registration/no-go decision is required before
any one-time sealed held-out evaluation.

## WI-0193 checkpoint 15: create a review-only fixed TOPIQ ranking proposal

Five-fold calibration-only ranking results over 238 known-tier photos:
with score-gap cutoff at the 75th percentile of training differences,
TOPIQ expressed an ordering for **1441/5635 = 25.57%** of potential
within-fold pairs, with **90.27%** correct ordering on selected
pairs whose **human tiers actually differ**. MUSIQ scored **89.75%**
at ~25% all-pair coverage. TOPIQ at the 90th percentile reached
97.4% conditional ordering at only 10.65% coverage. These
are exploratory calibration results with correlated pairs,
not held-out or production precision claims.

**The fixed-threshold research hypothesis to review:** TOPIQ NR
`pyiqa-topiq_nr-0.1.16`, immutable `jpeg-1600-q78` proxy
input, **75th percentile** of raw-score differences across
all 238 rated representative calibration photos.
The five-fold study used distinct fold-specific cutoffs; to
evaluate on an untouched future cohort, one reproducible
fixed numeric cutoff must first be calculated **without using
held-out labels**.

When checkpoint 15 has merged:

~~~powershell
cd "C:\Kod\codex\Photo Identity Indexer"
git switch features/m37
git pull --ff-only origin features/m37

$python = Join-Path $env:LOCALAPPDATA "PhotoIdentity\iqa-venv\Scripts\python.exe"
& $python tools/PhotoIdentity.PhotoQualityEvaluation/ranking_freeze_proposal.py `
  --input private/photo-quality-benchmark.json `
  --scores private/topiq_nr-scores-600.json `
  --output private/wi0193-topiq-p75-ranking-freeze-proposal.json
~~~

This is offline and lightweight: **no PyTorch/model inference,
photo decoding, network activity or archive changes**. It reads
only representative *calibration* human ratings to establish the
238-photo eligibility group; it never looks at sealed held-out
human reviews. It verifies complete 600-photo candidate coverage,
zero inference failures, score direction and frozen proxy profile.
The output is an **exclusively created, private draft** with score
export/calibration fingerprints, fixed numeric score-gap threshold,
and the explicit abstention/pairwise-order rule. It will refuse
to overwrite an existing file.

The console prints only the candidate, gap quantile, calibration
cohort size, number of pairs, numeric threshold and always-false
approval flags. Please share this **aggregate console JSON**, not
the private full draft, benchmark, per-photo scores or photo paths.

This stage does **not freeze or authorize** the candidate:
`researchHypothesisFrozen=false`,
`heldOutEvaluationAllowed=false`,
`productionModelApproved=false`,
`automaticVeryLowExclusionApproved=false`.
An explicit next-stage review is required before the one-time
held-out evaluation. We must preserve the distinction between
selective pairwise score ordering and an absolute photo-quality
tier or universal slideshow ranking algorithm. The preliminary
mapping-based Low/Normal/High classifiers are rejected, and
automatic Very Low exclusion remains no-go.


## WI-0193 checkpoint 16: maintainer-approved TOPIQ p75 research freeze

On **2026-10-18**, after receiving the private, aggregate-only
checkpoint-15 proposal output, the maintainer explicitly approved
freezing **TOPIQ NR** at the single fixed raw-score gap
**`0.16755089163780212`**. This is a *research hypothesis freeze*,
not a model deployment, held-out label access, absolute photo rating,
collection/slideshow preference change or automatic photo exclusion.
Issue #536 records the decision and the predeclared research endpoints.

Frozen design:

- Candidate `pyiqa-topiq_nr-0.1.16`; score direction **higher is better**;
  input `jpeg-1600-q78` durable review proxies.
- 75th percentile of 28,203 unordered raw-score gaps from the 238 already
  reviewed representative calibration photos; this historical origin
  explains the threshold but **does not permit recalculation** after freeze.
- For any two distinct photo scores, express a preference for the
  higher-scored photo only when their absolute score difference is at least
  `0.16755089163780212` and strictly positive. Otherwise abstain.
  Same-tier human labels cannot be used at inference time.
- Research criteria for the **single representative held-out evaluation**:
  >=80% correct ordering among selected different-human-tier pairs (nonzero
  selected denominator), >=20% decision coverage among all unordered pairs,
  and >=80% correct ordering among selected pairs with >=2-tier human
  difference (nonzero selected denominator).
- Report all pair counts and denominators, abstentions, same-tier selected
  pairs, missing/failed score handling and photo-resampled uncertainty.
  Do not switch candidates, revise the cutoff, or repeat evaluation based on
  held-out outcomes. These exploratory-model-selection caveats remain
  material even if the held-out criteria pass.

The existing private checkpoint-15 proposal still correctly says
`review-proposal-not-frozen`; **do not edit it manually**. Instead use
the new *offline, standard-library-only* local attestation command,
after merging this checkpoint to `features/m37`:

~~~powershell
cd "C:\Kod\codex\Photo Identity Indexer"
git switch features/m37
git pull --ff-only origin features/m37

$python = Join-Path $env:LOCALAPPDATA "PhotoIdentity\iqa-venv\Scripts\python.exe"

& $python -m unittest discover -s tools/PhotoIdentity.PhotoQualityEvaluation -p test_ranking_freeze.py

& $python tools/PhotoIdentity.PhotoQualityEvaluation/ranking_freeze.py `
  --proposal private/wi0193-topiq-p75-ranking-freeze-proposal.json `
  --input private/photo-quality-benchmark.json `
  --scores private/topiq_nr-scores-600.json `
  --output private/wi0193-topiq-p75-ranking-frozen.json
~~~

The attestor deterministically regenerates the *calibration-only* proposal
from the private unchanged benchmark and complete existing TOPIQ score export,
then requires exact equality with the stored proposal, proposal and evidence
fingerprints, candidate, score direction, proxy profile and **exact**
approved cutoff. It fails closed on any drift. The private attestation
records fingerprinted evidence and the fixed research criteria; its console
prints **aggregate-only** fields suitable to share with the developer. It
refuses to overwrite an existing output. No image data, PyTorch models,
inference or held-out/challenge *human ratings* are accessed.

The private attestation sets `researchHypothesisFrozen=true`, while
`heldOutEvaluationAllowed=false`, `heldOutLabelsAccessed=false`,
`productionModelApproved=false` and
`automaticVeryLowExclusionApproved=false`. It must remain private; never
commit the full attestation, proposal, score JSON or benchmark manifest.

**Next gate:** developer implements and reviews a separately guarded,
one-shot held-out evaluator. Running it requires a *later, explicit*
maintainer authorization. No sealed held-out labels may be accessed by this
checkpoint. Independently verify model/checkpoint provenance and license
rights before even considering deployment.
