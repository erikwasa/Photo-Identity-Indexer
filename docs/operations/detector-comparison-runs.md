# Detector comparison runs

This procedure records the completed governed M16 YuNet confidence sweep. The immutable confidence-0.9 baseline and every isolated threshold candidate must remain available in the private detector-evaluation root as durable evidence.

## Final governed status

- Confidence `0.9`: immutable baseline; fully reviewed; failed the M16 gate.
- Confidence `0.8`: fully reviewed; failed the M16 gate.
- Confidence `0.7`: fully reviewed; failed the M16 gate.
- Confidence `0.6`: fully reviewed; failed the M16 gate.
- Confidence `0.5`: fully reviewed; failed the M16 gate.

The sweep completed on 2026-08-06. Do not rerun or overwrite any of these catalogues, logs, outputs, private comparisons or exports. Threshold tuning is insufficient; active detector work continues under WI-0036 using [Multi-scale detector runs](multiscale-detector-runs.md).

## Invariants retained by every candidate

Every candidate used:

- the exact 100 staged filenames;
- unchanged source bytes and full source SHA-256 values;
- the same private manifest metadata and countable-face rule;
- the frozen confidence-0.9 face-level ground truth;
- the same comparison IoU threshold, defaulting to `0.50`;
- the pinned YuNet and SFace model revisions;
- padding `0.25`; and
- unchanged preprocessing except for the confidence value under evaluation.

Comparison creation rejected any changed, missing, extra or duplicate source.

## Frozen baseline

The completed confidence-0.9 authored session was frozen once under:

```text
<DetectorEvaluationRoot>\ground-truth
```

It remains the reusable reference for later detector experiments. Do not create another baseline snapshot from a threshold candidate.

Freezing required all 100 photos to be complete and to satisfy:

```text
countable_faces = correct_or_background_detections + manually_marked_misses
```

## Isolated threshold catalogues

The completed runs used separate locations equivalent to:

```text
C:\PhotoIdentity\M16\runs\confidence-080\catalogue.db
C:\PhotoIdentity\M16\runs\confidence-070\catalogue.db
C:\PhotoIdentity\M16\runs\confidence-060\catalogue.db
C:\PhotoIdentity\M16\runs\confidence-050\catalogue.db
```

Each run retained a separate database, output directory, batch log and durable configuration record. No experiment detections were written to the canonical reviewed catalogue.

The historical processing command changed only confidence and path tags:

```powershell
$repo = "C:\Kod\codex\Photo Identity Indexer"
$sample = "C:\PhotoIdentity\M16\sample"
$confidence = 0.7
$confidenceTag = "070"
$candidateRoot = "C:\PhotoIdentity\M16\runs\confidence-$confidenceTag"
$candidateDb = Join-Path $candidateRoot "catalogue.db"
$candidateOutput = Join-Path $candidateRoot "outputs"
$candidateLog = Join-Path $candidateRoot "batch-start.log"

New-Item -ItemType Directory -Force `
    -Path $candidateRoot,$candidateOutput | Out-Null

if (Test-Path -LiteralPath $candidateDb) {
    throw "Candidate catalogue already exists: $candidateDb"
}

Set-Location -LiteralPath $repo

& dotnet run `
    --project .\src\PhotoIdentity.Cli `
    -- `
    batch start `
    --database $candidateDb `
    --source $sample `
    --output $candidateOutput `
    --detector-model yunet-2023mar-fp32 `
    --embedder-model sface-2021dec-fp32 `
    --confidence $confidence `
    --padding 0.25 2>&1 | Tee-Object -FilePath $candidateLog

if ($LASTEXITCODE -ne 0) {
    throw "Candidate processing failed for confidence $confidence."
}
```

Completed candidates must not be recreated with this command.

## Candidate attachment and review

Every completed processing run was attached to the same frozen baseline while the application used the same private detector-evaluation root.

Comparison creation:

- required the exact frozen photo set;
- verified every full source SHA-256;
- snapshotted candidate detections into the private comparison file;
- applied deterministic IoU matching; and
- surfaced only unmatched, duplicate or ambiguous components.

Clean one-to-one matches were counted automatically. Review decisions used these rules:

- match one candidate detection to one reference face when they represent the same face;
- classify an unmatched candidate as **False detection**;
- classify an additional detection of an already counted face as **Duplicate detection**; and
- mark a reference face as missed when no candidate represents it.

Corrections were saved atomically under:

```text
<DetectorEvaluationRoot>\comparisons
```

## Complete M16 gate

A candidate decision was recorded only after every exception was resolved and the material-category assessment was saved.

The fixed gate was:

- overall recall at least `90%`;
- five-plus-face recall at least `85%`;
- no more than `10` false or duplicate detections; and
- no material failure category incompatible with the intended archive workflow.

The comparison export retained overall, five-plus, source-group and primary-category summaries plus the four-part gate. Detailed files and exports remain private; Git contains only privacy-safe aggregate decisions.

## Final decision

All governed thresholds failed the complete gate. There is no approved threshold-only YuNet configuration. Preserve the records, close WI-0035 and continue with the governed multi-scale pipeline in WI-0036.
