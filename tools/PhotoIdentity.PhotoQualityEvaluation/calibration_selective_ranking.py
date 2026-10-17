#!/usr/bin/env python3
"""Research-only selective pairwise ranking diagnostic on calibration photos.

Predeclared score-gap quantiles are learned from TRAIN-fold score gaps
WITHOUT human ratings. Pairwise preference accuracy is measured on
disjoint validation-fold photos only. This is NOT absolute quality scoring,
a production ranker, exclusion permission, or held-out validation.

No human review field is accessed outside representative calibration photos.
"""
from __future__ import annotations

import argparse
from collections import defaultdict
import hashlib
import itertools
import json
import math

from candidate_audit import LEVELS, audit, load

PROTOCOL = "wi0193-calibration-selective-ranking-v1"
FOLDS = 5
GAP_QUANTILES = (0.0, 0.5, 0.75, 0.9)
INPUT_PROFILE = "profile-id=jpeg-1600-q78"


def fold_number(benchmark_id, photo):
    # Exact duplicate images remain in one fold. The original benchmark
    # grouped near-duplicates across partitions, but this auxiliary
    # calibration-only split does not guarantee near-duplicate isolation.
    source = photo.get("contentSha256")
    if not (isinstance(source, str) and len(source) == 64
            and all(c in "0123456789abcdef" for c in source.lower())):
        source = photo["revisionId"].casefold()
    payload = (benchmark_id + "\n" + PROTOCOL + "\n" + source.casefold()).encode("utf-8")
    return int.from_bytes(hashlib.sha256(payload).digest()[:8], "big") % FOLDS


def threshold_for(gaps, fraction):
    values = sorted(gaps)
    if not values:
        raise ValueError("Insufficient training pairs")
    if fraction == 0:
        return 0.0
    index = max(0, math.ceil(fraction * len(values)) - 1)
    return values[index]


def summarize_counts(stats):
    return {
        "totalPairs": stats["all"],
        "selectedPairs": stats["selected"],
        "pairCoverage": stats["selected"] / stats["all"] if stats["all"] else None,
        "differentTierPairs": stats["nonTie"],
        "selectedDifferentTierPairs": stats["nonTieSelected"],
        "differentTierCoverage": (
            stats["nonTieSelected"] / stats["nonTie"]
            if stats["nonTie"] else None),
        "differentTierOrderingAccuracy": (
            stats["nonTieCorrect"] / stats["nonTieSelected"]
            if stats["nonTieSelected"] else None),
        "twoTierOrMorePairs": stats["twoTier"],
        "selectedTwoTierOrMorePairs": stats["twoTierSelected"],
        "twoTierOrMoreCoverage": (
            stats["twoTierSelected"] / stats["twoTier"]
            if stats["twoTier"] else None),
        "twoTierOrMoreOrderingAccuracy": (
            stats["twoTierCorrect"] / stats["twoTierSelected"]
            if stats["twoTierSelected"] else None),
        "orderingTiesAmongSelectedDistinctTiers": stats["ties"],
    }


def evaluate_pairs(photos, scores, threshold):
    stats = dict.fromkeys(("all", "selected", "nonTie", "nonTieSelected",
                           "nonTieCorrect", "twoTier", "twoTierSelected",
                           "twoTierCorrect", "ties"), 0)
    for a, b in itertools.combinations(photos, 2):
        first, second = a["revisionId"].casefold(), b["revisionId"].casefold()
        quality_gap = abs(LEVELS[a["review"]["qualityLevel"]] -
                          LEVELS[b["review"]["qualityLevel"]])
        gap = abs(scores[first] - scores[second])
        selected = gap > 0 and gap >= threshold
        stats["all"] += 1
        if selected:
            stats["selected"] += 1
        if quality_gap:
            stats["nonTie"] += 1
            if selected:
                stats["nonTieSelected"] += 1
        if quality_gap >= 2:
            stats["twoTier"] += 1
            if selected:
                stats["twoTierSelected"] += 1
        if selected and quality_gap:
            direction = ((scores[first] - scores[second]) *
                         (LEVELS[a["review"]["qualityLevel"]] -
                          LEVELS[b["review"]["qualityLevel"]]))
            if direction > 0:
                stats["nonTieCorrect"] += 1
                if quality_gap >= 2:
                    stats["twoTierCorrect"] += 1
            elif direction == 0:
                stats["ties"] += 1
    return stats


def evaluate(benchmark, exports):
    if benchmark.get("schemaVersion") != 2:
        raise ValueError("Expected frozen benchmark schema v2")
    if INPUT_PROFILE not in benchmark.get("proxyProfile", "").splitlines():
        raise ValueError("Expected frozen review-proxy input profile")
    if len(exports) != 2:
        raise ValueError("Exactly two candidate exports required")
    # Validate complete benchmark coverage without touching human labels
    # outside calibration. The audit is calibration-only by design.
    audit(benchmark, exports)
    for e in exports:
        if e.get("inputKind") != "review-proxy":
            raise ValueError("Only prior frozen review-proxy scores are eligible")
    calibration = [p for p in benchmark["photos"]
                   if p.get("partition") == "calibration"
                   and p.get("sampleKind") == "representative"
                   and p.get("review", {}).get("qualityLevel") in LEVELS]
    if len(calibration) < 150:
        raise ValueError("At least 150 reviewed calibration photos required")

    scores_by_candidate = {}
    for export in exports:
        candidate = export["candidate"]["candidateId"]
        sign = 1 if export["higherIsBetter"] else -1
        scores_by_candidate[candidate] = {
            row["revisionId"].casefold(): float(row["rawScore"]) * sign
            for row in export["scores"] if row["rawScore"] is not None
        }
    if len(scores_by_candidate) != 2:
        raise ValueError("Distinct candidate IDs required")
    common = [p for p in calibration if all(
        p["revisionId"].casefold() in mapping
        for mapping in scores_by_candidate.values())]
    if len(common) != len(calibration):
        raise ValueError("Both candidates must have all reviewed calibration scores")

    folds = defaultdict(list)
    for photo in common:
        folds[fold_number(benchmark["benchmarkId"], photo)].append(photo)
    if set(folds) != set(range(FOLDS)) or min(map(len, folds.values())) < 15:
        raise ValueError("Unbalanced auxiliary folds; cannot evaluate safely")

    reports = []
    for candidate, scores in scores_by_candidate.items():
        bins = {}
        for fraction in GAP_QUANTILES:
            bins[str(fraction)] = {"pooled": dict.fromkeys(
                ("all", "selected", "nonTie", "nonTieSelected",
                 "nonTieCorrect", "twoTier", "twoTierSelected",
                 "twoTierCorrect", "ties"), 0), "folds": []}
        for fold_id in range(FOLDS):
            train = [p for i in range(FOLDS) if i != fold_id for p in folds[i]]
            validation = folds[fold_id]
            # Thresholds based solely on raw score gaps of TRAIN photos.
            # No labels contribute to threshold selection.
            training_gaps = [
                abs(scores[a["revisionId"].casefold()] -
                    scores[b["revisionId"].casefold()])
                for a, b in itertools.combinations(train, 2)]
            for fraction in GAP_QUANTILES:
                cutoff = threshold_for(training_gaps, fraction)
                counts = evaluate_pairs(validation, scores, cutoff)
                entry = bins[str(fraction)]
                for key, value in counts.items():
                    entry["pooled"][key] += value
                entry["folds"].append({
                    "fold": fold_id,
                    "validationPhotos": len(validation),
                    "trainingScoreGapThreshold": cutoff,
                    "twoTierOrMorePairs": counts["twoTier"],
                    "selectedTwoTierOrMorePairs": counts["twoTierSelected"],
                })
        reports.append({
            "candidate": candidate,
            "jointCalibrationPhotos": len(common),
            "foldSizes": [len(folds[f]) for f in range(FOLDS)],
            "quantiles": {
                name: {"scoreGapTrainingQuantile": float(name),
                       **summarize_counts(entry["pooled"]),
                       "foldDiagnostics": entry["folds"]}
                for name, entry in bins.items()
            }
        })
    return {
        "protocol": PROTOCOL,
        "scope": "5-fold within-calibration photo-disjoint diagnostic only",
        "thresholdRule": (
            "predeclared score-gap quantiles from training-fold raw scores "
            "only, then ordering evaluated on same-fold validation pairs"),
        "quantiles": list(GAP_QUANTILES),
        "candidateReports": reports,
        "sampleIndependenceWarning": (
            "Pairs re-use photos; pooled counts are not independent samples. "
            "Exact source-content duplicates share folds, but visually "
            "near-duplicate photos may cross folds. Fold-specific thresholds "
            "are diagnostics, not one frozen production threshold."),
        "calibrationOnly": True,
        "heldOutLabelsAccessed": False,
        "heldOutEvaluationAllowed": False,
        "productionModelApproved": False,
        "automaticVeryLowExclusionApproved": False,
        "rankingNotClassification": True,
        "warning": (
            "This evaluates score-gap selective pair ordering, not absolute "
            "quality tiers or production slideshow selection. Model licensing, "
            "benefit, runtime, preprocessing and external held-out validity "
            "are unresolved. No automatic exclusion."),
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", required=True, help="Private frozen benchmark")
    parser.add_argument("--scores", nargs=2, required=True,
                        help="Two complete private proxy score exports")
    parser.add_argument("--fold-details", action="store_true",
                        help="Also print fold-size/threshold diagnostics")
    args = parser.parse_args()
    result = evaluate(load(args.input), [load(path) for path in args.scores])
    if not args.fold_details:
        for candidate in result["candidateReports"]:
            for row in candidate["quantiles"].values():
                row.pop("foldDiagnostics", None)
    print(json.dumps(result, indent=2, allow_nan=False))
    print("Calibration aggregates only; keep photo identities, raw scores and "
          "held-out labels private.")


if __name__ == "__main__":
    main()
