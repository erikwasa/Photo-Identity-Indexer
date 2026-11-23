# Local evaluation workflow — historical reviewed-catalogue procedure

> **Historical procedure.** This page records the reviewed-catalogue evaluation workflow used before WI-0149 retired the SQLite-opening `batch`, `match regenerate` and `evaluate export` CLI paths. Do not copy these commands into current operation.

The retained evidence is useful for understanding how earlier model comparisons fixed immutable source scope, exact detector/embedder revisions, deterministic split identity and held-out reporting. The original procedure operated against the then-canonical SQLite catalogue and produced private deterministic manifests/reports outside Git.

## Historical evaluation boundary

An accepted run fixed:

- the canonical catalogue snapshot used at that time;
- immutable source and asset revisions;
- detector model ID and SHA-256;
- embedder model ID and SHA-256;
- alignment and preprocessing contracts;
- dataset ID and pipeline version;
- split seed and split counts; and
- the processing run that produced the selected embeddings.

Human-confirmed assignments were canonical. Suggestions and evaluation outputs were derived, model-versioned evidence. Validation selected thresholds; the held-out test split reported final results without selecting replacement thresholds.

## Current supported evaluation surface

WI-0149 removed the migration-era catalogue export/regeneration commands rather than porting them to PostgreSQL because they were no longer part of supported application operation. Current CLI help is authoritative:

```powershell
dotnet run --project src/PhotoIdentity.Cli -- --help
```

The provider-neutral `evaluate --dataset ...` command remains available for an already prepared neutral evaluation dataset. New catalogue-backed evaluation features should be added against PostgreSQL/provider-neutral contracts rather than reviving the retired SQLite command surface.

Current bounded semantic/image/caption evaluation commands that explicitly accept `--postgres-connection-env` are separate PostgreSQL-backed engineering workflows and document their own input/output boundaries.

## Historical evidence principles retained

For any future same-corpus model comparison, preserve the principles that made the old procedure reproducible:

- fix exact model IDs and hashes;
- fix immutable revision scope;
- use deterministic ordering/split identity;
- select thresholds from validation data only;
- report held-out results separately;
- compare repeated output hashes when deterministic bytes are expected; and
- keep private catalogues, images, crops, embeddings, names and per-person reports outside Git.

The earlier FP32/INT8 and detector experiments remain governed evidence under their completed work items/runbooks. They are not instructions to restore the retired catalogue provider or CLI commands.

## Related references

- [Local operator guide](local-operator-guide.md)
- [Operations documentation map](index.md)
- [Evaluation method](../models/evaluation-method.md)
- [Model manifests and governance](../models/model-governance.md)
- [Recognition and identity matching](../architecture/identity-matching.md)
- [Historical SQLite persistence record](sqlite-persistence.md)
