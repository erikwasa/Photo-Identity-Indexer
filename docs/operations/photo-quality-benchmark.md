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
