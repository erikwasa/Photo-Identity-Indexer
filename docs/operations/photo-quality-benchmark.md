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
