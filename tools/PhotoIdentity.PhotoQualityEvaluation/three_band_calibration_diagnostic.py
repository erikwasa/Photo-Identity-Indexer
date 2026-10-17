#!/usr/bin/env python3
"""Compare two stored, unapproved WI-0193 calibration three-band drafts.

Uses ONLY aggregate confusion matrices already recorded in private policy
drafts: no image/model inference, no benchmark/score rows and no human
held-out or challenge labels are read. This cannot estimate held-out
performance or choose a production model.
"""
from __future__ import annotations

import argparse
import json
import math

from calibration_policy_draft import BANDS, PROTOCOL
from candidate_audit import load

REPORT_PROTOCOL = "wi0193-three-band-calibration-diagnostic-v1"


def validate_draft(draft):
    if (draft.get("schemaVersion") != 1 or draft.get("protocol") != PROTOCOL
            or draft.get("status") != "research-draft-not-frozen"):
        raise ValueError("Expected an unapproved calibration-only draft")
    if any(draft.get(field) is not False for field in (
            "heldOutEvaluationAllowed", "productionModelApproved",
            "automaticVeryLowExclusionApproved")):
        raise ValueError("Unexpected model/held-out/exclusion approval flag")
    if draft.get("inputKind") != "jpeg-1600-q78-review-proxy":
        raise ValueError("Expected frozen review proxy score input")
    if not all(isinstance(draft.get(key), str) and draft[key] for key in (
            "benchmarkId", "benchmarkFingerprint", "calibrationReviewSha256",
            "scoreExportSha256", "candidateId")):
        raise ValueError("Missing candidate or calibration provenance")
    if draft.get("mapping", {}).get("bandsAscending") != list(BANDS):
        raise ValueError("Three-band mapping or direction changed")
    diagnostic = draft.get("calibrationOnlyDiagnostic") or {}
    rows = diagnostic.get("confusion")
    if not isinstance(rows, dict) or set(rows) != set(BANDS):
        raise ValueError("Expected complete true-vs-predicted confusion")
    counts = {}
    for true_band in BANDS:
        row = rows[true_band]
        if not isinstance(row, dict) or set(row) != set(BANDS):
            raise ValueError("Expected exactly three predicted bands")
        for predicted in BANDS:
            val = row[predicted]
            if type(val) is not int or val < 0:
                raise ValueError("Confusion entries must be nonnegative integers")
        counts[true_band] = dict(row)
    total = sum(sum(row.values()) for row in counts.values())
    if total == 0 or total != diagnostic.get("ratedScored"):
        raise ValueError("Confusion counts and calibrated sample disagree")
    correct = sum(counts[band][band] for band in BANDS)
    recorded = diagnostic.get("apparentThreeBandAccuracy")
    if (type(recorded) not in (int, float) or not math.isfinite(recorded)
            or not math.isclose(correct / total, recorded, abs_tol=1e-12)):
        raise ValueError("Recorded accuracy does not match confusion")
    return counts, total


def metrics(draft):
    confusion, total = validate_draft(draft)
    true_counts = {band: sum(confusion[band].values()) for band in BANDS}
    predicted_counts = {band: sum(confusion[actual][band] for actual in BANDS)
                        for band in BANDS}
    correct = sum(confusion[band][band] for band in BANDS)
    per_class = {}
    for band in BANDS:
        true_positive = confusion[band][band]
        actual = true_counts[band]
        predicted = predicted_counts[band]
        recall = true_positive / actual if actual else None
        precision = true_positive / predicted if predicted else None
        f1 = (2 * true_positive / (actual + predicted)
              if actual + predicted else None)
        per_class[band] = {
            "actualCount": actual,
            "predictedCount": predicted,
            "correctCount": true_positive,
            "precision": precision,
            "recall": recall,
            "f1": f1,
        }
    present = [band for band in BANDS if true_counts[band]]
    majority = max(BANDS, key=lambda band: true_counts[band])
    majority_count = true_counts[majority]
    return {
        "candidate": draft["candidateId"],
        "calibrationRated": total,
        "correct": correct,
        "accuracy": correct / total,
        "majorityConstantPrediction": majority,
        "majorityBaselineCorrect": majority_count,
        "majorityBaselineAccuracy": majority_count / total,
        "accuracyMinusMajorityBaseline": (correct - majority_count) / total,
        "balancedAccuracy": (
            sum(per_class[band]["recall"] for band in present) / len(present)),
        "macroF1": (sum(per_class[band]["f1"] for band in present
                        if per_class[band]["f1"] is not None) / len(present)),
        "perClass": per_class,
        "confusionTrueRowsPredictedColumns": confusion,
        "extremeCrossBandMistakes": (
            confusion["low-or-below"]["high-or-above"] +
            confusion["high-or-above"]["low-or-below"]),
    }


def compare_drafts(left, right):
    lm = metrics(left)
    rm = metrics(right)
    if left["candidateId"] == right["candidateId"]:
        raise ValueError("Provide two different candidate drafts")
    if any(left.get(key) != right.get(key) for key in (
            "benchmarkId", "benchmarkFingerprint", "calibrationReviewSha256",
            "inputKind")):
        raise ValueError("Drafts do not share a frozen calibration reference")
    if lm["calibrationRated"] != rm["calibrationRated"]:
        raise ValueError("Drafts must cover same calibration review population")
    if any(lm["perClass"][band]["actualCount"] !=
           rm["perClass"][band]["actualCount"] for band in BANDS):
        raise ValueError("Drafts disagree on ground-truth three-band counts")
    return {
        "protocol": REPORT_PROTOCOL,
        "scope": "aggregate representative calibration drafts only",
        "left": lm,
        "right": rm,
        "rightMinusLeftAccuracy": rm["accuracy"] - lm["accuracy"],
        "samePhotoPairwiseDifferenceAvailable": False,
        "heldOutLabelsAccessed": False,
        "heldOutEvaluationAllowed": False,
        "productionModelApproved": False,
        "automaticVeryLowExclusionApproved": False,
        "interpretation": (
            "Confusions and balanced/macro metrics are apparent calibration "
            "diagnostics, not cross-validated or held-out accuracy. Constant "
            "Normal may have higher raw accuracy by exploiting class imbalance "
            "but cannot rank photos. Do not optimize thresholds against held-out "
            "ratings or freeze either draft from these diagnostics."),
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--left", required=True, help="Stored private TOPIQ draft")
    parser.add_argument("--right", required=True, help="Stored private MUSIQ draft")
    args = parser.parse_args()
    result = compare_drafts(load(args.left), load(args.right))
    # No identifiers, source paths, scores, human-review notes or photos.
    print(json.dumps(result, indent=2, allow_nan=False))
    print("Calibration aggregates only; no held-out labels accessed.")


if __name__ == "__main__":
    main()
