#!/usr/bin/env python3
"""Paired-photo bootstrap on blinded representative CALIBRATION labels only.

This is a diagnostic confidence interval, not a held-out decision, an automatic
exclusion threshold, or a test of deployment readiness. No per-photo data is
printed. Standard library only.
"""
from __future__ import annotations

import argparse
import json
import random

from candidate_audit import LEVELS, audit, load, spearman


def pairwise_accuracy(labels, scores):
    correct = total = ties = 0
    for i in range(len(labels)):
        for j in range(i + 1, len(labels)):
            if abs(labels[i] - labels[j]) < 2:
                continue
            total += 1
            d = (labels[i] - labels[j]) * (scores[i] - scores[j])
            correct += d > 0
            ties += d == 0
    return correct / total if total else None, total, ties


def percentile(values, fraction):
    sorted_values = sorted(values)
    return sorted_values[min(len(values) - 1, int(fraction * len(values)))]


def confidence(benchmark, exports, left, right, repetitions=1000, seed=20261017):
    # Fail closed on candidate identity, benchmark identity and complete score
    # coverage. Audit itself never reads held-out or challenge human labels.
    audit(benchmark, exports)
    if left == right:
        raise ValueError("Candidate IDs must differ")
    by_name = {e["candidate"]["candidateId"]: e for e in exports}
    if left not in by_name or right not in by_name:
        raise ValueError("Both selected candidate IDs must occur in --scores")
    if not 100 <= repetitions <= 10000:
        raise ValueError("Repetitions must be between 100 and 10000")

    # Order of access is intentional: NEVER look at a review outside calibration.
    calibration = [
        p for p in benchmark["photos"]
        if p.get("partition") == "calibration"
        and p.get("sampleKind") == "representative"
        and p.get("review", {}).get("qualityLevel") in LEVELS
    ]
    score_maps = {}
    for name in (left, right):
        export = by_name[name]
        sign = 1 if export["higherIsBetter"] else -1
        score_maps[name] = {
            s["revisionId"].lower(): s["rawScore"] * sign
            for s in export["scores"] if s["rawScore"] is not None
        }
    common = [p for p in calibration if all(
        p["revisionId"].lower() in score_maps[name] for name in (left, right))]
    if len(common) < 10:
        raise ValueError("At least 10 jointly scored, rated calibration photos required")
    labels = [LEVELS[p["review"]["qualityLevel"]] for p in common]
    a = [score_maps[left][p["revisionId"].lower()] for p in common]
    b = [score_maps[right][p["revisionId"].lower()] for p in common]

    left_rho = spearman(labels, a)
    right_rho = spearman(labels, b)
    left_pair, pair_count, left_ties = pairwise_accuracy(labels, a)
    right_pair, _, right_ties = pairwise_accuracy(labels, b)
    if any(x is None for x in (left_rho, right_rho, left_pair, right_pair)):
        raise ValueError("Calibration label/score variation is insufficient")

    rng = random.Random(seed)
    rho_deltas = []
    pair_deltas = []
    for _ in range(repetitions):
        sample = [rng.randrange(len(common)) for _ in common]
        yl = [labels[i] for i in sample]
        al = [a[i] for i in sample]
        bl = [b[i] for i in sample]
        ra = spearman(yl, al)
        rb = spearman(yl, bl)
        pa, _, _ = pairwise_accuracy(yl, al)
        pb, _, _ = pairwise_accuracy(yl, bl)
        if ra is None or rb is None or pa is None or pb is None:
            continue
        rho_deltas.append(rb - ra)
        pair_deltas.append(pb - pa)
    if len(rho_deltas) < 0.9 * repetitions:
        raise ValueError("Too many degenerate bootstrap samples")
    return {
        "protocol": "wi0193-paired-calibration-bootstrap-v1",
        "scope": "representative calibration only; no held-out or challenge labels accessed",
        "leftCandidate": left,
        "rightCandidate": right,
        "commonCalibrationPhotos": len(common),
        "twoTierPairs": pair_count,
        "left": {"spearman": left_rho, "pairwiseAccuracy": left_pair,
                 "pairwiseTies": left_ties},
        "right": {"spearman": right_rho, "pairwiseAccuracy": right_pair,
                  "pairwiseTies": right_ties},
        "rightMinusLeft": {
            "spearman": right_rho - left_rho,
            "spearmanBootstrap95": [percentile(rho_deltas, 0.025),
                                    percentile(rho_deltas, 0.975)],
            "pairwiseAccuracy": right_pair - left_pair,
            "pairwiseBootstrap95": [percentile(pair_deltas, 0.025),
                                     percentile(pair_deltas, 0.975)]
        },
        "bootstrap": {"method": "paired photos with replacement (not independent pairs)",
                      "seed": seed, "requested": repetitions,
                      "validResamples": len(rho_deltas)},
        "notes": ("Descriptive percentile intervals only; correlated pairwise comparisons "
                  "and frozen calibration do not establish deployment safety. "
                  "Do not open held-out labels or tune thresholds from them.")
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", required=True)
    parser.add_argument("--scores", nargs="+", required=True)
    parser.add_argument("--left", required=True, help="Candidate ID, not a filename")
    parser.add_argument("--right", required=True, help="Candidate ID, not a filename")
    parser.add_argument("--repetitions", type=int, default=1000)
    parser.add_argument("--seed", type=int, default=20261017)
    args = parser.parse_args()
    result = confidence(load(args.input), [load(p) for p in args.scores],
                        args.left, args.right, args.repetitions, args.seed)
    print(json.dumps(result, indent=2, allow_nan=False))


if __name__ == "__main__":
    main()
