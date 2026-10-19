#!/usr/bin/env python3
"""WI-0194 diagnostic only: read candidate edges; emit aggregate sensitivity."""
from __future__ import annotations
import argparse
from collections import Counter, defaultdict
import json
from pathlib import Path
from redundancy_review_pairs import (
    BUCKETS, HELDOUT_PERCENT, QUOTAS, SEED, digest, _stratum, prepare_inventory)

PROTOCOL = "wi0194-partition-sensitivity-v1"
DISTANCES = (4, 8, 12)


def repartition(photos, pairs, cutoff):
    parents = {key: key for key in photos}
    def find(key):
        while parents[key] != key:
            parents[key] = parents[parents[key]]
            key = parents[key]
        return key
    def union(a, b):
        a, b = find(a), find(b)
        if a != b:
            parents[max(a, b)] = min(a, b)
    content, user_groups = {}, {}
    for key, photo in photos.items():
        union(key, content.setdefault(photo["sha"], key))
        if photo["group"] is not None:
            union(key, user_groups.setdefault(photo["group"], key))
    for (a, b), edge in pairs.items():
        d = edge["dhashDistance"]
        if d is not None and d <= cutoff:
            union(a, b)
    groups = defaultdict(list)
    for key in photos:
        groups[find(key)].append(key)
    membership = {}
    for family in groups.values():
        token = "|".join(sorted(family))
        part = ("held-out" if int(digest(SEED + "|group|" + token)[:8], 16)
                % 100 < HELDOUT_PERCENT else "calibration")
        for key in family:
            membership[key] = part
    return membership, [len(group) for group in groups.values()]


def analyze(data):
    photos, pairs, original, _ = prepare_inventory(data)
    result = []
    for cutoff in DISTANCES:
        membership, sizes = repartition(photos, pairs, cutoff)
        if cutoff == 4 and membership != original:
            raise ValueError("Baseline group assignment differs from sampler")
        bins = {name: Counter() for name in ("calibration", "held-out")}
        boundary = Counter()
        flagged_heldout = set()
        for (a, b), edge in pairs.items():
            d, c = edge["dhashDistance"], edge["clipCosine"]
            if membership[a] == membership[b]:
                category = _stratum(edge, photos[a]["sha"] == photos[b]["sha"])
                bins[membership[a]][category] += 1
                continue
            boundary["all"] += 1
            if edge["sourceKind"] == "retrieved":
                boundary["retrieved"] += 1
            if d is not None and d <= cutoff:
                raise ValueError("Linked pair crossed partition")
            if d is not None and 5 <= d <= 8:
                boundary["dhash5To8"] += 1
            if d is not None and 9 <= d <= 12:
                boundary["dhash9To12"] += 1
            if c is not None and c >= 0.90:
                boundary["clipAtLeast0_90"] += 1
            if (d is not None and 5 <= d <= 12) or (c is not None and c >= 0.90):
                flagged_heldout.add(a if membership[a] == "held-out" else b)
        counts = Counter(membership.values())
        result.append({
            "linkageDhashDistanceAtMost": cutoff,
            "calibrationPhotos": counts["calibration"],
            "heldOutPhotos": counts["held-out"],
            "familyCount": len(sizes),
            "multiPhotoFamilies": sum(size > 1 for size in sizes),
            "largestFamilyPhotos": max(sizes),
            "familiesAtLeastTenPhotos": sum(size >= 10 for size in sizes),
            "crossPartitionPairs": boundary["all"],
            "crossPartitionRetrievedPairs": boundary["retrieved"],
            "crossPartitionDhash5To8": boundary["dhash5To8"],
            "crossPartitionDhash9To12": boundary["dhash9To12"],
            "crossPartitionClipAtLeast0_90": boundary["clipAtLeast0_90"],
            "heldOutPhotosIncidentToRiskCandidates": len(flagged_heldout),
            "eligible": {part: {bucket: bins[part][bucket] for bucket in BUCKETS}
                         for part in bins},
            "quotaShortfalls": {
                part: {bucket: QUOTAS[bucket] - bins[part][bucket]
                       for bucket in BUCKETS
                       if bins[part][bucket] < QUOTAS[bucket]}
                for part in bins},
        })
    return {
        "protocol": PROTOCOL,
        "status": "linkage-sensitivity-only-not-frozen",
        "photos": len(photos),
        "candidatePairs": len(pairs),
        "scenarios": result,
        "humanLabelsAccessed": False,
        "reviewManifestCreated": False,
        "thresholdApproved": False,
        "productionSuppressionApproved": False,
        "limitations": "These are only proposed leakage-family partitions, not redundancy cutoffs. High CLIP similarity may be semantic. Unobserved near-copy edges remain unknown. Scenario reassignment changes partition membership. Neither held-out reviews nor production behavior is touched."
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", required=True)
    args = parser.parse_args()
    try:
        root = (Path.cwd() / "private").resolve()
        path = Path(args.input).resolve(strict=True)
        if not path.is_relative_to(root):
            raise ValueError("Input must be private")
        with path.open("r", encoding="utf-8") as stream:
            report = analyze(json.load(stream))
    except (OSError, ValueError, TypeError, KeyError, OverflowError) as error:
        raise SystemExit("WI-0194 sensitivity failed (" + type(error).__name__ + ")") from None
    print(json.dumps(report, indent=2, allow_nan=False))


if __name__ == "__main__":
    main()
