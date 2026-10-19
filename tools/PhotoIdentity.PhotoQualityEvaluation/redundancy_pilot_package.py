#!/usr/bin/env python3
"""WI-0194 integrated LOCAL calibration-pilot preparation and diagnostics.

Never loads image bytes, reads held-out labels, or updates archive/catalogue.
All source, pair identifiers, human labels and model evidence stay private.
Only calibration-calibration pairs are eligible; cutoff bands are strata only.
"""
from __future__ import annotations

import argparse
from collections import Counter, defaultdict
import hashlib
import json
import math
from pathlib import Path

from redundancy_calibration_pilot_preflight import (
    MAX_PAIR_OCCURRENCES_PER_PHOTO, QUOTAS, SEED, category,
)
from redundancy_review_pairs import digest, prepare_inventory

BLIND_PROTOCOL = "wi0194-calibration-blind-pilot-v1"
AUDIT_PROTOCOL = "wi0194-calibration-pilot-audit-v1"
LABELS_PROTOCOL = "wi0194-calibration-pilot-labels-v1"
LABELS = ("redundant", "similar-keep-both", "not-similar", "unsure")


def private_file(raw, *, must_exist):
    root = (Path.cwd() / "private").resolve()
    path = Path(raw).resolve(strict=must_exist)
    if not path.is_relative_to(root) or path == root:
        raise ValueError("Pilot files must be below private/")
    if must_exist and not path.is_file():
        raise ValueError("Pilot source must be a regular file")
    return path


def load_json(path):
    with path.open("r", encoding="utf-8") as stream:
        return json.load(stream)


def canonical_bytes(value):
    return (json.dumps(value, indent=2, ensure_ascii=True, allow_nan=False)
            + "\n").encode("utf-8")


def fingerprint(data):
    return hashlib.sha256(data).hexdigest()


def make_package(data):
    inventory, candidate_pairs, membership, families = prepare_inventory(data)
    eligible = defaultdict(list)
    for (a, b), row in candidate_pairs.items():
        if membership[a] != "calibration" or membership[b] != "calibration":
            continue
        bucket = category(row, inventory[a]["sha"] == inventory[b]["sha"])
        if bucket in QUOTAS:
            eligible[bucket].append((a, b, row))

    usage = Counter()
    selected = []
    for bucket, quota in QUOTAS.items():
        ordered = sorted(
            eligible[bucket],
            key=lambda item: (digest(SEED + "|" + bucket + "|" +
                                     item[0] + "|" + item[1]), item[0], item[1]))
        for a, b, row in ordered:
            if sum(entry["bucket"] == bucket for entry in selected) >= quota:
                break
            if (usage[a] >= MAX_PAIR_OCCURRENCES_PER_PHOTO or
                    usage[b] >= MAX_PAIR_OCCURRENCES_PER_PHOTO):
                continue
            pair_id = "cp-" + digest(SEED + "|pair|" + a + "|" + b)[:24]
            selected.append({
                "id": pair_id, "a": a, "b": b, "bucket": bucket,
                "dhashDistance": row["dhashDistance"],
                "clipCosine": row["clipCosine"],
                "sourceKind": row["sourceKind"],
            })
            usage[a] += 1
            usage[b] += 1

    quota_counts = Counter(x["bucket"] for x in selected)
    shortfalls = {
        bucket: quota - quota_counts[bucket]
        for bucket, quota in QUOTAS.items() if quota_counts[bucket] < quota
    }
    if shortfalls:
        raise ValueError("Calibration pilot failed its declared quotas")

    selected.sort(key=lambda item: (digest(
        SEED + "|blind|" + item["id"]), item["id"]))
    blind = {
        "schemaVersion": 1, "protocol": BLIND_PROTOCOL,
        "purpose": "calibration-only-pilot-unfrozen-heldout-excluded",
        "labels": list(LABELS),
        "pairs": [{
            "pairId": item["id"],
            "leftRevisionId": inventory[item["a"]]["display"],
            "rightRevisionId": inventory[item["b"]]["display"],
        } for item in selected],
    }
    digest_blind = fingerprint(canonical_bytes(blind))
    audit = {
        "schemaVersion": 1, "protocol": AUDIT_PROTOCOL,
        "status": "calibration-pilot-draft-no-labels",
        "pilotSha256": digest_blind,
        "candidateManifestFingerprint": digest(json.dumps(
            data, sort_keys=True, separators=(",", ":"), ensure_ascii=True,
            allow_nan=False)),
        "calibrationPhotos": sum(x == "calibration" for x in membership.values()),
        "heldOutPhotosExcluded": sum(x == "held-out" for x in membership.values()),
        "leakageFamilyCount": families,
        "maxOccurrencesPerPhoto": MAX_PAIR_OCCURRENCES_PER_PHOTO,
        "genuineExactCopyPairsAvailable": sum(
            inventory[a]["sha"] == inventory[b]["sha"]
            for (a, b) in candidate_pairs),
        "exactCopyControlQuota": 0,
        "proposedQuotas": dict(QUOTAS),
        "selectedCounts": {b: quota_counts[b] for b in QUOTAS},
        "pairEvidence": [{
            "pairId": item["id"],
            "candidateBucket": item["bucket"],
            "dhashDistance": item["dhashDistance"],
            "clipCosine": item["clipCosine"],
            "sourceKind": item["sourceKind"],
        } for item in selected],
        "heldOutPairEvidenceIncluded": False,
        "heldOutHumanLabelsAccessed": False,
        "modelThresholdApproved": False,
        "productionSuppressionApproved": False,
    }
    return blind, audit


def create_files(candidate, blind_dest, audit_dest):
    if len({candidate, blind_dest, audit_dest}) != 3:
        raise ValueError("Pilot source and output paths must be distinct")
    if blind_dest.exists() or audit_dest.exists():
        raise ValueError("Pilot outputs already exist")
    blind, audit = make_package(load_json(candidate))
    payload = ((blind_dest, canonical_bytes(blind)),
               (audit_dest, canonical_bytes(audit)))
    # Never truncate an existing artifact. Prevalidate both before writing.
    written = []
    try:
        for path, data in payload:
            path.parent.mkdir(parents=True, exist_ok=True)
            with path.open("xb") as stream:
                stream.write(data)
            written.append(path)
    except OSError:
        for path in written:
            path.unlink(missing_ok=True)
        raise
    return {
        "protocol": BLIND_PROTOCOL,
        "status": "calibration-blind-pilot-prepared-not-reviewed",
        "calibrationPilotPairs": len(blind["pairs"]),
        "selectedByBucket": audit["selectedCounts"],
        "distinctPilotPhotos": len({
            row[side] for row in blind["pairs"]
            for side in ("leftRevisionId", "rightRevisionId")
        }),
        "heldOutPhotosIncluded": 0,
        "heldOutReviewLabelsAccessed": False,
        "reviewSetFrozen": False,
        "productionSuppressionApproved": False,
    }


def validate_blind(blind):
    if (not isinstance(blind, dict) or set(blind) !=
            {"schemaVersion", "protocol", "purpose", "labels", "pairs"}
            or type(blind["schemaVersion"]) is not int
            or blind["schemaVersion"] != 1
            or blind["protocol"] != BLIND_PROTOCOL
            or blind["purpose"] !=
                "calibration-only-pilot-unfrozen-heldout-excluded"
            or blind["labels"] != list(LABELS)
            or not isinstance(blind["pairs"], list)
            or not 1 <= len(blind["pairs"]) <= sum(QUOTAS.values())):
        raise ValueError("Invalid calibration-only blind pilot")
    ids = set()
    revisions = set()
    for row in blind["pairs"]:
        if (not isinstance(row, dict) or set(row) !=
                {"pairId", "leftRevisionId", "rightRevisionId"}):
            raise ValueError("Invalid blind pair")
        pid, left, right = (row["pairId"], row["leftRevisionId"],
                            row["rightRevisionId"])
        if (not isinstance(pid, str) or not pid.startswith("cp-")
                or len(pid) != 27 or
                any(x not in "0123456789abcdef" for x in pid[3:])
                or pid in ids or not isinstance(left, str)
                or not isinstance(right, str) or left == right
                or not left or not right):
            raise ValueError("Invalid or repeated pilot pair")
        ids.add(pid)
        revisions.update((left.casefold(), right.casefold()))
    return ids, revisions


def validate_labels(labels, pilot_sha, pair_ids):
    if (not isinstance(labels, dict)
            or set(labels) != {"schemaVersion", "protocol", "pilotSha256",
                               "reviews"}
            or labels["schemaVersion"] != 1
            or labels["protocol"] != LABELS_PROTOCOL
            or labels["pilotSha256"] != pilot_sha
            or not isinstance(labels["reviews"], list)):
        raise ValueError("Unbound or invalid calibration reviews")
    received = {}
    for entry in labels["reviews"]:
        if (not isinstance(entry, dict) or
                set(entry) != {"pairId", "label", "notes"} or
                entry["pairId"] not in pair_ids or
                entry["pairId"] in received or
                entry["label"] not in LABELS or
                not isinstance(entry["notes"], str) or
                len(entry["notes"]) > 1000):
            raise ValueError("Invalid pilot review entry")
        received[entry["pairId"]] = entry["label"]
    return received


def wilson_interval(success, n):
    if not n:
        return None
    z = 1.959963984540054
    p = success / n
    d = 1 + z*z/n
    center = (p + z*z/(2*n))/d
    half = z*math.sqrt(p*(1-p)/n + z*z/(4*n*n))/d
    return [round(max(0.0, center-half), 4),
            round(min(1.0, center+half), 4)]


def report(blind_path, audit_path, labels_path):
    blind = load_json(blind_path)
    pair_ids, _ = validate_blind(blind)
    sha = fingerprint(blind_path.read_bytes())
    audit = load_json(audit_path)
    if (not isinstance(audit, dict) or
            audit.get("protocol") != AUDIT_PROTOCOL or
            audit.get("pilotSha256") != sha or
            audit.get("heldOutPairEvidenceIncluded") is not False or
            not isinstance(audit.get("pairEvidence"), list) or
            {x["pairId"] for x in audit["pairEvidence"]} != pair_ids or
            len(audit["pairEvidence"]) != len(pair_ids)):
        raise ValueError("Audit must be bound to this calibration-only pilot")
    labels = load_json(labels_path)
    reviewed = validate_labels(labels, sha, pair_ids)
    grouped = defaultdict(Counter)
    for row in audit["pairEvidence"]:
        name = row["candidateBucket"]
        if name not in QUOTAS:
            raise ValueError("Unknown pilot category")
        if row["pairId"] in reviewed:
            grouped[name][reviewed[row["pairId"]]] += 1
    by_bucket = {}
    for bucket in QUOTAS:
        counts = grouped[bucket]
        decisive = counts["redundant"] + counts["similar-keep-both"] + counts["not-similar"]
        by_bucket[bucket] = {
            "reviewed": sum(counts.values()),
            "redundant": counts["redundant"],
            "similarKeepBoth": counts["similar-keep-both"],
            "notSimilar": counts["not-similar"],
            "unsure": counts["unsure"],
            "redundantShareAmongDecisive": (
                round(counts["redundant"]/decisive, 4) if decisive else None),
            "descriptiveWilson95ForRedundantShare": wilson_interval(
                counts["redundant"], decisive),
        }
    return {
        "protocol": "wi0194-calibration-pilot-report-v1",
        "status": "calibration-label-descriptive-only-not-heldout",
        "pilotPairs": len(pair_ids),
        "reviewedPairs": len(reviewed),
        "remainingPairs": len(pair_ids) - len(reviewed),
        "bucketCounts": by_bucket,
        "humanReviewLabelsUsed": bool(reviewed),
        "heldOutReviewsAccessed": False,
        "thresholdFrozen": False,
        "productionSuppressionApproved": False,
        "limitations": (
            "Stratified, nonrepresentative calibration-pilot review. Wilson "
            "intervals are descriptive binomial approximations, not independent "
            "held-out precision, recall or calibrated automatic suppression. "
            "Reviewed pairs share photos and are intentionally over-sampled "
            "for ambiguity. No model or suppression policy was selected."
        )
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="mode", required=True)
    make = sub.add_parser("prepare")
    make.add_argument("--input", required=True)
    make.add_argument("--blind-output", required=True)
    make.add_argument("--audit-output", required=True)
    stats = sub.add_parser("report")
    stats.add_argument("--blind", required=True)
    stats.add_argument("--audit", required=True)
    stats.add_argument("--labels", required=True)
    args = parser.parse_args()
    try:
        if args.mode == "prepare":
            summary = create_files(private_file(args.input, must_exist=True),
                                   private_file(args.blind_output, must_exist=False),
                                   private_file(args.audit_output, must_exist=False))
        else:
            summary = report(private_file(args.blind, must_exist=True),
                             private_file(args.audit, must_exist=True),
                             private_file(args.labels, must_exist=True))
    except (OSError, ValueError, TypeError, KeyError, OverflowError) as err:
        raise SystemExit("WI-0194 pilot operation failed (" +
                         type(err).__name__ + ")") from None
    print(json.dumps(summary, indent=2, allow_nan=False))


if __name__ == "__main__":
    main()
