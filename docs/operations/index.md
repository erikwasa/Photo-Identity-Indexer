# Operations documentation

Use this page to decide which runbook is current. Some files under `docs/operations` are intentionally retained as reproducible evidence for completed detector/model experiments or catalogue migration; they are not normal operator instructions.

## Current operator path

- [Local operator guide](local-operator-guide.md) — authoritative day-to-day setup, application, permanent-archive and recovery path.
- [Archive background synchronization](archive-background-sync.md) — current browser-triggered archive-sync behavior, durable background states, API compatibility and troubleshooting for long-running scans.
- [Windows operator package](windows-package.md) — self-contained `win-x64` package build, installation, durable-data boundary and side-by-side upgrade procedure.
- [PostgreSQL production operations](postgresql-operations.md) — current production startup/restart, logical backup, isolated restore verification, upgrade boundary and WI-0106 catch-up acceptance path.
- [PostgreSQL local runtime](postgresql-local-runtime.md) — Podman/WSL PostgreSQL service setup, localhost diagnostics and verification details.
- [Review-proxy serving and bounded originals](review-proxy-serving.md) — current archive storage/original-serving semantics.
- [Bounded archive acceptance](bounded-archive-acceptance.md) — retained permanent-archive acceptance record/runbook.

## Historical catalogue migration evidence

- [PostgreSQL catalogue migration and cutover](postgresql-catalogue-cutover.md) — accepted SQLite-to-PostgreSQL authority-transfer and rollback evidence from WI-0102. It is historical migration evidence, not a supported current catalogue-selection or daily-backup path.
- [Historical SQLite persistence record](sqlite-persistence.md) — retired persistence policy retained so completed delivery records remain understandable and link-valid.

PostgreSQL production operations own the current backup/restore procedure.

## Conditional maintenance and engineering procedures

- [FrameEvidence currentness and payload audit (WI-0190)](video-frame-evidence-audit.md) — safe read-only PostgreSQL inspection of a single frame batch, stale/exclusion detection and private cost-baseline guidance.
- [Human-annotated video scene coverage (WI-0190)](video-frame-scene-coverage.md) — read-only comparison of saved frame timestamps against operator scene boundaries, with numeric missed-scene counts and hypothetical interval/scene-balanced schedules.
- [Offline video-frame CLIP retrieval quality evaluation (WI-0190)](video-frame-retrieval-evaluation.md) — private synthetic/locally assembled ranking judgments with numeric-only Hit/MRR/Recall/MAP/nDCG across three diagnostic policies.
- [Live private video-frame CLIP retrieval benchmark (WI-0190)](video-frame-live-retrieval-evaluation.md) — read-only current PostgreSQL vectors, locally pinned CLIP text encoding and aggregate human-judged ranking metrics.
- [Private representative video-frame acceptance evidence (WI-0190)](video-frame-acceptance-evidence-report.md) — current batch storage/scene coverage and self-reported CPU/GPU/cost/caption metrics across bounded distinct clips.
- [Frame workspace crash recovery (WI-0190)](video-frame-scratch-crash-recovery.md) — operator-only age-gated, lock-aware, marker-owned scratch recovery after abnormal termination.
- [Opt-in annotated-scene FrameEvidence one-shot (WI-0190)](video-frame-evidence-annotated-one-shot.md) — persisted scene-balanced evidence on a selected current local proxy; manual cuts encoded in private regenerable sampling policy; operator disclosure required.
- [Bounded four-frame selected one-shot (WI-0190)](video-frame-four-scene-selected-one-shot.md) — explicit 1–4-frame interval/annotated-scene cap with unchanged 13-minute attempt, 15-minute grant and all-or-nothing v38 publication.
- [Consolidated measured frame acceptance and owned-child interruption (WI-0190)](video-frame-consolidated-acceptance-session.md) — strict operator preview/apply gates, process-local timing, honest GPU/Azure unknowns, disposable hard-stop and read-only recovery audit.
- [Approved pinned FFmpeg/FFprobe on Windows](ffmpeg-approved-video-tools.md) — persistent physical tool paths, digest verification, WinGet alias/reparse-point troubleshooting and read-only CLI helper.
- [Azure caption burst through a localhost bridge](azure-caption-burst-bridge.md) — operator-authorized temporary GPU acceleration for a generated-caption backlog while Photo Identity itself remains loopback-only; includes exact-digest checks, benchmark, production enable/monitor, inspection, shutdown/scale-to-zero and later reactivation.
- [Testing and CI strategy](testing-and-ci-strategy.md) — engineering policy for test-layer choice, integration-host isolation, flaky-test handling, PR gates and timing evidence.
- [Review-proxy measurement](review-proxy-measurement.md) — calibration/measurement procedure for selecting or re-evaluating a proxy profile; not a routine daily task.
- [Detector pipeline rollout](detector-rollout.md) — maintenance-only migration procedure for an existing catalogue created with a different detector. New permanent-archive analysis already uses the governed CenterFace profile and does not require a rollout first.
- [Whole-image embedding evaluation](whole-image-embedding-evaluation.md) — bounded WI-0127 exact-vector experiment for semantic retrieval, similar-photo retrieval and Creative Collection diversity; not a production vector-store procedure.
- [Local caption and narration evaluation](local-caption-narration-evaluation.md) — WI-0128 experiment evidence plus the retained loopback-only archive caption-enrichment policy; generated captions are revision-bound derived photo evidence and consumers do not trigger generation.

- [WI-0195 read-only collection shadow research](collection-shadow-simulation.md) — optional local offline simulator and private blinded shortlist comparison; no approved production quality/redundancy policy or collection writes.\n\n## Retained model-evaluation evidence

- [Historical local reviewed-catalogue evaluation workflow](local-evaluation.md) — pre-WI-0149 procedure retained for reproducibility principles; its SQLite/batch/match/export commands are retired.
- [Historical multi-model comparison workflow](multi-model-comparison.md) — accepted FP32/INT8 comparison evidence; the former automation is retired and its script path is only a historical tombstone.
- [Detector recall pilot](detector-recall-pilot.md)
- [Detector comparison runs](detector-comparison-runs.md)
- [Multi-scale detector runs](multiscale-detector-runs.md)
- [CenterFace detector runs](centerface-detector-runs.md)

M16 is complete. CenterFace `centerface-2019-fp32`, confidence `0.5`, `single-pass`, is the selected permanent archive detector pipeline. Historical YuNet threshold, multi-scale and embedding-comparison experiments are retained as evidence rather than current operator procedures.
