#!/usr/bin/env python3
"""WI-0194: private candidate-cohort preflight, aggregate counts only.

Reads existing offline candidate evidence, not source files, photos, models,
human labels, reviewer records, or the WI-0193 quality-study held-out set.
Does not write any file, freeze a cohort, select thresholds, or approve use.
"""
from __future__ import annotations

import argparse
from collections import Counter
import json
from pathlib import Path

from redundancy_review_pairs import (
    BUCKETS, NEAR_COPY_DHASH_MAX, QUOTAS, _stratum, prepare_inventory,
)


PROTOCOL = "wi0194-redundancy-candidate-preflight-v1"


def preflight(data):
    """Validate complete manifest and count observable (not labelled) risks."""
    inventory, pairs, membership, families, sizes = prepare_inventory(
        data, include_family_sizes=True)
    photo_counts = Counter(membership.values())
    eligible = {p: Counter() for p in ("calibration", "held-out")}
    retrieved = 0
    negatives = 0
    exact_copy_pairs = 0
    strict_near_links = 0
    cross = 0
    cross_retrieved = 0
    cross_dhash_5_to_8 = 0
    cross_dhash_9_to_12 = 0
    cross_high_clip = 0
    cross_exact_content = 0
    cross_strict_near_links = 0

    for (a, b), evidence in pairs.items():
        source = evidence["sourceKind"]
        if source == "retrieved":
            retrieved += 1
        else:
            negatives += 1
        same_content = inventory[a]["sha"] == inventory[b]["sha"]
        if same_content:
            exact_copy_pairs += 1
        d = evidence["dhashDistance"]
        clip = evidence["clipCosine"]
        if d is not None and d <= NEAR_COPY_DHASH_MAX:
            strict_near_links += 1

        if membership[a] != membership[b]:
            cross += 1
            if source == "retrieved":
                cross_retrieved += 1
            if same_content:
                cross_exact_content += 1
            if d is not None and d <= NEAR_COPY_DHASH_MAX:
                cross_strict_near_links += 1
            # Unlinked edges are possible *review-set leakage warnings*.
            # Neither dHash nor CLIP cutoffs are truth/suppression rules.
            if d is not None and 5 <= d <= 8:
                cross_dhash_5_to_8 += 1
            if d is not None and 9 <= d <= 12:
                cross_dhash_9_to_12 += 1
            if clip is not None and clip >= 0.90:
                cross_high_clip += 1
            continue

        bucket = _stratum(evidence, same_content)
        eligible[membership[a]][bucket] += 1

    if cross_exact_content or cross_strict_near_links:
        raise ValueError("Known exact or linked near-copy family crossed partitions")

    sha_counts = Counter(photo["sha"] for photo in inventory.values())
    same_content_families = sum(n > 1 for n in sha_counts.values())
    same_content_photos = sum(n for n in sha_counts.values() if n > 1)
    family_distribution = {
        "totalFamilies": families,
        "multiPhotoFamilies": sum(n >= 2 for n in sizes),
        "photosInMultiPhotoFamilies": sum(n for n in sizes if n >= 2),
        "familiesAtLeastTenPhotos": sum(n >= 10 for n in sizes),
        "largestFamilyPhotos": max(sizes),
        "exactContentDuplicateFamilies": same_content_families,
        "photosSharingExactContent": same_content_photos,
    }
    bucket_counts = {
        p: {bucket: eligible[p][bucket] for bucket in BUCKETS}
        for p in ("calibration", "held-out")
    }
    quota_shortfalls = {
        p: {bucket: QUOTAS[bucket] - eligible[p][bucket]
            for bucket in BUCKETS if eligible[p][bucket] < QUOTAS[bucket]}
        for p in ("calibration", "held-out")
    }
    return {
        "protocol": PROTOCOL,
        "status": "candidate-cohort-preflight-only-not-frozen",
        "photoCount": len(inventory),
        "candidatePairs": len(pairs),
        "retrievedPairs": retrieved,
        "randomNegativeControls": negatives,
        "exactContentCandidatePairs": exact_copy_pairs,
        "strictDhashNearCopyCandidateLinks": strict_near_links,
        "photoDisjointSplit": {
            "calibrationPhotos": photo_counts["calibration"],
            "heldOutPhotos": photo_counts["held-out"],
        },
        "leakageFamilies": family_distribution,
        "candidatePartitionCounts": {
            "withinPartitionPairs": len(pairs) - cross,
            "crossPartitionPairsExcludedFromSampling": cross,
            "crossPartitionRetrievedPairs": cross_retrieved,
            "crossPartitionKnownExactContentPairs": cross_exact_content,
            "crossPartitionStrictDhashNearCopyPairs": cross_strict_near_links,
            "crossPartitionDhashDistance5To8": cross_dhash_5_to_8,
            "crossPartitionDhashDistance9To12": cross_dhash_9_to_12,
            "crossPartitionClipCosineAtLeast0_90": cross_high_clip,
        },
        "eligibleBucketCounts": bucket_counts,
        "quotaShortfallsPerPartition": quota_shortfalls,
        "humanRedundancyLabelsAccessed": False,
        "heldOutReviewLabelsAccessed": False,
        "reviewSetFrozen": False,
        "thresholdsCalibrated": False,
        "productionSuppressionApproved": False,
        "limitations": (
            "Candidate similarities are not human-reviewed redundancy. "
            "Linked dHash<=4 and exact-copy families co-partition by construction; "
            "dHash5-12 and high CLIP cross-partition counts are possible unlinked "
            "near-copy risks, not confirmed leakage. Retrieval only contains "
            "existing candidate links; unobserved near copies cannot be ruled out. "
            "CLIP-seed coverage is not full-corpus CLIP recall. "
            "No human review, frozen held-out cohort, or production approval."
        ),
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", required=True, help="Existing private candidate JSON")
    args = parser.parse_args()
    try:
        private_root = (Path.cwd() / "private").resolve()
        candidate_path = Path(args.input).resolve(strict=True)
        if not candidate_path.is_relative_to(private_root):
            raise ValueError("Candidate evidence must be read from private/")
        with candidate_path.open("r", encoding="utf-8") as stream:
            data = json.load(stream)
        report = preflight(data)
    except (OSError, ValueError, TypeError, KeyError, OverflowError) as error:
        # No accidental private filenames, values or IDs from exceptions.
        raise SystemExit("WI-0194 preflight failed (" + type(error).__name__ + ")") from None
    print(json.dumps(report, indent=2, allow_nan=False))


if __name__ == "__main__":
    main()
