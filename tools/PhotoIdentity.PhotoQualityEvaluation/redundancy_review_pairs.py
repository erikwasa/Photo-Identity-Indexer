#!/usr/bin/env python3
"""WI-0194: offline, review-blind, photo-disjoint candidate-pair sampler.

Consumes a *private* candidate-pair inventory, not originals or IQA labels.
Does not retrieve nearest neighbours, load ML models, open photos, access
human redundancy labels, tune thresholds or mutate any application state.
All numeric cutoffs below are PRE-SAMPLING STRATA ONLY; never a redundancy
classification, production suppression threshold or held-out criterion.
"""
from __future__ import annotations

import argparse
from collections import Counter, defaultdict
import hashlib
import json
import math
from pathlib import Path

from learned_iqa import private_output

PROTOCOL = "wi0194-blind-redundancy-pairs-v1"
INPUT_PROTOCOL = "wi0194-candidate-pairs-v1"
SEED = "wi0194-pair-sampling-2026-10-18-v1"
HELDOUT_PERCENT = 25
NEAR_COPY_DHASH_MAX = 4   # Conservative leakage grouping, not truth.
BUCKETS = (
    "exact-copy-control", "near-copy", "high", "medium",
    "borderline", "disagreement", "random-negative",
)
QUOTAS = {
    "exact-copy-control": 12,
    "near-copy": 35,
    "high": 60,
    "medium": 60,
    "borderline": 60,
    "disagreement": 50,
    "random-negative": 35,
}
PHOTO_KEYS = frozenset({"revisionId", "contentSha256", "leakageGroup"})
PAIR_KEYS = frozenset({
    "leftRevisionId", "rightRevisionId", "dhashDistance",
    "clipCosine", "sourceKind",
})
MAX_PHOTOS = 200_000
MAX_PAIRS = 2_000_000


def digest(text):
    return hashlib.sha256(text.encode("utf-8")).hexdigest()


def _identifier(value, field):
    if (not isinstance(value, str) or not value or len(value) > 256
            or value.strip() != value or any(ord(c) < 32 or c in "/\\:" for c in value)):
        raise ValueError(f"Invalid {field}")
    return value.casefold()


def _shape(record, allowed, required, name):
    if (not isinstance(record, dict) or not required.issubset(record)
            or not set(record).issubset(allowed)):
        raise ValueError(f"Invalid {name} fields; no human labels or paths permitted")


def _num(record, key, lower, upper, integer=False):
    value = record.get(key)
    if value is None:
        return None
    if type(value) not in ((int,) if integer else (int, float)):
        raise ValueError(f"Invalid numeric {key}")
    if not math.isfinite(value) or not lower <= value <= upper:
        raise ValueError(f"Out-of-range {key}")
    return value


def _pair_key(a, b):
    return tuple(sorted((a, b)))


def _stratum(row, same_content):
    if same_content:
        return "exact-copy-control"
    if row["sourceKind"] == "random-negative":
        return "random-negative"
    d, c = row["dhashDistance"], row["clipCosine"]
    if d is not None and c is not None and (
            (d <= 10 and c <= 0.65) or (d >= 25 and c >= 0.90)):
        return "disagreement"
    if d is not None and d <= NEAR_COPY_DHASH_MAX:
        return "near-copy"
    if (d is not None and d <= 12) or (c is not None and c >= 0.90):
        return "high"
    if (d is not None and d <= 24) or (c is not None and c >= 0.78):
        return "medium"
    return "borderline"


def prepare_inventory(data):
    """Validate candidate evidence; never read or accept any review fields."""
    _shape(data, {"schemaVersion", "protocol", "sampleId", "photos", "pairs"},
           {"schemaVersion", "protocol", "sampleId", "photos", "pairs"}, "manifest")
    if type(data["schemaVersion"]) is not int or data["schemaVersion"] != 1:
        raise ValueError("Unsupported schemaVersion")
    if data["protocol"] != INPUT_PROTOCOL:
        raise ValueError("Incompatible candidate manifest protocol")
    _identifier(data["sampleId"], "sampleId")
    photos = data["photos"]
    pairs = data["pairs"]
    if (not isinstance(photos, list) or not 2 <= len(photos) <= MAX_PHOTOS
            or not isinstance(pairs, list) or not 1 <= len(pairs) <= MAX_PAIRS):
        raise ValueError("Candidate manifest population exceeds bounds or is empty")

    inventory = {}
    exact_content = {}
    user_groups = {}
    for raw in photos:
        _shape(raw, PHOTO_KEYS, {"revisionId", "contentSha256"}, "photo")
        original_id = raw["revisionId"]
        ident = _identifier(original_id, "revisionId")
        if ident in inventory:
            raise ValueError("Duplicate photo revisionId")
        sha = raw["contentSha256"]
        if (not isinstance(sha, str) or len(sha) != 64
                or any(c not in "0123456789abcdefABCDEF" for c in sha)):
            raise ValueError("Invalid photo contentSha256")
        group = raw.get("leakageGroup")
        if group is not None:
            _identifier(group, "leakageGroup")
            group = group.casefold()
        inventory[ident] = {"display": original_id, "sha": sha.lower(), "group": group}
        exact_content.setdefault(sha.lower(), ident)
        if group is not None:
            user_groups.setdefault(group, ident)

    parent = {ident: ident for ident in inventory}

    def find(ident):
        while parent[ident] != ident:
            parent[ident] = parent[parent[ident]]
            ident = parent[ident]
        return ident

    def union(a, b):
        left, right = find(a), find(b)
        if left != right:
            parent[max(left, right)] = min(left, right)

    for ident, photo in inventory.items():
        union(ident, exact_content[photo["sha"]])
        if photo["group"] is not None:
            union(ident, user_groups[photo["group"]])

    rows = {}
    for raw in pairs:
        _shape(raw, PAIR_KEYS,
               {"leftRevisionId", "rightRevisionId", "sourceKind"}, "pair")
        a = _identifier(raw["leftRevisionId"], "leftRevisionId")
        b = _identifier(raw["rightRevisionId"], "rightRevisionId")
        if a == b or a not in inventory or b not in inventory:
            raise ValueError("Pair is self-referential or has unknown revisionId")
        key = _pair_key(a, b)
        if key in rows:
            raise ValueError("Duplicate/reversed candidate pair")
        kind = raw["sourceKind"]
        if kind not in ("retrieved", "random-negative"):
            raise ValueError("Invalid candidate sourceKind")
        distance = _num(raw, "dhashDistance", 0, 64, integer=True)
        similarity = _num(raw, "clipCosine", -1.0, 1.0)
        if kind == "retrieved" and distance is None and similarity is None:
            raise ValueError("Retrieved pair needs at least one candidate score")
        rows[key] = {
            "sourceKind": kind, "dhashDistance": distance,
            "clipCosine": similarity,
        }
        # Automatically group known dHash near copies before partitioning.
        # The cutoff can overgroup false positives; this is a conservative
        # isolation rule, NOT a claim that near copies are redundant.
        if distance is not None and distance <= NEAR_COPY_DHASH_MAX:
            union(a, b)

    groups = defaultdict(list)
    for ident in inventory:
        groups[find(ident)].append(ident)
    memberships = {}
    for members in groups.values():
        family = "|".join(sorted(members))
        assigned = ("held-out" if int(digest(SEED + "|group|" + family)[:8], 16) % 100
                    < HELDOUT_PERCENT else "calibration")
        for ident in members:
            memberships[ident] = assigned

    return inventory, rows, memberships, len(groups)


def make_pair_sets(data):
    inventory, candidates, memberships, group_count = prepare_inventory(data)
    selected_pool = defaultdict(list)
    eligible = Counter()
    crossed = 0
    for (a, b), row in candidates.items():
        if memberships[a] != memberships[b]:
            crossed += 1
            continue
        bucket = _stratum(row, inventory[a]["sha"] == inventory[b]["sha"])
        partition = memberships[a]
        eligible[(partition, bucket)] += 1
        opaque_id = "p-" + digest(SEED + "|pair|" + a + "|" + b)[:24]
        selected_pool[(partition, bucket)].append((opaque_id, a, b, row))

    sampled = []
    for partition in ("calibration", "held-out"):
        for bucket in BUCKETS:
            pool = selected_pool[(partition, bucket)]
            ordered = sorted(
                pool, key=lambda item: (digest(SEED + "|pick|" + item[0]), item[0]))
            for identifier, a, b, scores in ordered[:QUOTAS[bucket]]:
                sampled.append((identifier, a, b, partition, bucket, scores))
    if not sampled:
        raise ValueError("No within-partition candidate pairs could be selected")

    sampled.sort(key=lambda x: (digest(SEED + "|blind-order|" + x[0]), x[0]))
    review = {
        "schemaVersion": 1,
        "protocol": PROTOCOL,
        "purpose": "blind-pair-review-only; not a quality benchmark or production policy",
        "labels": ["redundant", "similar-keep-both", "not-similar", "unsure"],
        "pairs": [{
            "pairId": pid,
            "leftRevisionId": inventory[a]["display"],
            "rightRevisionId": inventory[b]["display"],
        } for pid, a, b, _, _, _ in sampled],
    }
    audit = {
        "schemaVersion": 1,
        "protocol": PROTOCOL,
        "status": "research-review-set-draft-not-frozen",
        "sampleId": data["sampleId"],
        "sourceFingerprint": digest(json.dumps(
            data, sort_keys=True, separators=(",", ":"), ensure_ascii=True,
            allow_nan=False)),
        "split": "photo-disjoint; exact-content, supplied leakage families and dHash<=4 links co-partitioned",
        "partitionSeed": SEED,
        "calibrationPhotos": sum(x == "calibration" for x in memberships.values()),
        "heldOutPhotos": sum(x == "held-out" for x in memberships.values()),
        "leakageFamilyCount": group_count,
        "inputCandidatePairs": len(candidates),
        "excludedCrossPartitionPairs": crossed,
        "candidateBucketCounts": {
            partition: {bucket: eligible[(partition, bucket)] for bucket in BUCKETS}
            for partition in ("calibration", "held-out")
        },
        "selectedBucketCounts": {
            partition: {
                bucket: sum(p == partition and b == bucket
                            for _, _, _, p, b, _ in sampled)
                for bucket in BUCKETS
            } for partition in ("calibration", "held-out")
        },
        "quotaPerPartition": dict(QUOTAS),
        "selectedPairs": [{
            "pairId": pid, "partition": partition,
            "candidateBucket": bucket, **scores,
        } for pid, _, _, partition, bucket, scores in sampled],
        "heldOutReviewsAccessed": False,
        "modelOrThresholdApproved": False,
        "productionSuppressionApproved": False,
        "limitations": (
            "Sampling-only, not calibrated precision. Near-duplicate linkage "
            "requires adequate candidate-pair recall and supplied groups. "
            "Unlinked near duplicates may leak across partitions. Freeze and "
            "inspect the real archive cohort before any blind human reviews."
        ),
    }
    return review, audit


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", required=True, help="Private candidate-pair JSON")
    parser.add_argument("--blind-output", required=True,
                        help="New private blind review manifest")
    parser.add_argument("--audit-output", required=True,
                        help="New private partition/sampling audit")
    args = parser.parse_args()
    blind_dest = private_output(args.blind_output)
    audit_dest = private_output(args.audit_output)
    input_path = Path(args.input).resolve(strict=True)
    if len({blind_dest, audit_dest, input_path}) != 3:
        parser.error("Input and output paths must be distinct")
    # Candidate manifests are private inventory/evidence; never print rows.
    with input_path.open("r", encoding="utf-8") as stream:
        data = json.load(stream)
    review, audit = make_pair_sets(data)
    # Prevalidate that neither output exists; x mode also prevents overwrite.
    for dest in (blind_dest, audit_dest):
        dest.parent.mkdir(parents=True, exist_ok=True)
        if dest.exists():
            raise ValueError("Review outputs are one-time new files; refusing overwrite")
    with blind_dest.open("x", encoding="utf-8") as stream:
        json.dump(review, stream, indent=2, allow_nan=False)
        stream.write("\n")
    with audit_dest.open("x", encoding="utf-8") as stream:
        json.dump(audit, stream, indent=2, allow_nan=False)
        stream.write("\n")

    print(json.dumps({
        "protocol": PROTOCOL,
        "status": audit["status"],
        "calibrationPhotos": audit["calibrationPhotos"],
        "heldOutPhotos": audit["heldOutPhotos"],
        "leakageFamilyCount": audit["leakageFamilyCount"],
        "inputCandidatePairs": audit["inputCandidatePairs"],
        "crossPartitionPairsExcluded": audit["excludedCrossPartitionPairs"],
        "selectedPairCounts": {
            k: sum(v.values()) for k, v in audit["selectedBucketCounts"].items()
        },
        "heldOutReviewsAccessed": False,
        "productionSuppressionApproved": False,
    }, indent=2, allow_nan=False))


if __name__ == "__main__":
    main()
