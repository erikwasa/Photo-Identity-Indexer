# WI-0193: maintained-archive calibration evidence (2026-10-17)

This note contains **aggregate results only**, reproduced from private benchmark and
candidate-score exports supplied by the maintainer. The private benchmark manifest,
photo identities, source keys, labels, model-score rows and local cache paths are
**not** version-controlled. Results apply to the frozen calibration partition and
are **not** final held-out validation.

## Evidence scope

- Archive eligible at benchmark selection: 27,052 photos with durable review proxies.
- Frozen benchmark: 400 representative photos, split into 240 calibration and
  160 held-out photos; 180 initial challenge cases, leaving 20 challenge reserve slots.
- Four score exports cover all 580 benchmark photos with **zero inference failures**.
- Human-labeled calibration representative photos usable for ordinal comparison:
  **238 / 240**; the other two are Unsure.
- Calibration quality tiers (only, no held-out/challenge label counts): Very Low 3,
  Low 38, Normal 154, High 42, Very High 1.
- Evaluation input: the durable `jpeg-1600-q78` proxies; initial learned runner
  provider: CPU, `pyiqa 0.1.16`.

## Maintainer calibration measurements

| Candidate | Spearman rho | Correct ordering when >=2 human tiers apart | Ties | Median inference per proxy | p95 |
| --- | ---: | ---: | ---: | ---: | ---: |
| WI-0182 technical reason-count | 0.234 | 943 / 2,379 (39.6%) | 1,378 | 24 ms | 37 ms |
| TOPIQ NR | 0.359 | 1,994 / 2,379 (83.8%) | 0 | 4,423 ms | 4,941 ms |
| MUSIQ | 0.370 | 1,970 / 2,379 (82.8%) | 0 | 2,686 ms | 2,910 ms |
| ARNIQA | 0.274 | 1,826 / 2,379 (76.8%) | 0 | 2,579 ms | 3,040 ms |

For 238 reviewed calibration images, TOPIQ and MUSIQ are a reasonable
**research shortlist**. TOPIQ is 1.0 percentage point ahead on the
clearly-separated pairwise metric; MUSIQ is 0.011 ahead in Spearman rho and
faster on this CPU. The pairwise metric contains repeated photos, not
2,379 independent samples.

### Paired uncertainty: TOPIQ versus MUSIQ

A paired **photo-level** bootstrap (1,000 resamples, fixed seed 20261017)
on the same reviewed calibration photos gives:

- MUSIQ minus TOPIQ in Spearman rho: **+0.0111**, descriptive 95% bootstrap
  interval approximately **[-0.0546, +0.0806]**.
- MUSIQ minus TOPIQ in pairwise accuracy: **-0.0101**, descriptive 95% bootstrap
  interval approximately **[-0.0590, +0.0368]**.

Both intervals include zero. This is not statistical evidence of
superiority for either candidate. These are percentile-bootstrap diagnostics,
not multiple-comparison adjusted hypothesis tests or deployment acceptance.
They were computed without consulting held-out/challenge human labels.

### Very Low safeguard

Only **3** calibration photos have Very Low human labels.

- TOPIQ's best observed calibration threshold flagged **1** photo with
  **1/1** Very Low matches: observed precision 100%, **Wilson 95% lower
  bound 20.7%**, recall 1/3.
- MUSIQ, ARNIQA and WI-0182 had no calibration threshold meeting the
  **98% observed-precision** diagnostic target.
- No model demonstrates safe 98% precision for normal automatic exclusion.
  **Do not enable exclusion** or declare a Very Low production threshold.
- Any ranking preference is a future product decision requiring held-out,
  ordinal-mapping, license and deployment evidence; it is not enabled by
  this evaluation.

## Outstanding evidence and gates

1. Record exact downloaded model checkpoint identities/SHA-256, byte sizes,
   independent model-weight license terms, Python/PyTorch versions and
   CPU/GPU/runtime constraints. Current learned exports explicitly say
   `weights-not-yet-pinned`.
2. Select up to 20 **new, non-benchmark** model-disagreement/rare-hard
   challenge cases from the maintained archive, append without changing the
   representative core, and review blindly. Existing 580 scored photos
   **cannot** be reused as those additional cases.
3. Run a bounded 50–100 paired-photo original-pixel versus review-proxy
   comparison on maintained originals (with explicit HEIC/RAW decoding and
   immutable source handling). Current learned exports measure proxies only.
4. Freeze the candidate, calibration map, thresholds or explicit no-go
   policy, and versioned evidence **before** any one-time held-out scoring
   against human labels.
5. Keep catalogue, search/collection selection and normal exclusion
   behavior unchanged throughout WI-0193 evaluation.

## Reproduction

Run `python tools/PhotoIdentity.PhotoQualityEvaluation/paired_calibration_confidence.py`
with `--input`, `--scores`, `--left`, and `--right` using private files;
see `docs/operations/photo-quality-benchmark.md` for the full Windows
PowerShell command. The output contains aggregate metrics and candidate names
only; preserve private benchmark and per-photo score exports outside Git.
