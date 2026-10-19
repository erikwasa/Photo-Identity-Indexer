"""Synthetic WI-0194 calibration-only feasibility tests; no real photos."""
import copy
import json
import unittest

from redundancy_calibration_pilot_preflight import analyze, category, QUOTAS
from test_redundancy_review_pairs import fixture


class CalibrationRiskPilotPreflightTests(unittest.TestCase):
    def test_aggregate_is_stable_and_heldout_not_sampled(self):
        data = fixture()
        report = analyze(data)
        shuffled = copy.deepcopy(data)
        shuffled["photos"].reverse()
        shuffled["pairs"].reverse()
        self.assertEqual(report, analyze(shuffled))
        self.assertEqual(report["photos"], 100)
        self.assertEqual(report["candidatePairs"], 149)
        self.assertEqual(report["heldOutPhotosInDryRun"], 0)
        self.assertEqual(report["exactCopyControlsRequested"], 0)
        self.assertFalse(report["pilotReviewManifestCreated"])
        self.assertFalse(report["humanLabelsAccessed"])
        self.assertFalse(report["reviewSetFrozen"])
        self.assertFalse(report["automaticSuppressionApproved"])
        self.assertLessEqual(
            report["dryRunPairs"] * 2,
            report["dryRunDistinctCalibrationPhotos"] *
            report["maxOccurrencesPerPhoto"])
        for bucket, row in report["bucketFeasibility"].items():
            self.assertLessEqual(row["dryRunSelectedPairs"], QUOTAS[bucket])
            self.assertLessEqual(row["dryRunSelectedPairs"], row["eligiblePairs"])
            self.assertEqual(
                row["dryRunShortfall"],
                QUOTAS[bucket] - row["dryRunSelectedPairs"])
        output = json.dumps(report)
        for secret in ("private-revision-", "leftRevisionId",
                       "rightRevisionId", "contentSha256",
                       "synthetic-redundancy-study"):
            self.assertNotIn(secret, output)

    def test_predeclared_disjoint_candidate_strata(self):
        def row(d, c, kind="retrieved"):
            return {"sourceKind": kind, "dhashDistance": d, "clipCosine": c}
        self.assertEqual(category(row(0, .99), True),
                         "genuine-exact-content-copy")
        self.assertEqual(category(row(None, None, "random-negative"), False),
                         "screened-random-control")
        self.assertEqual(category(row(3, .20), False),
                         "dhash-close-clip-low-disagreement")
        self.assertEqual(category(row(30, .95), False),
                         "dhash-far-clip-high-disagreement")
        self.assertEqual(category(row(4, .95), False),
                         "strict-dhash-0-to-4")
        self.assertEqual(category(row(6, .75), False),
                         "weak-dhash-5-to-8")
        self.assertEqual(category(row(11, .75), False),
                         "weak-dhash-9-to-12")
        self.assertEqual(category(row(18, .94), False),
                         "high-clip-other")
        self.assertEqual(category(row(31, .72), False),
                         "clip-mid-borderline")
        self.assertEqual(category(row(18, .72), False),
                         "other-candidate")

    def test_injected_review_labels_are_rejected(self):
        data = fixture()
        data["pairs"][0]["humanReview"] = "redundant"
        with self.assertRaises(ValueError):
            analyze(data)


if __name__ == "__main__":
    unittest.main()
