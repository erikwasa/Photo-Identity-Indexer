"""No-inference, calibration-only contract tests for selective ranking."""
from copy import deepcopy
import hashlib
import json
import unittest

from calibration_selective_ranking import (
    GAP_QUANTILES, PROTOCOL, evaluate, fold_number, threshold_for,
)


class SelectiveRankingTests(unittest.TestCase):
    def setUp(self):
        labels = ("very-low", "low", "normal", "high", "very-high")
        photos = []
        left_rows = []
        right_rows = []
        for i in range(255):
            partition = "calibration" if i < 220 else (
                "held-out" if i < 240 else "challenge")
            label = labels[i % 5]
            quality = i % 5
            revision = f"private-revision-{i}"
            digest = hashlib.sha256(f"image-{i//2}".encode()).hexdigest()
            photos.append({
                "revisionId": revision,
                "contentSha256": digest,
                "partition": partition,
                "sampleKind": "representative" if i < 240 else "challenge",
                "review": {
                    "qualityLevel": label,
                    "notes": "confidential-note-do-not-emit",
                },
            })
            # Numeric scores are correlated with human tiers but not exact;
            # the tool must never emit them or use held-out human labels.
            left_rows.append({
                "revisionId": revision,
                "rawScore": quality * 0.13 + (i % 7) * 0.01,
                "errorCode": None,
            })
            right_rows.append({
                "revisionId": revision,
                "rawScore": quality * 10 + (i % 11) * 0.6,
                "errorCode": None,
            })
        self.benchmark = {
            "schemaVersion": 2,
            "benchmarkId": "secret-benchmark-id",
            "proxyProfile": "profile-id=jpeg-1600-q78",
            "photos": photos,
        }
        self.exports = [
            {"schemaVersion": 1,
             "benchmarkId": "secret-benchmark-id",
             "inputKind": "review-proxy",
             "higherIsBetter": True,
             "candidate": {
                 "candidateId": name,
                 "modelVersion": "pyiqa-test",
             },
             "scores": rows}
            for name, rows in (("topiq_nr", left_rows), ("musiq", right_rows))
        ]

    def test_folds_calibration_only_and_gated(self):
        result = evaluate(self.benchmark, self.exports)
        self.assertEqual(result["protocol"], PROTOCOL)
        self.assertEqual(result["quantiles"], list(GAP_QUANTILES))
        self.assertFalse(result["heldOutLabelsAccessed"])
        self.assertFalse(result["heldOutEvaluationAllowed"])
        self.assertFalse(result["automaticVeryLowExclusionApproved"])
        self.assertFalse(result["productionModelApproved"])
        self.assertEqual(len(result["candidateReports"]), 2)
        for candidate in result["candidateReports"]:
            self.assertEqual(candidate["jointCalibrationPhotos"], 220)
            self.assertEqual(sum(candidate["foldSizes"]), 220)
            all_pairs = candidate["quantiles"]["0.0"]
            most = candidate["quantiles"]["0.9"]
            self.assertGreater(all_pairs["twoTierOrMorePairs"], 0)
            self.assertGreater(all_pairs["differentTierPairs"], 0)
            self.assertGreater(all_pairs["twoTierOrMoreOrderingAccuracy"], 0.5)
            self.assertGreaterEqual(all_pairs["selectedPairs"], most["selectedPairs"])
            self.assertEqual(len(most["foldDiagnostics"]), 5)
            self.assertLessEqual(most["pairCoverage"], 1)
        raw = json.dumps(result)
        for private in ("secret-benchmark-id", "private-revision-",
                        "confidential-note-do-not-emit"):
            self.assertNotIn(private, raw)

    def test_heldout_and_challenge_review_mutation_has_no_effect(self):
        a = evaluate(self.benchmark, self.exports)
        for row in self.benchmark["photos"]:
            if row["partition"] != "calibration":
                row["review"] = {
                    "qualityLevel": "very-low", "notes": "changed-private-note"}
        b = evaluate(self.benchmark, self.exports)
        self.assertEqual(a, b)

    def test_content_sha_duplicate_group_stays_together(self):
        a = self.benchmark["photos"][0]
        b = self.benchmark["photos"][1]
        self.assertEqual(a["contentSha256"], b["contentSha256"])
        self.assertEqual(
            fold_number(self.benchmark["benchmarkId"], a),
            fold_number(self.benchmark["benchmarkId"], b))

    def test_full_export_required(self):
        self.exports[0]["scores"].pop()
        with self.assertRaisesRegex(ValueError, "full benchmark"):
            evaluate(self.benchmark, self.exports)

    def test_review_proxy_required(self):
        self.exports[1]["inputKind"] = "original"
        with self.assertRaisesRegex(ValueError, "review-proxy"):
            evaluate(self.benchmark, self.exports)

    def test_different_models_required(self):
        self.exports[1]["candidate"]["candidateId"] = "topiq_nr"
        with self.assertRaises(ValueError):
            evaluate(self.benchmark, self.exports)

    def test_low_rating_count_refused(self):
        for i in range(100):
            self.benchmark["photos"][i]["review"]["qualityLevel"] = "unsure"
        with self.assertRaisesRegex(ValueError, "150"):
            evaluate(self.benchmark, self.exports)

    def test_quantile_gap_monotonic_and_nonzero_baseline(self):
        self.assertEqual(threshold_for([1, 2, 3], 0), 0)
        self.assertEqual(threshold_for([1, 2, 3, 4], 0.5), 2)
        self.assertEqual(threshold_for([1, 2, 3, 4], 0.9), 4)


if __name__ == "__main__":
    unittest.main()
