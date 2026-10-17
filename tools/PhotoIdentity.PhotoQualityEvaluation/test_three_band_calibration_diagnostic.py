"""Offline aggregate-only diagnostics for the two unsealed calibration drafts."""
from copy import deepcopy
import json
import unittest

from three_band_calibration_diagnostic import compare_drafts, metrics

BANDS = ("low-or-below", "normal", "high-or-above")


def draft(name, confusion):
    n = sum(sum(row.values()) for row in confusion.values())
    return {
        "schemaVersion": 1,
        "protocol": "wi0193-calibration-policy-draft-v1",
        "status": "research-draft-not-frozen",
        "benchmarkId": "do-not-print-private-benchmark-id",
        "benchmarkFingerprint": "same-population",
        "calibrationReviewSha256": "same-calibration-labels",
        "scoreExportSha256": "private-score-digest-" + name,
        "candidateId": name,
        "inputKind": "jpeg-1600-q78-review-proxy",
        "mapping": {"bandsAscending": list(BANDS)},
        "calibrationOnlyDiagnostic": {
            "ratedScored": n,
            "confusion": confusion,
            "apparentThreeBandAccuracy":
                sum(confusion[b][b] for b in BANDS) / n,
        },
        "heldOutEvaluationAllowed": False,
        "productionModelApproved": False,
        "automaticVeryLowExclusionApproved": False,
        "privateExtra": {
            "sourcePath": "secret-folder/portrait-of-a-person.jpg",
            "heldOutReview": "secret-very-low",
            "revisionId": "secret-revision",
        },
    }


class ThreeBandDiagnosticTests(unittest.TestCase):
    def setUp(self):
        self.topiq = draft("pyiqa-topiq_nr-0.1.16", {
            "low-or-below": {
                "low-or-below": 15, "normal": 17, "high-or-above": 9},
            "normal": {
                "low-or-below": 50, "normal": 49, "high-or-above": 55},
            "high-or-above": {
                "low-or-below": 18, "normal": 0, "high-or-above": 25},
        })
        self.musiq = draft("pyiqa-musiq-0.1.16", {
            "low-or-below": {
                "low-or-below": 14, "normal": 21, "high-or-above": 6},
            "normal": {
                "low-or-below": 45, "normal": 51, "high-or-above": 58},
            "high-or-above": {
                "low-or-below": 12, "normal": 6, "high-or-above": 25},
        })

    def test_user_observed_accuracy_and_majority_baseline(self):
        result = compare_drafts(self.topiq, self.musiq)
        a, b = result["left"], result["right"]
        self.assertEqual(a["calibrationRated"], 238)
        self.assertEqual(a["correct"], 89)
        self.assertEqual(b["correct"], 90)
        self.assertEqual(a["majorityBaselineCorrect"], 154)
        self.assertEqual(b["majorityBaselineCorrect"], 154)
        self.assertAlmostEqual(result["rightMinusLeftAccuracy"], 1 / 238)
        self.assertEqual(a["perClass"]["low-or-below"]["actualCount"], 41)
        self.assertEqual(a["perClass"]["high-or-above"]["actualCount"], 43)
        self.assertEqual(a["extremeCrossBandMistakes"], 27)
        self.assertGreater(a["balancedAccuracy"], 0)
        self.assertLessEqual(a["macroF1"], 1)
        self.assertFalse(result["heldOutLabelsAccessed"])
        self.assertFalse(result["productionModelApproved"])
        self.assertFalse(result["automaticVeryLowExclusionApproved"])
        output = json.dumps(result)
        for secret in ("secret-folder", "secret-revision",
                       "secret-very-low", "do-not-print-private-benchmark-id"):
            self.assertNotIn(secret, output)

    def test_calibration_digest_mismatch_rejected(self):
        self.musiq["calibrationReviewSha256"] = "changed-labels"
        with self.assertRaisesRegex(ValueError, "frozen calibration"):
            compare_drafts(self.topiq, self.musiq)

    def test_row_total_mismatch_rejected(self):
        self.musiq["calibrationOnlyDiagnostic"]["confusion"]["normal"]["normal"] += 1
        with self.assertRaisesRegex(ValueError, "Confusion counts"):
            compare_drafts(self.topiq, self.musiq)

    def test_accuracy_tampering_rejected(self):
        self.topiq["calibrationOnlyDiagnostic"]["apparentThreeBandAccuracy"] = 1
        with self.assertRaisesRegex(ValueError, "Recorded accuracy"):
            metrics(self.topiq)

    def test_approval_flag_must_remain_false(self):
        for flag in ("heldOutEvaluationAllowed", "productionModelApproved",
                     "automaticVeryLowExclusionApproved"):
            corrupted = deepcopy(self.topiq)
            corrupted[flag] = True
            with self.assertRaisesRegex(ValueError, "approval flag"):
                metrics(corrupted)

    def test_rejects_candidate_alias_or_mismatched_population(self):
        self.musiq["candidateId"] = self.topiq["candidateId"]
        with self.assertRaisesRegex(ValueError, "different candidate"):
            compare_drafts(self.topiq, self.musiq)
        self.musiq["candidateId"] = "pyiqa-musiq-0.1.16"
        self.musiq["calibrationOnlyDiagnostic"]["confusion"]["low-or-below"] = {
            "low-or-below": 15, "normal": 20, "high-or-above": 6}
        # Counts and recorded accuracy will first detect the manipulation.
        with self.assertRaises(ValueError):
            compare_drafts(self.topiq, self.musiq)

    def test_negative_or_boolean_confusion_rejected(self):
        self.topiq["calibrationOnlyDiagnostic"]["confusion"]["normal"]["normal"] = True
        with self.assertRaisesRegex(ValueError, "nonnegative integers"):
            metrics(self.topiq)
        self.topiq["calibrationOnlyDiagnostic"]["confusion"]["normal"]["normal"] = -1
        with self.assertRaisesRegex(ValueError, "nonnegative integers"):
            metrics(self.topiq)

    def test_empty_predicted_class_precision_is_null_not_infinite(self):
        confusion = {
            "low-or-below": {
                "low-or-below": 0, "normal": 41, "high-or-above": 0},
            "normal": {
                "low-or-below": 0, "normal": 154, "high-or-above": 0},
            "high-or-above": {
                "low-or-below": 0, "normal": 43, "high-or-above": 0},
        }
        report = metrics(draft("constant-normal", confusion))
        self.assertAlmostEqual(report["accuracy"], 154 / 238)
        self.assertAlmostEqual(report["balancedAccuracy"], 1 / 3)
        self.assertIsNone(report["perClass"]["low-or-below"]["precision"])


if __name__ == "__main__":
    unittest.main()
