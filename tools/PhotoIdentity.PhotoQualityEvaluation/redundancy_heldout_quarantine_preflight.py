#!/usr/bin/env python3
"""WI-0194: aggregate-only sensitivity to excluding risk-exposed held-out photos.

Uses only existing private candidate similarities and the unchanged dHash<=4
photo-disjoint baseline. It never reads review labels, opens images, writes
files, freezes review sets, or changes application/product behavior.
Quarantine candidates are potential leakage indicators, NOT proven near-copies.
"""
from __future__ import annotations

import argparse
from collections import Counter
import json
from pathlib import Path

from redundancy_review_pairs import (
    BUCKETS, QUOTAS, _stratum, prepare_inventory,
)

PROTOCOL = "wi0194-heldout-risk-quarantine-preflight-v1"
SCENARIOS = (
    ("baseline-no-quarantine", 4, False),
    ("cross-dhash-5-to-8", 8, False),
    ("cross-dhash-5-to-12", 12, False),
    ("cross-dhash-5-to-12-or-clip-at-least-0_90", 12, True),
)


def _is_risk_edge(evidence, max_distance, include_high_clip):
    distance, cosine = evidence["dhashDistance"], evidence["clipCosine"]
    return ((distance is not None and 5 <= distance <= max_distance)
            or (include_high_clip and cosine is not None and cosine >= .90))


def quarantine_scenarios(data):
    inventory, pairs, membership, families = prepare_inventory(data)
    counts = Counter(membership.values())
    baseline_eligible = {partition: Counter()
                         for partition in ("calibration", "held-out")}
    for (left, right), evidence in pairs.items():
        if membership[left] == membership[right]:
            label = _stratum(evidence,
                             inventory[left]["sha"] == inventory[right]["sha"])
            baseline_eligible[membership[left]][label] += 1

    scenarios = []
    for name, distance, high_clip in SCENARIOS:
        quarantined = set()
        triggering_pairs = 0
        for (left, right), evidence in pairs.items():
            if membership[left] == membership[right]:
                continue
            if _is_risk_edge(evidence, distance, high_clip):
                triggering_pairs += 1
                quarantined.add(
                    left if membership[left] == "held-out" else right)

        if len(quarantined) > counts["held-out"]:
            raise ValueError("Invalid held-out quarantine count")

        eligible = Counter()
        removed = Counter()
        for (left, right), evidence in pairs.items():
            if membership[left] != "held-out" or membership[right] != "held-out":
                continue
            label = _stratum(evidence,
                             inventory[left]["sha"] == inventory[right]["sha"])
            if left in quarantined or right in quarantined:
                removed[label] += 1
            else:
                eligible[label] += 1

        if name == "baseline-no-quarantine" and eligible != baseline_eligible["held-out"]:
            raise ValueError("Baseline bucket eligibility changed")

        scenarios.append({
            "scenario": name,
            "crossPartitionTriggerCandidatePairs": triggering_pairs,
            "heldOutPhotosQuarantined": len(quarantined),
            "heldOutPhotosRemaining": counts["held-out"] - len(quarantined),
            "heldOutCandidatePairsExcluded": sum(removed.values()),
            "heldOutCandidatePairsRemaining": sum(eligible.values()),
            "eligibleHeldOutBuckets": {b: eligible[b] for b in BUCKETS},
            "heldOutBucketShortfalls": {
                b: QUOTAS[b] - eligible[b]
                for b in BUCKETS if eligible[b] < QUOTAS[b]
            },
            "nonExactCopyBucketQuotasMet": all(
                eligible[b] >= QUOTAS[b] for b in BUCKETS
                if b != "exact-copy-control"
            ),
        })

    return {
        "protocol": PROTOCOL,
        "status": "candidate-quarantine-sensitivity-only-not-frozen",
        "photos": len(inventory),
        "candidatePairs": len(pairs),
        "baselineLeakageFamilyCount": families,
        "calibrationPhotos": counts["calibration"],
        "heldOutPhotos": counts["held-out"],
        "baselineCalibrationEligible": {
            b: baseline_eligible["calibration"][b] for b in BUCKETS
        },
        "scenarios": scenarios,
        "genuineExactCopyControlsInCandidates": sum(
            1 for (a, b) in pairs if inventory[a]["sha"] == inventory[b]["sha"]
        ),
        "humanLabelsAccessed": False,
        "heldOutReviewsAccessed": False,
        "reviewManifestWritten": False,
        "reviewSetFrozen": False,
        "productionSuppressionApproved": False,
        "limitations": (
            "All scenarios use the SAME strict dHash<=4 photo-disjoint split. "
            "Observed dHash5-8, dHash5-12 and high CLIP cosine candidate edges "
            "are risk indicators, not proven near-copies or redundancy labels. "
            "Quarantine excludes held-out photos incident to risk edges; it "
            "does not change calibration membership or the underlying candidate "
            "manifest. Similarity-based exclusions may bias any future held-out "
            "sample; unobserved links and seeded-CLIP coverage gaps remain. "
            "Passing bucket quotas is NOT review-set freeze authorization."
        ),
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", required=True,
                        help="Existing private unreviewed candidate JSON")
    args = parser.parse_args()
    try:
        root = (Path.cwd() / "private").resolve()
        source = Path(args.input).resolve(strict=True)
        if not source.is_relative_to(root):
            raise ValueError("Candidate evidence must be in private/")
        with source.open("r", encoding="utf-8") as stream:
            report = quarantine_scenarios(json.load(stream))
    except (OSError, ValueError, TypeError, KeyError, OverflowError) as error:
        raise SystemExit(
            "WI-0194 quarantine preflight failed (" +
            type(error).__name__ + ")") from None
    print(json.dumps(report, indent=2, allow_nan=False))


if __name__ == "__main__":
    main()
