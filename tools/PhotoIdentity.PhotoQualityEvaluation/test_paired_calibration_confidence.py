"""Offline WI-0193 bootstrap contract tests; no model downloads."""
import json
import unittest

from paired_calibration_confidence import confidence, pairwise_accuracy


class ConfidenceTests(unittest.TestCase):
    def setUp(self):
        labels = ["very-low", "low", "normal", "high", "very-high"] * 3
        self.benchmark = {
            "benchmarkId": "synthetic",
            "photos": [
                {"revisionId": f"cal-{i}", "partition": "calibration",
                 "sampleKind": "representative",
                 "review": {"qualityLevel": label}}
                for i, label in enumerate(labels)
            ] + [
                {"revisionId": "unseen", "partition": "held-out",
                 "sampleKind": "representative",
                 "review": {"qualityLevel": "very-low"}},
                {"revisionId": "challenge", "partition": "challenge",
                 "sampleKind": "challenge",
                 "review": {"qualityLevel": "high"}}
            ]
        }
        values = [1, 2, 3, 4, 5] * 3 + [9, 9]
        self.exports = [
            self.score("perfect", values),
            self.score("noisy", [3, 2, 1, 5, 4] * 3 + [0, 0])
        ]

    def score(self, name, values):
        return {
            "schemaVersion": 1,
            "benchmarkId": "synthetic",
            "candidate": {"candidateId": name, "modelName": name,
                          "modelVersion": "synthetic", "framework": "none",
                          "license": "test", "source": "test",
                          "preprocessing": "test", "scoreSemantics": "test",
                          "modelSizeBytes": 0, "executionProvider": "cpu"},
            "higherIsBetter": True,
            "inputKind": "review-proxy",
            "photoCount": len(values),
            "runtime": {"totalElapsedMilliseconds": 0,
                        "medianInferenceMilliseconds": 0,
                        "p95InferenceMilliseconds": 0},
            "scores": [
                {"revisionId": photo["revisionId"], "rawScore": value,
                 "inferenceMilliseconds": 0, "errorCode": None,
                 "diagnostics": []}
                for photo, value in zip(self.benchmark["photos"], values)
            ]
        }

    def test_labels_outside_calibration_cannot_affect_results(self):
        first = confidence(self.benchmark, self.exports, "perfect", "noisy", 100)
        self.benchmark["photos"][-1]["review"]["qualityLevel"] = "low"
        self.benchmark["photos"][-2]["review"]["qualityLevel"] = "very-high"
        second = confidence(self.benchmark, self.exports, "perfect", "noisy", 100)
        self.assertEqual(first, second)
        self.assertEqual(first["commonCalibrationPhotos"], 15)
        self.assertNotIn("cal-0", json.dumps(first))
        self.assertNotIn("unseen", json.dumps(first))

    def test_reproducible_and_metric_direction(self):
        a = confidence(self.benchmark, self.exports, "noisy", "perfect", 100)
        b = confidence(self.benchmark, self.exports, "noisy", "perfect", 100)
        self.assertEqual(a, b)
        self.assertGreater(a["rightMinusLeft"]["spearman"], 0)
        self.assertGreater(a["rightMinusLeft"]["pairwiseAccuracy"], 0)  # Synthetic inversion spans two human tiers.

    def test_rejects_partial_exports(self):
        self.exports[0]["scores"].pop()
        with self.assertRaises(ValueError):
            confidence(self.benchmark, self.exports, "perfect", "noisy", 100)

    def test_ties_are_not_correct_pairs(self):
        fraction, total, ties = pairwise_accuracy([1, 3, 5], [1, 1, 5])
        self.assertEqual((fraction, total, ties), (2 / 3, 3, 1))


if __name__ == "__main__":
    unittest.main()
