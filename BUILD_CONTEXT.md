# Build context

Formal lifecycle status is resolved through `PhotoIdentity.Docs`; read the target shard and linked document before continuing.

## Current focus

**WI-0193 checkpoint 7 pending review (2026-10-17):** The maintainer
completed blinded reviews of the frozen 600-photo benchmark and successfully
reconciled the four full candidate exports (all with zero inference
failures). Private-manifest inspection confirmed 240/240 calibration and
200/200 challenge ratings recorded, including the 20 TOPIQ/MUSIQ
disagreement cases; **no held-out quality-rating values were inspected**.
Only 238 calibration ratings are known-tier. The candidate audit retains
MUSIQ rho=0.370, TOPIQ rho=0.359, ARNIQA rho=0.274, WI-0182 rho=0.234.
Automatic Very Low exclusion remains unsupported and no model is chosen.

The new `original_proxy_study.py` supports private deterministic
50–100 image paired original-decode vs immutable proxy scoring using the
TOPIQ/MUSIQ runner, SHA verification, format-balanced selection from
**non-held-out** partitions, and aggregate report. Optional `pillow-heif`
and `rawpy` dependencies are local only. Document codec/color/rendering
differences from production and *do not* equate the study with pure
downscale effects. Maintainer run and checkpoint provenance/license audit
pending. Next freeze an explicit policy before a one-shot held-out analysis.
M37 PRs target `features/m37`, not main.


**WI-0193 checkpoint 6 pending review:** `reconcile_quality_scores.py` now
extends the frozen 580-photo learned-IQA score exports to 600 **only after**
the extra 20 rank-disagreement challenge photos are imported. It requires an
explicit 580-photo pre-import snapshot, validates unchanged frozen
representatives, benchmark identity and exact imported candidates. TOPIQ/MUSIQ
reuse the earlier separately scored screening-pool rows; ARNIQA can score only
the 20 selected proxies; the WI-0182 baseline is cheap to rerun on 600.
All old score exports remain unchanged and new private 600-photo exports have
strict model version/provider/coverage checks. See
`docs/operations/photo-quality-benchmark.md` checkpoint 6 for the ordered
PowerShell procedure, especially the mandatory separate pre-import backup:
the legacy `.before-challenge.bak` may already exist from WI-0192.
Maintainer archive screening/import and score reconciliation are **not yet run**.
No held-out metrics or model/threshold policy freeze. No production changes.


**Checkpoint 5 pending review:** The existing catalogue-backed challenge generator now
supports bounded `--pool-only` screening of fresh, non-benchmark images, excluding
benchmark content SHA and deduplicating pool content. `disagreement_challenges.py`
scores this pool (distinct input kind) with TOPIQ/MUSIQ and selects up to 20
private, blinded candidate imports by within-pool normalized-rank disagreement,
with deterministic tie-breaking. No maintained-archive disagreement candidates
have yet been run/imported/reviewed. Importing 20 expands the benchmark to
600: the four existing 580-photo candidate exports will need explicit
reconciliation against that new benchmark before the final held-out decision.
No automatic exclusion, production scoring, or model selection is enabled.
M37 checkpoint PRs target `features/m37`, not `main`.


**2026-10-17 evidence:** WI-0193 now has full private-archive benchmark score exports
for WI-0182, TOPIQ NR, MUSIQ and ARNIQA (580/580 each, zero failures), with 238
known-tier representative calibration reviews. MUSIQ rho=0.370/pairwise=82.8%;
TOPIQ rho=0.359/pairwise=83.8%; bootstrap differences include zero. Very Low
precision is unproven (only 3 calibration Very Low labels); automatic exclusion
remains **no-go**. The aggregate-only evidence is documented in
`docs/operations/photo-quality-model-evidence-2026-10-17.md`. The added
`paired_calibration_confidence.py` produces uncertainty estimates without
consulting held-out labels. Next gates: weight hashes/licenses, 20 new
non-benchmark disagreements, original-versus-proxy comparison, frozen
calibration, one-shot held-out. No production model selected. M37 PRs target
`features/m37`, not main.

WI-0193 / issue #536 is in progress on the M37 integration branch `features/m37`. Checkpoint 1 establishes a benchmark-bound candidate score schema, a fixed WI-0182 technical-reason-count baseline and calibration-only rank/pairwise/Very-Low safety metrics with Wilson uncertainty. Held-out human labels remain excluded from evaluation until a candidate and calibration policy are frozen. WI-0192 is maintainer-accepted: 400 representative photos (240 calibration / 160 held-out) plus 180 challenge photos from 27,052 durable-proxy candidates, with 20 slots reserved for WI-0193 model-disagreement cases; 255 blinded ratings were completed at WI-0192 acceptance and the sample was judged representative. Legacy original width/height remain a documented limitation. M37 checkpoint PRs target `features/m37`, not `main`.


WI-0182 / issue #490: PR #509 implements the evaluation-only technical-quality harness over durable review proxies. It adds versioned raw sharpness/exposure/clipping/contrast/resolution evidence, q78 profile-gated candidate reasons, deterministic PostgreSQL proxy sampling, private JSON review export, timing/storage measurements and synthetic analyzer coverage. No collection semantics or catalogue quality persistence are introduced. Keep WI-0182 in review until representative private-archive review/runtime evidence is recorded.

WI-0186 / issue #512: PR #515 scopes Visual/Caption/Combined search through an optional saved Smart Collection without weakening exact Smart Collection semantics. The server resolves one eligibility set, applies it before semantic top-k and inside caption SQL, and keeps explicit saved search-result collections immutable. PR #517 corrected the large-scope PostgreSQL timeout. The 2026-10-13 maintainer retest succeeded for Fideli (5,638 eligible) in all three modes and Mor och Dotter (1,057 eligible); provenance badges behave as expected. Keep WI-0186 in review until independent exact-membership and saved-list restart verification is recorded.

WI-0187 / issue #513: PR #516 lets Creative Collections use either the existing exact Smart Collection anchor or a versioned ranked Search anchor with Visual/Caption/Combined mode, optional Smart scope and bounded top-N admission. Search anchor materialization reuses WI-0186 retrieval, then enters the existing one-level moment/context and deterministic Creative selection pipeline. The maintainer now reports the search-anchor workflow works as expected. Keep WI-0187 in review pending the specific two-recipe, legacy compatibility and snapshot-immutability evidence in its acceptance criteria.

M30: WI-0150 completed, ADR-0012 accepted, and WI-0151 in progress. The maintainer's 2026-10-15 pilot verified 25 locally available videos (1,284 online-only untouched), 16 correct SDR posters across H.264/HEVC Main and 9 deliberately deferred HEVC Main 10 HLG clips, zero reported failures and no unexpected hydration. Anonymous evidence is documented in docs/operations/video-pilot-evidence-2026-10-15.md. Checkpoint 2 adds PostgreSQL v33 photo-default media-kind and revision-bound video metadata/poster schema with isolation tests. The CLI remains isolated: no automatic video writes, no gallery cards, and no photo-only query changes yet.

## Maintainer acceptance — 2026-10-04 (Europe/Stockholm)

The maintainer completed the current maintained UI review batch:

- WI-0165 is accepted after PR #505; slideshow counts, player-triggered **Prepared** continuity, reload/revalidation restoration and stale-state behavior work as expected.
- WI-0178 is accepted after PR #501; the maintained desktop and phone/PWA slideshow-library search/filter experience works as expected.
- WI-0185 is accepted after PRs #506, #507 and #508; the date-only audit filters and the remaining cross-person assignment audit workflow work as expected.

WI-0165, WI-0178 and WI-0185 are completed. M35 is completed. Issues #499 and #503 are resolved; issue #481 was already closed by PR #501 and now has maintained acceptance evidence.

The maintainer previously accepted WI-0163, WI-0167, WI-0169, WI-0170, WI-0171, WI-0172, WI-0173, WI-0175, WI-0176, WI-0177, WI-0179, WI-0180, WI-0181, WI-0183 and WI-0184.

## Next concrete step

WI-0193 checkpoint 1 is the current M37 task. After the calibration protocol merges, run the fixed WI-0182 baseline on the private benchmark, then add at least three justified learned no-reference IQA candidates (TOPIQ, MUSIQ, ARNIQA or deployable equivalents), reserve the remaining challenge slots for model disagreements, and compare proxy versus decoded-original input before freezing a calibration policy. WI-0194 remains independently ready.

WI-0151: maintained PostgreSQL v33 accepted, and PR #540 merged opt-in dry-run-first MOV/MP4 local ingestion. Current checkpoint implements conservative embedded capture/GPS/camera extraction with explicit timestamp timezone/provenance, strict ISO-6709 coordinates and pilot-report privacy. Next: revision-safe durable poster files and exclusion/deletion cleanup, then safe browsing, full photo-only regression and final maintainer real-file acceptance. No automatic video worker; the maintainer prefers one final review after the entire work item is built.

## Relevant pointers

- docs/delivery/work-items/WI-0186-scoped-semantic-caption-search.md
- docs/operations/semantic-caption-photo-search-evaluation.md
- docs/delivery/work-items/WI-0187-search-creative-anchors.md
- docs/operations/creative-collection-preview.md
- docs/delivery/work-items/WI-0182-photo-quality-scoring-evaluation.md
- docs/operations/photo-quality-evaluation.md
- docs/decisions/ADR-0012-isolated-video-media-architecture.md
- docs/operations/video-codec-survey.md
- docs/operations/video-codec-survey-evidence-2026-10-14.md
- docs/operations/video-pilot-evidence-2026-10-15.md

\nWI-0193 checkpoint 2 adds the private, explicit opt-in Python `pyiqa`
runner for `topiq_nr`, `musiq`, and `arniqa` with SHA-verified
benchmark proxies and schema-compatible scored exports. The runner has offline
contract tests and the maintainer has now run the full private 580-photo benchmark;
exact model checkpoint hashes/licenses, original comparison and held-out decision
remain outstanding. Do not confuse candidate support with
model acceptance.

WI-0193 checkpoint 3 adds `candidate_audit.py` and offline tests for exact full-score
coverage and calibration-only comparative metrics. Full learned-model runs are complete;
weight hash/license validation, original-vs-proxy checks, disagreement challenge sampling
and one-shot held-out evaluation are still outstanding. Do not choose a production model based
on a framework's model name or on an unverified score export.
