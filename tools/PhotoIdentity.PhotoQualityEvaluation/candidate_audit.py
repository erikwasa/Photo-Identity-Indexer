#!/usr/bin/env python3
"""Audit private WI-0193 candidate exports without revealing human/held-out labels."""
from __future__ import annotations

import argparse
import hashlib
import json
import math
from pathlib import Path
import statistics

LEVELS = {"very-low": 1, "low": 2, "normal": 3, "high": 4, "very-high": 5}


def load(path):
    with open(path, encoding="utf-8") as handle:
        return json.load(handle)


def ranks(values):
    order = sorted(range(len(values)), key=lambda i: (values[i], i))
    result = [0.0] * len(values)
    start = 0
    while start < len(order):
        end = start + 1
        while end < len(order) and values[order[end]] == values[order[start]]:
            end += 1
        for j in range(start, end):
            result[order[j]] = (start + end + 1) / 2
        start = end
    return result


def spearman(x, y):
    if len(x) < 2:
        return None
    a, b = ranks(x), ranks(y)
    mean_a, mean_b = statistics.mean(a), statistics.mean(b)
    numerator = sum((u - mean_a) * (v - mean_b) for u, v in zip(a, b))
    denominator = math.sqrt(sum((u - mean_a) ** 2 for u in a) *
                            sum((v - mean_b) ** 2 for v in b))
    return numerator / denominator if denominator else None


def audit(benchmark, exports):
    if not isinstance(benchmark.get("photos"), list) or not benchmark.get("benchmarkId"):
        raise ValueError("Invalid benchmark")
    photos = benchmark["photos"]
    ids = [p["revisionId"].lower() for p in photos]
    if len(ids) != len(set(ids)):
        raise ValueError("Duplicate benchmark revisions")
    expected = set(ids)
    # Human labels are strictly limited to the frozen calibration representative core.
    calibration = {p["revisionId"].lower(): LEVELS[p.get("review", {}).get("qualityLevel")]
                   for p in photos
                   if p.get("partition") == "calibration"
                   and p.get("sampleKind", "representative") != "challenge"
                   and p.get("review", {}).get("qualityLevel") in LEVELS}

    candidates = {}
    for export in exports:
        candidate = export.get("candidate") or {}
        key = candidate.get("candidateId")
        if not key or key in candidates:
            raise ValueError("Missing or duplicate candidate ID")
        if export.get("schemaVersion") != 1 or export.get("benchmarkId") != benchmark["benchmarkId"]:
            raise ValueError("Score export has incompatible benchmark/schema")
        scores = export.get("scores")
        if not isinstance(scores, list) or len(scores) != len(photos):
            raise ValueError("Candidate scores must cover full benchmark")
        mapping = {}
        for score in scores:
            revision = score["revisionId"].lower()
            if revision in mapping or revision not in expected:
                raise ValueError("Duplicate or foreign score revision")
            value = score.get("rawScore")
            if value is not None and (isinstance(value, bool) or not isinstance(value, (int, float))
                                      or not math.isfinite(value)):
                raise ValueError("Non-finite/invalid score")
            if value is None and not score.get("errorCode"):
                raise ValueError("Missing inference error code")
            mapping[revision] = (float(value) if value is not None else None)
        if set(mapping) != expected:
            raise ValueError("Missing benchmark score revisions")
        direction = 1 if export.get("higherIsBetter") is True else -1 if export.get("higherIsBetter") is False else None
        if direction is None:
            raise ValueError("Missing score direction")
        candidates[key] = (mapping, direction, candidate, export.get("runtime") or {})

    summaries = []
    for name, (scores, direction, descriptor, runtime) in candidates.items():
        valid = [(calibration[k], scores[k] * direction)
                 for k in sorted(calibration) if scores[k] is not None]
        correlation = spearman([v[0] for v in valid], [v[1] for v in valid])
        summaries.append({
            "candidate": name,
            "calibrationReviewed": len(calibration),
            "calibrationScored": len(valid),
            "calibrationSpearman": correlation,
            "fullBenchmarkInferenceFailures": sum(value is None for value in scores.values()),
            "runtimeMedianMs": runtime.get("medianInferenceMilliseconds"),
            "runtimeP95Ms": runtime.get("p95InferenceMilliseconds"),
            "modelSizeBytes": descriptor.get("modelSizeBytes"),
            "modelVersion": descriptor.get("modelVersion"),
            "provenanceComplete": (descriptor.get("modelSizeBytes") is not None
                                   and "not-yet-pinned" not in str(descriptor.get("modelVersion", ""))),
        })
    pairs = []
    names = sorted(candidates)
    for i, left in enumerate(names):
        for right in names[i + 1:]:
            a, direction_a = candidates[left][:2]
            b, direction_b = candidates[right][:2]
            common = sorted(k for k in calibration if a[k] is not None and b[k] is not None)
            agreement = spearman([a[k] * direction_a for k in common],
                                 [b[k] * direction_b for k in common])
            pairs.append({"left": left, "right": right,
                          "calibrationCommon": len(common),
                          "calibrationScoreRankCorrelation": agreement})

    return {
        "protocol": "wi0193-candidate-audit-v1",
        "benchmarkFingerprint": hashlib.sha256(benchmark["benchmarkId"].encode()).hexdigest()[:16],
        "representativeCalibrationReviewed": len(calibration),
        "candidates": summaries,
        "candidatePairs": pairs,
        "warning": ("Calibration-only diagnostic. No held-out or challenge human labels "
                    "read or reported. Model/threshold selection and Very Low exclusion "
                    "require additional evidence. Correlation does not establish safety.")
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", required=True, help="Private benchmark manifest")
    parser.add_argument("--scores", nargs="+", required=True, help="Full candidate score exports")
    args = parser.parse_args()
    result = audit(load(args.input), [load(item) for item in args.scores])
    print(json.dumps(result, indent=2, allow_nan=False))


if __name__ == "__main__":
    main()
