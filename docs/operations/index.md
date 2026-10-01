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

- [Azure caption burst through a localhost bridge](azure-caption-burst-bridge.md) — operator-authorized temporary GPU acceleration for a generated-caption backlog while Photo Identity itself remains loopback-only; includes exact-digest checks, benchmark, production enable/monitor, inspection, shutdown/scale-to-zero and later reactivation.
- [Testing and CI strategy](testing-and-ci-strategy.md) — engineering policy for test-layer choice, integration-host isolation, flaky-test handling, PR gates and timing evidence.
- [Review-proxy measurement](review-proxy-measurement.md) — calibration/measurement procedure for selecting or re-evaluating a proxy profile; not a routine daily task.
- [Detector pipeline rollout](detector-rollout.md) — maintenance-only migration procedure for an existing catalogue created with a different detector. New permanent-archive analysis already uses the governed CenterFace profile and does not require a rollout first.
- [Whole-image embedding evaluation](whole-image-embedding-evaluation.md) — bounded WI-0127 exact-vector experiment for semantic retrieval, similar-photo retrieval and Creative Collection diversity; not a production vector-store procedure.
- [Local caption and narration evaluation](local-caption-narration-evaluation.md) — WI-0128 experiment evidence plus the retained loopback-only archive caption-enrichment policy; generated captions are revision-bound derived photo evidence and consumers do not trigger generation.

## Retained model-evaluation evidence

- [Historical local reviewed-catalogue evaluation workflow](local-evaluation.md) — pre-WI-0149 procedure retained for reproducibility principles; its SQLite/batch/match/export commands are retired.
- [Historical multi-model comparison workflow](multi-model-comparison.md) — accepted FP32/INT8 comparison evidence; the former automation is retired and its script path is only a historical tombstone.
- [Detector recall pilot](detector-recall-pilot.md)
- [Detector comparison runs](detector-comparison-runs.md)
- [Multi-scale detector runs](multiscale-detector-runs.md)
- [CenterFace detector runs](centerface-detector-runs.md)

M16 is complete. CenterFace `centerface-2019-fp32`, confidence `0.5`, `single-pass`, is the selected permanent archive detector pipeline. Historical YuNet threshold, multi-scale and embedding-comparison experiments are retained as evidence rather than current operator procedures.
