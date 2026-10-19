#!/usr/bin/env python3
"""WI-0194: pre-registered independent held-out pair pilot, offline/private.

This is an exploratory HELD-OUT descriptive validation of frozen source
heuristic categories, not a precision-certified production policy.
Preparation does not access ANY labels; it only uses the calibration blind
photo endpoints to exclude observed candidate-level cross-split risks.
"""
from __future__ import annotations
from collections import Counter, defaultdict
import argparse
import json
from pathlib import Path

from redundancy_pilot_package import (
    AUDIT_PROTOCOL as CALIB_AUDIT_PROTOCOL, BLIND_PROTOCOL as CALIB_BLIND_PROTOCOL,
    LABELS, canonical_bytes, fingerprint, load_json, private_file,
    validate_blind, wilson_interval,
)
from redundancy_calibration_pilot_preflight import (
    MAX_PAIR_OCCURRENCES_PER_PHOTO, QUOTAS, category,
)
from redundancy_review_pairs import digest, prepare_inventory

BLIND_PROTOCOL = "wi0194-heldout-blind-validation-v1"
AUDIT_PROTOCOL = "wi0194-heldout-validation-audit-v1"
LABELS_PROTOCOL = "wi0194-heldout-validation-labels-v1"
SEED = "wi0194-heldout-independent-validation-fixed-v1"
PURPOSE = "heldout-independent-pair-validation-frozen-before-labels"
MIN_PAIRS = 100
MAX_QUARANTINED_FRACTION = .20
RISK_DHASH_LOWER = 5
RISK_DHASH_UPPER = 12
RISK_CLIP_MIN = .90


def validate_holdout_blind(blind):
    if (not isinstance(blind, dict) or set(blind) !=
            {"schemaVersion", "protocol", "purpose", "labels", "pairs"}
            or type(blind["schemaVersion"]) is not int
            or blind["schemaVersion"] != 1
            or blind["protocol"] != BLIND_PROTOCOL
            or blind["purpose"] != PURPOSE
            or blind["labels"] != list(LABELS)
            or not isinstance(blind["pairs"], list)
            or not 1 <= len(blind["pairs"]) <= sum(QUOTAS.values())):
        raise ValueError("Invalid held-out blind validation")
    ids = set()
    revisions = set()
    for row in blind["pairs"]:
        if not isinstance(row, dict) or set(row) != {
                "pairId", "leftRevisionId", "rightRevisionId"}:
            raise ValueError("Malformed validation pair")
        pid = row["pairId"]
        a, b = row["leftRevisionId"], row["rightRevisionId"]
        if (not isinstance(pid, str) or not pid.startswith("hp-")
                or len(pid) != 27 or any(c not in "0123456789abcdef" for c in pid[3:])
                or pid in ids or not isinstance(a, str) or not isinstance(b, str)
                or not a or not b or a.casefold() == b.casefold()):
            raise ValueError("Repeated/malformed held-out pair")
        ids.add(pid)
        revisions.update((a.casefold(), b.casefold()))
    return ids, revisions


def _risk(edge):
    d, c = edge["dhashDistance"], edge["clipCosine"]
    return ((d is not None and RISK_DHASH_LOWER <= d <= RISK_DHASH_UPPER)
            or (c is not None and c >= RISK_CLIP_MIN))


def prepare(data, calibration_blind):
    inventory, pairs, membership, family_count = prepare_inventory(data)
    calib_ids, calib_revisions = validate_blind(calibration_blind)
    if any(revision not in inventory or membership[revision] != "calibration"
           for revision in calib_revisions):
        raise ValueError("Calibration pilot no longer matches this source split")
    if len(calib_revisions) < 2:
        raise ValueError("Not enough distinct calibration pilot photos")

    # The quarantine scope is ONLY reviewed calibration-pilot endpoints.
    # Unlike earlier blanket quarantine, no unreviewed calibration photo
    # contributes risk edges. Do not inspect any labels while preparing.
    exposed = set()
    risky_edges = 0
    for (a, b), evidence in pairs.items():
        if membership[a] == membership[b]:
            continue
        calib, heldout = (a, b) if membership[a] == "calibration" else (b, a)
        if calib in calib_revisions and _risk(evidence):
            exposed.add(heldout)
            risky_edges += 1
    heldout_total = sum(part == "held-out" for part in membership.values())
    if len(exposed) / heldout_total > MAX_QUARANTINED_FRACTION:
        raise ValueError("Observed contamination exclusion exceeds held-out budget")

    eligible = defaultdict(list)
    excluded_pairs = 0
    for (a, b), row in pairs.items():
        if membership[a] != "held-out" or membership[b] != "held-out":
            continue
        if a in exposed or b in exposed:
            excluded_pairs += 1
            continue
        bucket = category(row, inventory[a]["sha"] == inventory[b]["sha"])
        if bucket in QUOTAS:
            eligible[bucket].append((a, b, row))

    usage = Counter()
    chosen = []
    for bucket, quota in QUOTAS.items():
        ordered = sorted(
            eligible[bucket],
            key=lambda item: (digest(SEED + "|" + bucket + "|" +
                                     item[0] + "|" + item[1]), item[0], item[1]))
        selected_for_bucket = 0
        for a, b, evidence in ordered:
            if selected_for_bucket == quota:
                break
            if (usage[a] >= MAX_PAIR_OCCURRENCES_PER_PHOTO or
                    usage[b] >= MAX_PAIR_OCCURRENCES_PER_PHOTO):
                continue
            chosen.append((a, b, evidence, bucket))
            usage[a] += 1
            usage[b] += 1
            selected_for_bucket += 1
    # Do not allow a 10-case convenience test to masquerade as archive evidence.
    if len(chosen) < MIN_PAIRS:
        raise ValueError("Independent held-out candidate supply below minimum")
    chosen.sort(key=lambda row: (
        digest(SEED + "|blind|" + row[0] + "|" + row[1]), row[0], row[1]))

    blind = {
        "schemaVersion": 1, "protocol": BLIND_PROTOCOL,
        "purpose": PURPOSE, "labels": list(LABELS),
        "pairs": [{
            "pairId": "hp-" + digest(SEED + "|pair|" + a + "|" + b)[:24],
            "leftRevisionId": inventory[a]["display"],
            "rightRevisionId": inventory[b]["display"],
        } for a, b, _, _ in chosen],
    }
    blind_sha = fingerprint(canonical_bytes(blind))
    selected = Counter(bucket for _, _, _, bucket in chosen)
    audit = {
        "schemaVersion": 1,
        "protocol": AUDIT_PROTOCOL,
        "status": "heldout-validation-frozen-before-human-labels",
        "purpose": PURPOSE,
        "pilotSha256": blind_sha,
        "calibrationBlindSha256": fingerprint(canonical_bytes(calibration_blind)),
        "candidateManifestFingerprint": digest(json.dumps(
            data, sort_keys=True, separators=(",", ":"),
            ensure_ascii=True, allow_nan=False)),
        "predeclaredRule": {
            "baselineDhashFamilyLinkDistanceAtMost": 4,
            "riskDhashBand": [RISK_DHASH_LOWER, RISK_DHASH_UPPER],
            "riskClipCosineAtLeast": RISK_CLIP_MIN,
            "selectionSeed": SEED,
            "maxPairOccurrencesPerPhoto": MAX_PAIR_OCCURRENCES_PER_PHOTO,
        },
        "calibrationPilotPhotosProtected": len(calib_revisions),
        "calibrationPilotPairsExcluded": len(calib_ids),
        "heldOutPhotosBeforeQuarantine": heldout_total,
        "heldOutPhotosQuarantinedByObservedLinks": len(exposed),
        "observedCrossSplitRiskEdgesToCalibrationPilot": risky_edges,
        "withinHeldOutCandidatePairsExcludedByQuarantine": excluded_pairs,
        "heldOutPhotosAfterQuarantine": heldout_total - len(exposed),
        "heldOutCandidateSelectedPairs": len(chosen),
        "selectedBucketCounts": {bucket: selected[bucket] for bucket in QUOTAS},
        "eligibleBucketCounts": {bucket: len(eligible[bucket]) for bucket in QUOTAS},
        "genuineExactContentControlQuota": 0,
        "familyCount": family_count,
        "pairEvidence": [{
            "pairId": blind["pairs"][i]["pairId"],
            "candidateBucket": row[3],
            "dhashDistance": row[2]["dhashDistance"],
            "clipCosine": row[2]["clipCosine"],
            "sourceKind": row[2]["sourceKind"],
        } for i, row in enumerate(chosen)],
        "heldOutReviewLabelsRead": False,
        "calibrationReviewLabelsRead": False,
        "suppressionThresholdFrozen": False,
        "productionSuppressionApproved": False,
        "limitations": (
            "This is a photo-disjoint candidate-stratified HELD-OUT audit, "
            "not population-representative archive precision. Guard only covers "
            "existing dHash/seeded CLIP retrieved edges; unobserved leakage "
            "cannot be excluded. Excluding exposed heldout photos may bias "
            "evaluation, especially difficult cases. No automatic suppression "
            "rule or production threshold was frozen or approved."
        ),
    }
    return blind, audit


def create_files(candidate_path, calibration_path, blind_dest, audit_dest):
    if len({candidate_path, calibration_path, blind_dest, audit_dest}) != 4:
        raise ValueError("All validation paths must be distinct")
    if blind_dest.exists() or audit_dest.exists():
        raise ValueError("Held-out validation artifacts already exist")
    data = load_json(candidate_path)
    calibration_blind = load_json(calibration_path)
    blind, audit = prepare(data, calibration_blind)
    written = []
    try:
        for path, value in ((blind_dest, blind), (audit_dest, audit)):
            path.parent.mkdir(parents=True, exist_ok=True)
            with path.open("xb") as stream:
                stream.write(canonical_bytes(value))
            written.append(path)
    except OSError:
        for path in written:
            path.unlink(missing_ok=True)
        raise
    return {
        "protocol": BLIND_PROTOCOL,
        "status": audit["status"],
        "validationPairs": len(blind["pairs"]),
        "selectedByBucket": audit["selectedBucketCounts"],
        "distinctHeldOutPhotos": len({
            row[side] for row in blind["pairs"]
            for side in ("leftRevisionId", "rightRevisionId")
        }),
        "calibrationPilotPhotosProtected": audit["calibrationPilotPhotosProtected"],
        "heldOutPhotosQuarantinedByObservedLinks":
            audit["heldOutPhotosQuarantinedByObservedLinks"],
        "heldOutPhotosAfterQuarantine": audit["heldOutPhotosAfterQuarantine"],
        "calibrationHumanLabelsRead": False,
        "heldOutHumanLabelsRead": False,
        "productionSuppressionApproved": False,
    }


def validate_review_file(review, blind_sha, pair_ids):
    if (not isinstance(review, dict) or set(review) !=
            {"schemaVersion", "protocol", "pilotSha256", "reviews"}
            or type(review["schemaVersion"]) is not int
            or review["schemaVersion"] != 1
            or review["protocol"] != LABELS_PROTOCOL
            or review["pilotSha256"] != blind_sha
            or not isinstance(review["reviews"], list)):
        raise ValueError("Invalid or unbound held-out reviews")
    labelled = {}
    for row in review["reviews"]:
        if (not isinstance(row, dict) or set(row) !=
                {"pairId", "label", "notes"} or
                row["pairId"] not in pair_ids or row["pairId"] in labelled or
                row["label"] not in LABELS or not isinstance(row["notes"], str)
                or len(row["notes"]) > 1000):
            raise ValueError("Invalid or duplicate held-out label")
        labelled[row["pairId"]] = row["label"]
    return labelled


def report(blind_path, audit_path, labels_path):
    blind = load_json(blind_path)
    ids, _ = validate_holdout_blind(blind)
    pilot_sha = fingerprint(blind_path.read_bytes())
    audit = load_json(audit_path)
    if (not isinstance(audit, dict) or audit.get("protocol") != AUDIT_PROTOCOL
            or audit.get("status") !=
                "heldout-validation-frozen-before-human-labels"
            or audit.get("pilotSha256") != pilot_sha
            or audit.get("suppressionThresholdFrozen") is not False
            or not isinstance(audit.get("pairEvidence"), list)
            or len(audit["pairEvidence"]) != len(ids)
            or {p["pairId"] for p in audit["pairEvidence"]} != ids):
        raise ValueError("Validation audit not bound to frozen blind cohort")
    labelled = validate_review_file(load_json(labels_path), pilot_sha, ids)
    bucket_counts = defaultdict(Counter)
    for row in audit["pairEvidence"]:
        bucket = row["candidateBucket"]
        if bucket not in QUOTAS:
            raise ValueError("Unknown held-out bucket")
        if row["pairId"] in labelled:
            bucket_counts[bucket][labelled[row["pairId"]]] += 1
    result = {}
    for bucket in QUOTAS:
        counts = bucket_counts[bucket]
        n = counts["redundant"] + counts["similar-keep-both"] + counts["not-similar"]
        result[bucket] = {
            "reviewed": sum(counts.values()),
            "redundant": counts["redundant"],
            "similarKeepBoth": counts["similar-keep-both"],
            "notSimilar": counts["not-similar"],
            "unsure": counts["unsure"],
            "descriptiveRedundantShare": round(counts["redundant"] / n, 4) if n else None,
            "descriptiveWilson95": wilson_interval(counts["redundant"], n),
        }
    return {
        "protocol": "wi0194-heldout-validation-report-v1",
        "status": "heldout-descriptive-review-not-production-validation",
        "validationPairs": len(ids),
        "reviewedPairs": len(labelled),
        "remainingPairs": len(ids) - len(labelled),
        "bucketCounts": result,
        "reviewedOnlyAfterCohortFreeze": True,
        "calibrationReviewLabelsAccessed": False,
        "thresholdFrozen": False,
        "automaticSuppressionAuthorized": False,
        "limitations": audit["limitations"] + (
            " Bucket Wilson intervals are descriptive only, not independent "
            "photo-cluster confidence intervals, deployment precision, or "
            "an unbiased archive error estimate."
        ),
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    modes = parser.add_subparsers(dest="mode", required=True)
    first = modes.add_parser("prepare")
    first.add_argument("--input", required=True)
    first.add_argument("--calibration-blind", required=True)
    first.add_argument("--blind-output", required=True)
    first.add_argument("--audit-output", required=True)
    second = modes.add_parser("report")
    second.add_argument("--blind", required=True)
    second.add_argument("--audit", required=True)
    second.add_argument("--labels", required=True)
    args = parser.parse_args()
    try:
        if args.mode == "prepare":
            res = create_files(*(
                private_file(path, must_exist=required)
                for path, required in (
                    (args.input, True), (args.calibration_blind, True),
                    (args.blind_output, False), (args.audit_output, False)
                )))
        else:
            res = report(*(
                private_file(path, must_exist=True)
                for path in (args.blind, args.audit, args.labels)
            ))
    except (ValueError, OSError, TypeError, KeyError, OverflowError) as err:
        raise SystemExit("WI-0194 held-out validation failed (" +
                         type(err).__name__ + ")") from None
    print(json.dumps(res, indent=2, allow_nan=False))


if __name__ == "__main__":
    main()
