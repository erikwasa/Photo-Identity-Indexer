#!/usr/bin/env python3
"""WI-0194: aggregate-only calibration pilot feasibility, not a review export.

Uses an existing private candidate manifest. The candidate score bands are
sampling heuristics, not human redundancy truth or suppression thresholds.
No images, model inference, label reads, review manifests or database writes.
"""
from __future__ import annotations

import argparse
from collections import Counter, defaultdict
import json
from pathlib import Path

from redundancy_review_pairs import digest, prepare_inventory

PROTOCOL = "wi0194-calibration-pilot-feasibility-v1"
SEED = "wi0194-calibration-risk-pilot-v1"
MAX_PAIR_OCCURRENCES_PER_PHOTO = 2
# Explicitly no exact-copy quota: there are no genuine archive controls.
QUOTAS = {
    "strict-dhash-0-to-4": 24,
    "dhash-close-clip-low-disagreement": 20,
    "dhash-far-clip-high-disagreement": 20,
    "weak-dhash-5-to-8": 24,
    "weak-dhash-9-to-12": 24,
    "high-clip-other": 20,
    "clip-mid-borderline": 24,
    "screened-random-control": 24,
    "other-candidate": 20,
}


def category(evidence, same_content):
    """Mutually exclusive strata in predeclared precedence order."""
    if same_content:
        return "genuine-exact-content-copy"
    if evidence["sourceKind"] == "random-negative":
        return "screened-random-control"
    d, c = evidence["dhashDistance"], evidence["clipCosine"]
    if d is not None and c is not None and d <= 10 and c <= .65:
        return "dhash-close-clip-low-disagreement"
    if d is not None and c is not None and d >= 25 and c >= .90:
        return "dhash-far-clip-high-disagreement"
    if d is not None and d <= 4:
        return "strict-dhash-0-to-4"
    if d is not None and 5 <= d <= 8:
        return "weak-dhash-5-to-8"
    if d is not None and 9 <= d <= 12:
        return "weak-dhash-9-to-12"
    if c is not None and c >= .90:
        return "high-clip-other"
    if d is not None and d >= 25 and c is not None and .62 <= c < .78:
        return "clip-mid-borderline"
    return "other-candidate"


def analyze(data):
    photos, pairs, membership, family_count = prepare_inventory(data)
    eligible = defaultdict(list)
    excluded_cross = 0
    held_out_only = 0
    for (a, b), evidence in pairs.items():
        if membership[a] != "calibration" or membership[b] != "calibration":
            if membership[a] != membership[b]:
                excluded_cross += 1
            else:
                held_out_only += 1
            continue
        bucket = category(evidence, photos[a]["sha"] == photos[b]["sha"])
        eligible[bucket].append((a, b))

    # A bounded deterministic dry run of diversity-constrained selection.
    # Only *counts* leave this function; no candidate IDs or photos exported.
    used = Counter()
    selected = Counter()
    for bucket, quota in QUOTAS.items():
        items = sorted(eligible[bucket],
                       key=lambda pair: digest(SEED + "|" + bucket + "|" +
                                               pair[0] + "|" + pair[1]))
        for a, b in items:
            if selected[bucket] >= quota:
                break
            if (used[a] >= MAX_PAIR_OCCURRENCES_PER_PHOTO or
                    used[b] >= MAX_PAIR_OCCURRENCES_PER_PHOTO):
                continue
            selected[bucket] += 1
            used[a] += 1
            used[b] += 1

    calib_count = sum(p == "calibration" for p in membership.values())
    result = {
        "protocol": PROTOCOL,
        "status": "calibration-pilot-feasibility-only-not-authorized",
        "photos": len(photos),
        "candidatePairs": len(pairs),
        "leakageFamilies": family_count,
        "calibrationPhotos": calib_count,
        "heldOutPhotosNotSampled": len(photos) - calib_count,
        "crossPartitionPairsExcluded": excluded_cross,
        "heldOutWithinPartitionPairsNotSampled": held_out_only,
        "genuineExactContentPairsAvailableInCalibration":
            len(eligible["genuine-exact-content-copy"]),
        "exactCopyControlsRequested": 0,
        "maxOccurrencesPerPhoto": MAX_PAIR_OCCURRENCES_PER_PHOTO,
        "bucketFeasibility": {
            bucket: {
                "eligiblePairs": len(eligible[bucket]),
                "proposedQuota": quota,
                "dryRunSelectedPairs": selected[bucket],
                "dryRunShortfall": max(0, quota - selected[bucket]),
            } for bucket, quota in QUOTAS.items()
        },
        "dryRunPairs": sum(selected.values()),
        "dryRunDistinctCalibrationPhotos": len(used),
        "heldOutPhotosInDryRun": 0,
        "humanLabelsAccessed": False,
        "heldOutReviewLabelsAccessed": False,
        "pilotReviewManifestCreated": False,
        "reviewSetFrozen": False,
        "automaticSuppressionApproved": False,
        "limitations": (
            "Uses only baseline dHash<=4 family partition and pre-existing "
            "candidate evidence. Proposed calibration pilot strata and quotas "
            "are heuristics, not certified redundancy/near-copy classes. "
            "No review set or labels exist. Selected candidate pairs can "
            "share photos up to the stated diversity cap. No exact-copy "
            "controls were manufactured, and cross-partition weak links "
            "remain unresolved. A calibration-only pilot cannot validate "
            "held-out leakage safety, representative archive recall, model "
            "precision, or production suppression."
        ),
    }
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", required=True,
                        help="Existing private enriched candidate manifest")
    args = parser.parse_args()
    try:
        root = (Path.cwd() / "private").resolve()
        path = Path(args.input).resolve(strict=True)
        if not path.is_relative_to(root):
            raise ValueError("Input must reside in private/")
        with path.open("r", encoding="utf-8") as stream:
            report = analyze(json.load(stream))
    except (OSError, ValueError, TypeError, KeyError, OverflowError) as error:
        raise SystemExit(
            "WI-0194 calibration feasibility failed (" +
            type(error).__name__ + ")") from None
    print(json.dumps(report, indent=2, allow_nan=False))


if __name__ == "__main__":
    main()
