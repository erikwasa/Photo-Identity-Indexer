# Private provisional face-cluster and suggestion evaluation

This tooling supports private reviewed-data evaluation for WI-0113, WI-0116, WI-0081 and WI-0117. It never creates production clusters or canonical identity assignments; suggestion and automatic-assignment evaluators are read-only with respect to production behavior.

## Privacy boundary

The exported JSON contains face embeddings and is biometric/private data. Keep the sample and generated reports under `private/`, `data/`, or another location outside the repository. Never commit them.

The C# exporter intentionally omits source paths, filenames, crop paths, person names, policy actors, and raw catalogue identifiers from the file. Canonical person IDs are converted to deterministic local labels such as `person-0001`; face and photo IDs are similarly replaced with local sample labels.

For WI-0081/WI-0117 the backward-compatible export also includes the exact suggestion policy plus detector confidence, normalized face area, review time and whether the current reviewed Person has merge history. These fields are private derived evaluation metadata; they do not change production state.

## 1. Export one exact-model reviewed sample

Set the same local PostgreSQL connection string used by Photo Identity:

```powershell
$env:PhotoIdentity__Postgres__ConnectionString = "Host=localhost;Port=5432;Database=photoidentity;Username=...;Password=..."
```

Then export the production embedding model revision. The default output is already ignored by Git. For WI-0081/WI-0117 prefer bounds large enough to include the entire reviewed exact-model target corpus and production confirmed-reference population; each is capped at 20,000 rows.

```powershell
dotnet run --project tools/PhotoIdentity.ClusterEvaluation -- `
  --model-id sface-2021dec-fp32 `
  --model-hash <exact-model-sha256> `
  --max-faces 20000 `
  --max-references 20000 `
  --output private/cluster-evaluation/sample.json `
  --force
```

The existing `faces` field remains the backward-compatible reviewed target sample used by the WI-0113/WI-0116 evaluators. Assigned targets are labelled by canonical Person; canonical Unknown targets are included by default as unlabeled discovery/risk-probe rows. Pass `--exclude-unknown` only for a controlled comparison. Rejected and unreviewed faces are not target ground truth.

For WI-0081/WI-0117, `referenceFaces` separately carries the same exact-model confirmed reference population used by production matching: current reviewed assignments plus legacy `confirmed` person labels that have no review action, with merged People excluded. Face IDs are pseudonymized consistently across `faces` and `referenceFaces`, so a reviewed target can be removed from its own holdout reference set without exposing catalogue IDs.

Person maintenance consolidates review actions onto the surviving merge target. The exporter therefore flags reviewed/reference faces whose current Person has absorbed at least one Person merge. This makes merge-heavy identities visible as an audit segment without exposing names or IDs; it does not claim which individual face originally belonged to a merged source Person.

## 2. Create an isolated Python environment

```powershell
py -m venv private/cluster-evaluation/.venv
& private/cluster-evaluation/.venv/Scripts/Activate.ps1
python -m pip install -r tools/cluster-evaluation/requirements.txt
```

The evaluators use NumPy/scikit-learn over the exported exact-model embeddings.

## 3. WI-0113 clustering-policy evaluation

```powershell
python tools/cluster-evaluation/evaluate.py `
  private/cluster-evaluation/sample.json `
  --report-json private/cluster-evaluation/report.json `
  --report-md private/cluster-evaluation/report.md `
  --expected-face-count 10000
```

The default grid intentionally explores conservative neighbourhoods. Override the comma-separated grids when the first pass shows that the useful decision boundary lies elsewhere.

The WI-0113 report separates false merges, false splits, coverage/noise, same-photo conflicts and rough scaling cost. Unknown faces affect discovery coverage/noise but are excluded from identity-labelled false-merge/false-split denominators because `Unknown` is not a person identity.

## 4. WI-0116 cluster-assisted known-person advisory evaluation

Use the same private sample to evaluate the fixed production DBSCAN policy together with the initial advisory rule:

```powershell
python tools/cluster-evaluation/evaluate_advisory.py `
  private/cluster-evaluation/sample.json `
  --report-json private/cluster-evaluation/advisory-report.json `
  --report-md private/cluster-evaluation/advisory-report.md
```

The WI-0116 evaluator intentionally fixes clustering to the production defaults (`eps=0.30`, `min_samples=3`) and evaluates advisory identity evidence separately from canonical assignment. For each sample face it simulates ordinary known-person ranking by taking the best cosine similarity to another reviewed exemplar of each Person; the target face itself is excluded so a face cannot vote for itself.

The committed production advisory rule starts conservatively:

- a member vote must meet the existing ordinary Medium score threshold (default `0.50`),
- at least 3 independent members must favor the same Person,
- those votes must cover at least 60% of the discovered cluster,
- Core members must cover at least 60% of the cluster,
- a competing Person with more than 1 qualifying vote or more than 20% cluster support makes the result ambiguous,
- production `not same` evidence also forces ambiguity/fail-closed behavior; the private export does not contain those constraints, so that path is covered by automated production tests instead.

The private report records Strong / Ambiguous / Insufficient advisory counts, false-person proposals and conservative precision, opportunity recall, mixed-cluster behavior and estimated review-task compression. A Strong proposal counts as correct only when every reviewed member in that discovered cluster has one canonical identity and the advisory candidate matches it.

## 5. WI-0081 suggestion-accuracy investigation

Run the suggestion evaluator against the same exact-model export:

```powershell
python tools/cluster-evaluation/evaluate_suggestions.py `
  private/cluster-evaluation/sample.json `
  --report-json private/cluster-evaluation/suggestion-accuracy-report.json `
  --report-md private/cluster-evaluation/suggestion-accuracy-report.md `
  --reference-cap 8
```

The evaluator performs four read-only leave-one-out comparisons. All scenarios rank the reviewed targets against `referenceFaces` rather than assuming the target sample is the production reference population:

- **production-reference max exemplar** uses the same exact-model confirmed reference population and best-exemplar-per-Person aggregation as production matching, with the target itself removed;
- **duplicate-resistant max exemplar** additionally removes every reference from the target's exact-content group so the same image content cannot inflate holdout accuracy;
- **duplicate-resistant centroid** compares one normalized per-Person prototype instead of an unbounded nearest exemplar set;
- **duplicate-resistant quality-diverse cap** compares a bounded per-Person reference set selected by detector confidence/face area plus embedding diversity.

Historical target-specific rejected-Person filters are intentionally not replayed in the holdout evaluator. Reviewed targets are being used to measure identity evidence/reference behavior, not to reconstruct their past interaction state. This distinction is recorded in every report.

The duplicate-resistant max-exemplar result is the primary WI-0081 baseline. The production-reference row is retained because the difference between it and the duplicate-resistant baseline is evidence about exact-image-content leakage into the holdout.

The report includes current top-k accuracy, High/Medium policy behavior, reviewed-Unknown emission, genuine/impostor score behavior, segmentation by detector confidence/face size/reference-set size/chronology, reference concentration and offline mitigation deltas.

### Corrected exact-content source-copy audit

The initial WI-0081 report grouped references by `contentGroup`. A `contentGroup` identifies exact source-image content, so all faces in one ordinary group photo share that value. Counting every content group with multiple face rows as a duplicate therefore overstates duplicate/cross-label contamination.

Use the dedicated source-content audit for duplication evidence:

```powershell
python tools/cluster-evaluation/audit_suggestion_content.py `
  private/cluster-evaluation/sample.json `
  --report-json private/cluster-evaluation/suggestion-content-audit.json `
  --report-md private/cluster-evaluation/suggestion-content-audit.md
```

This audit treats exact content as repeated only when one `contentGroup` spans more than one pseudonymized `photoGroup` (asset revision). It separately reports same-identity reference evidence repeated across those exact-content source copies. It also counts cross-label near-duplicate face-pair candidates only when references come from different photo revisions of the same exact content and have cosine similarity at least `0.95`. Those pairs are review candidates, not automatic proof of an incorrect label.

The `duplicateReferenceContentGroups`, `crossLabelDuplicateReferenceGroups`, and `assignedUnknownMixedTargetContentGroups` fields emitted by the original `evaluate_suggestions.py` report should therefore not be interpreted as source-copy contamination evidence. They are retained only for backward compatibility with already-generated private reports.

Known faces with no remaining same-Person holdout reference are reported separately rather than counted as matching failures. Unknown faces have no labelled identity, so an emitted suggestion is a risk indicator rather than proof of an incorrect identity. Detector confidence and face area are queue-composition proxies, not direct embedding-quality labels. If either target/reference bound truncates the catalogue, record that selection caveat. If more than one embedding model hash is present historically, repeat the workflow separately for each model revision rather than pooling revisions.

Do **not** tune production thresholds merely to improve one aggregate metric. A candidate mitigation should improve or preserve false-positive-sensitive measures, especially High precision and Unknown High emission, and should be selected by the maintainer before production behavior changes.

## 6. WI-0117 multi-evidence automatic-assignment evaluation

WI-0117 reuses the same `sample.json`; no additional export is required for the first evaluation slice.

```powershell
python tools/cluster-evaluation/evaluate_auto_assignment.py `
  private/cluster-evaluation/sample.json `
  --report-json private/cluster-evaluation/auto-assignment-report.json `
  --report-md private/cluster-evaluation/auto-assignment-report.md
```

The evaluator does **not** lower the global High threshold or replace max-exemplar matching. Instead it asks whether targets that are not already current High can safely gain automatic assignment when multiple independent signals agree.

The evaluation uses duplicate-resistant production references and a deterministic exact-content split: SHA-256 of each target's exact-content group maps 60% of groups to policy selection and 40% to holdout validation. Exact copies cannot cross the split. Candidate rules are screened on the selection partition before holdout results are emitted.

Three candidate families are explored:

- **multi-reference** requires the same rank-1 Person to be supported by multiple independent reference content groups at Medium-or-stronger score gates;
- **cluster-corroborated** requires target-specific Strong DBSCAN cluster support for the same rank-1 Person, excluding the target itself and every member with the same exact image content from the vote;
- **cluster-plus-multi-reference** requires both independent reference and Strong cluster corroboration.

Every candidate is a union with the current High rule: current High assignments remain the baseline and the report measures only incremental additions beyond it. A `strict-no-extra-false` candidate adds assignments on the selection split without increasing either wrong-known or reviewed-Unknown assignments. A `rate-preserving` candidate keeps known precision, conservative precision and reviewed-Unknown assignment rate at least as strong as the current High baseline. Only those candidate classes are shortlisted automatically for holdout validation.

The report includes absolute and incremental false assignment counts, conservative precision, known precision/coverage, reviewed-Unknown assignment rate, and estimated review actions saved. The automatically selected first candidate is also segmented on holdout weak-detector, tiny-face and sparse-reference populations to preserve the WI-0081 quality guardrails.

The private export does not contain durable `not same` cluster constraints. Production WI-0116 evidence already fails closed when they exist; the private WI-0117 evaluator deliberately does not assume that unavailable negative evidence, making the simulation more permissive on that dimension.

This is an evaluation gate only. Do not change production automatic assignment from this report alone. If no candidate preserves the false-assignment/Unknown guardrails, WI-0117 can close with the current High-only policy unchanged.

## 7. Record decisions without committing private data

For WI-0113, record the selected clustering policy and aggregate evaluation conclusion. For WI-0116, record only aggregate advisory precision/recall/review-effort results and the maintainer decision about whether the initial advisory thresholds are acceptable. For WI-0081, record only aggregate baseline/segmentation/mitigation findings, the corrected source-content audit, and the selected implementation direction. For WI-0117, record only aggregate baseline/candidate/holdout metrics and the explicit decision to broaden automation or keep current High-only behavior.

The committed repository may contain non-personal aggregate metrics and policy conclusions, but never the sample, embeddings, crops, face/person mappings, or private per-face/per-cluster reports.
