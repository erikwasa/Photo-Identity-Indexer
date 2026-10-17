#!/usr/bin/env python3
"""Draft a calibration-only, fixed-threshold 3-band quality policy.

This is NOT a model selection, five-level quality calibration, deployed
filter, held-out evaluation or permission to exclude Very Low photographs.
No held-out/challenge human ratings are accessed.
"""
from __future__ import annotations

import argparse
from collections import Counter
import hashlib
import json
import math
from pathlib import Path
import statistics

from candidate_audit import LEVELS, audit, load
from learned_iqa import private_output

PROTOCOL = "wi0193-calibration-policy-draft-v1"
ANCHORS = ("low", "normal", "high")
BANDS = ("low-or-below", "normal", "high-or-above")


def fingerprint(data):
    serialized = json.dumps(data, sort_keys=True, separators=(",", ":"),
                            ensure_ascii=True, allow_nan=False)
    return hashlib.sha256(serialized.encode("utf-8")).hexdigest()


def calibration_draft(benchmark, score_export, checkpoint=None):
    if benchmark.get("schemaVersion") != 2:
        raise ValueError("Require frozen 600-photo benchmark v2")
    if "profile-id=jpeg-1600-q78" not in benchmark.get("proxyProfile", "").splitlines():
        raise ValueError("Require frozen jpeg-1600-q78 review proxies")
    if score_export.get("inputKind") != "review-proxy":
        raise ValueError("Require unmodified full review-proxy candidate scores")
    # The audit validates exact full-benchmark score coverage and calibration
    # labels only. It does not access human reviews outside calibration.
    audit(benchmark, [score_export])
    descriptor = score_export["candidate"]
    candidate_id = descriptor["candidateId"]
    model_name = descriptor.get("modelName")
    sign = 1 if score_export["higherIsBetter"] else -1
    score_map = {
        row["revisionId"].casefold(): row["rawScore"] * sign
        for row in score_export["scores"] if row["rawScore"] is not None
    }
    eligible = [
        (p["review"]["qualityLevel"], score_map[p["revisionId"].casefold()])
        for p in benchmark["photos"]
        if p.get("partition") == "calibration"
        and p.get("sampleKind") == "representative"
        and p.get("review", {}).get("qualityLevel") in LEVELS
        and p["revisionId"].casefold() in score_map
    ]
    tiers = {level: [score for label, score in eligible if label == level]
             for level in ANCHORS}
    # Median anchors are robust but calibration-derived and optimistic.
    if any(len(tiers[level]) < 10 for level in ANCHORS):
        raise ValueError("Need >=10 calibration ratings in each Low/Normal/High anchor")
    medians = {level: statistics.median(tiers[level]) for level in ANCHORS}
    if not medians["low"] < medians["normal"] < medians["high"]:
        raise ValueError("Anchor medians are not strictly monotonic; reject this draft")
    a = (medians["low"] + medians["normal"]) / 2
    b = (medians["normal"] + medians["high"]) / 2
    if not (math.isfinite(a) and math.isfinite(b) and a < b):
        raise ValueError("Invalid calibration thresholds")

    def prediction(score):
        return BANDS[0] if score < a else BANDS[2] if score >= b else BANDS[1]

    def human_band(label):
        return BANDS[0] if LEVELS[label] < 3 else (
            BANDS[2] if LEVELS[label] > 3 else BANDS[1])

    confusion = {name: {pred: 0 for pred in BANDS} for name in BANDS}
    for label, value in eligible:
        confusion[human_band(label)][prediction(value)] += 1
    observed_accuracy = (sum(confusion[name][name] for name in BANDS)
                         / len(eligible))
    checkpoint_evidence = None
    if checkpoint is not None:
        # Cache-discovery mode cannot attest model-to-file identity.
        if checkpoint.get("mode") != "explicit-candidate-checkpoints":
            raise ValueError("Use explicit checkpoint selection, not cache discovery")
        row = checkpoint.get("checkpoints", {}).get(model_name)
        if not isinstance(row, dict) or not isinstance(row.get("sha256"), str):
            raise ValueError("Selected candidate checkpoint missing from provenance")
        if len(row["sha256"]) != 64 or not all(
                c in "0123456789abcdef" for c in row["sha256"].lower()):
            raise ValueError("Invalid checkpoint SHA-256")
        checkpoint_evidence = {
            "artifactName": row["artifactName"],
            "sha256": row["sha256"],
            "sizeBytes": row["sizeBytes"],
            "licenseStatus": "not-verified",
            "mappingVerified": False,
            "warning": "Even explicit selection does not prove the model loaded these weights",
        }
    return {
        "schemaVersion": 1,
        "protocol": PROTOCOL,
        "status": "research-draft-not-frozen",
        "benchmarkId": benchmark["benchmarkId"],
        "benchmarkFingerprint": fingerprint({
            "benchmarkId": benchmark["benchmarkId"],
            "representativeCalibrationRevisions": sorted(
                p["revisionId"] for p in benchmark["photos"]
                if p.get("partition") == "calibration"
                and p.get("sampleKind") == "representative"
            ),
        }),
        "calibrationReviewSha256": fingerprint([
            {"revisionId": p["revisionId"],
             "qualityLevel": p.get("review", {}).get("qualityLevel")}
            for p in benchmark["photos"]
            if p.get("partition") == "calibration"
            and p.get("sampleKind") == "representative"
        ]),
        "candidateId": candidate_id,
        "candidateModel": model_name,
        "candidateDescriptor": descriptor,
        "scoreExportSha256": fingerprint(score_export),
        "inputKind": "jpeg-1600-q78-review-proxy",
        "directionSign": sign,
        "mapping": {
            "scope": "exploratory-coarse-three-band, not five-tier ordinal mapping",
            "method": "midpoints of calibration Low/Normal/High score medians",
            "fixedOrientedScoreThresholds": [a, b],
            "bandsAscending": list(BANDS),
            "medianAnchorScores": medians,
            "anchorCounts": {name: len(tiers[name]) for name in ANCHORS}
        },
        "calibrationOnlyDiagnostic": {
            "ratedScored": len(eligible),
            "confusion": confusion,
            "apparentThreeBandAccuracy": observed_accuracy,
            "warning": "In-sample calibration only, optimistic; not held-out evidence",
        },
        "checkpoint": checkpoint_evidence,
        "heldOutEvaluationAllowed": False,
        "productionModelApproved": False,
        "automaticVeryLowExclusionApproved": False,
        "veryLowExclusionReason": (
            "Three known Very Low calibration examples; no safe exclusion evidence"),
        "notes": (
            "A proposed numeric absolute-score mapping on a fixed calibration set. "
            "No held-out/challenge human labels inspected. Do not deploy, "
            "score held-out labels, or treat this output as a frozen policy."),
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", required=True, help="Private 600-photo benchmark")
    parser.add_argument("--scores", required=True, help="Exactly one full 600-photo candidate export")
    parser.add_argument("--checkpoint-evidence", help="Optional explicit checkpoint inventory")
    parser.add_argument("--output", required=True, help="Private draft JSON; exclusive creation")
    args = parser.parse_args()
    benchmark = load(args.input)
    scores = load(args.scores)
    checkpoint = load(args.checkpoint_evidence) if args.checkpoint_evidence else None
    result = calibration_draft(benchmark, scores, checkpoint)
    output = private_output(args.output)
    output.parent.mkdir(parents=True, exist_ok=True)
    with output.open("x", encoding="utf-8") as handle:
        json.dump(result, handle, indent=2, allow_nan=False)
    # Only aggregate evidence; never emit benchmark ID, source/revision paths or rows.
    print(json.dumps({
        "protocol": result["protocol"],
        "status": result["status"],
        "candidate": result["candidateId"],
        "anchorCounts": result["mapping"]["anchorCounts"],
        "fixedThresholds": result["mapping"]["fixedOrientedScoreThresholds"],
        "ratedScored": result["calibrationOnlyDiagnostic"]["ratedScored"],
        "apparentThreeBandAccuracy":
            result["calibrationOnlyDiagnostic"]["apparentThreeBandAccuracy"],
        "heldOutEvaluationAllowed": result["heldOutEvaluationAllowed"],
        "productionModelApproved": result["productionModelApproved"],
        "automaticVeryLowExclusionApproved": result["automaticVeryLowExclusionApproved"]
    }, indent=2, allow_nan=False))
    print("Calibration draft only; kept under private/, not frozen or approved.")


if __name__ == "__main__":
    main()
