# Multi-model comparison workflow — historical evidence

> **Historical procedure.** This page documents the completed reviewed-catalogue model-comparison workflow used before WI-0149. Its original automation depended on the SQLite catalogue plus `batch`, `match regenerate` and `evaluate export`, all of which were retired from the active CLI. `Invoke-MultiModelComparison.ps1` is now a non-operational historical tombstone that retains `-SelfTest` only so old references remain verifiable.

## Accepted comparison boundary

The completed workflow compared embedding-model revisions while keeping fixed:

- source root and immutable asset revisions;
- detector model ID and exact hash;
- face-alignment protocol;
- people, confirmed assignments, rejections and append-only review history;
- evaluation dataset ID, pipeline version, split seed, split counts and threshold sweep; and
- manual-review procedure.

Only the embedding-model revision changed.

The accepted first comparison used:

| Role | Baseline | Candidate |
|---|---|---|
| Detector | `yunet-2023mar-fp32` | `yunet-2023mar-fp32` |
| Embedder | `sface-2021dec-fp32` | `sface-2021dec-int8` |
| Alignment | `sface-five-point-v1` | `sface-five-point-v1` |

A model revision meant the model ID plus exact SHA-256 hash and preprocessing contract. Scores and thresholds from different revisions were not treated as interchangeable.

## What the retired automation did

The historical workflow:

- validated pinned detector/embedder manifests and installed hashes;
- created a content-hashed source snapshot;
- created a stopped-state backup of the then-canonical SQLite catalogue;
- processed the same source/catalogue scope with each configured embedder;
- asserted identical immutable-revision scope and detector-derived face counts;
- regenerated exact-model suggestions repeatedly and rejected unstable results;
- exported/evaluated deterministic splits repeatedly and rejected nondeterministic bytes;
- asserted identical gallery, validation and held-out test splits; and
- wrote aggregate metrics, storage measurements and exact model provenance to a private workspace.

Human Windows/phone inspection, representative disagreement review and the final recommendation remained explicit maintainer gates.

## Accepted FP32-versus-INT8 outcome

The accepted private same-corpus comparison kept detector, source scope, alignment, dataset, seed and review history fixed. A manual review of representative faces found no material practical identification/review-quality advantage for the INT8 SFace candidate, so `sface-2021dec-fp32` remained the selected default for that comparison boundary.

This historical conclusion does not claim that the earlier YuNet face population represents later CenterFace production detections. Any future production-model reaffirmation must use current governed data and supported PostgreSQL/provider-neutral tooling.

## Current status

Do not create a new configuration for `Invoke-MultiModelComparison.ps1`; it intentionally fails for normal execution after WI-0149. Current CLI help is authoritative:

```powershell
dotnet run --project src/PhotoIdentity.Cli -- --help
```

If a new cross-model comparison workflow is needed, implement it against PostgreSQL/provider-neutral contracts and current detector populations rather than restoring the retired SQLite catalogue commands.

The historical script path remains self-testable for compatibility with old governance references:

```powershell
powershell.exe -NoProfile -File .\Invoke-MultiModelComparison.ps1 -SelfTest
pwsh -NoProfile -File .\Invoke-MultiModelComparison.ps1 -SelfTest
```

## Privacy/evidence rule

Historical and future comparison workspaces are private. Do not commit photos, names, face IDs, catalogue backups, snapshots, raw manifests, reports, embeddings, local paths or per-person confusion rows.

## Related references

- [Local operator guide](local-operator-guide.md)
- [Historical local evaluation workflow](local-evaluation.md)
- [Baseline models](../models/baseline-models.md)
- [Candidate models](../models/candidate-models.md)
- [Model manifests and governance](../models/model-governance.md)
- [Recognition and identity matching](../architecture/identity-matching.md)
- [Historical SQLite persistence record](sqlite-persistence.md)
