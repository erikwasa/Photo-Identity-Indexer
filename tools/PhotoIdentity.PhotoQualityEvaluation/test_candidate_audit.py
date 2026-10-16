"""Offline, no model-weight tests for candidate audit."""
import unittest
from candidate_audit import audit


class CandidateAuditTests(unittest.TestCase):
    def setUp(self):
        self.benchmark = {
            "benchmarkId": "b",
            "photos": [
                {"revisionId": "a", "partition": "calibration", "review": {"qualityLevel": "low"}},
                {"revisionId": "b", "partition": "calibration", "review": {"qualityLevel": "high"}},
                {"revisionId": "c", "partition": "held-out", "review": {"qualityLevel": "very-low"}},
                {"revisionId": "d", "partition": "challenge", "sampleKind": "challenge",
                 "review": {"qualityLevel": "high"}},
            ]
        }

    def score(self, candidate, values):
        return {"schemaVersion": 1, "benchmarkId": "b",
                "candidate": {"candidateId": candidate,
                              "modelVersion": "weights-not-yet-pinned",
                              "modelSizeBytes": None},
                "higherIsBetter": True,
                "runtime": {"medianInferenceMilliseconds": 1,
                            "p95InferenceMilliseconds": 2},
                "scores": [{"revisionId": key, "rawScore": value,
                            "errorCode": None} for key, value in
                           zip("abcd", values)]}

    def test_heldout_labels_never_contribute_to_metrics(self):
        one = audit(self.benchmark, [self.score("model", [1, 3, 500, 500])])
        self.benchmark["photos"][2]["review"]["qualityLevel"] = "very-high"
        two = audit(self.benchmark, [self.score("model", [1, 3, 500, 500])])
        self.assertEqual(one, two)
        self.assertEqual(one["candidates"][0]["calibrationSpearman"], 1.0)
        self.assertEqual(one["representativeCalibrationReviewed"], 2)

    def test_requires_complete_exact_coverage(self):
        score = self.score("model", [1, 2, 3, 4])
        score["scores"].pop()
        with self.assertRaisesRegex(ValueError, "full benchmark"):
            audit(self.benchmark, [score])

    def test_inference_errors_count_but_do_not_pollute_rank(self):
        score = self.score("model", [1, 2, 3, 4])
        score["scores"][0]["rawScore"] = None
        score["scores"][0]["errorCode"] = "inference-failed"
        result = audit(self.benchmark, [score])
        self.assertEqual(result["candidates"][0]["fullBenchmarkInferenceFailures"], 1)
        self.assertIsNone(result["candidates"][0]["calibrationSpearman"])

    def test_disagreement_uses_only_calibration(self):
        result = audit(self.benchmark, [
            self.score("a", [1, 2, 3, 4]),
            self.score("b", [2, 1, 3, 4])])
        self.assertEqual(result["candidatePairs"][0]["calibrationScoreRankCorrelation"], -1.0)
        self.assertEqual(result["candidatePairs"][0]["calibrationCommon"], 2)


if __name__ == "__main__":
    unittest.main()
