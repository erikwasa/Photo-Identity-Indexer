# Operations documentation

Use this page to decide which runbook is current. Some files under `docs/operations` are intentionally retained as reproducible evidence for completed detector/model experiments; they are not normal operator instructions.

## Current operator path

- [Local operator guide](local-operator-guide.md) — authoritative day-to-day setup, application, permanent-archive and recovery path.
- [Windows operator package](windows-package.md) — self-contained `win-x64` package build, installation, durable-data boundary and side-by-side upgrade procedure.
- [SQLite persistence operations](sqlite-persistence.md) — current backup, restore, migration and locking policy.
- [Review-proxy serving and bounded originals](review-proxy-serving.md) — current archive storage/original-serving semantics.
- [Bounded archive acceptance](bounded-archive-acceptance.md) — active human gate while WI-0042 remains incomplete; after completion it remains the acceptance record/runbook.

## Conditional maintenance and engineering procedures

- [Review-proxy measurement](review-proxy-measurement.md) — calibration/measurement procedure for selecting or re-evaluating a proxy profile; not a routine daily task.
- [Detector pipeline rollout](detector-rollout.md) — maintenance-only migration procedure for an existing catalogue created with a different detector. New permanent-archive analysis already uses the governed CenterFace profile and does not require a rollout first.
- [Local evaluation workflow](local-evaluation.md) — specialized reproducible model-evaluation tooling. Its original examples use the historical YuNet pilot corpus; do not treat those detector settings as the permanent archive profile.
- [Multi-model comparison workflow](multi-model-comparison.md) — specialized embedding comparison workflow. The completed FP32/INT8 evidence used the earlier detector population; a future production-model reaffirmation must account for the selected CenterFace population.

## Retained M16 detector evidence

These files are historical governed experiment procedures. Keep them for reproducibility, but do not follow them as the current detector-selection sequence:

- [Detector recall pilot](detector-recall-pilot.md)
- [Detector comparison runs](detector-comparison-runs.md)
- [Multi-scale detector runs](multiscale-detector-runs.md)
- [CenterFace detector runs](centerface-detector-runs.md)

M16 is complete. CenterFace `centerface-2019-fp32`, confidence `0.5`, `single-pass`, is the selected permanent archive detector pipeline. YuNet threshold and multi-scale experiments are closed historical evidence.