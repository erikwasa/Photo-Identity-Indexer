"""Synthetic-only tests for the approved, offline TOPIQ research freeze."""
import copy
import unittest

from ranking_freeze import CRITERIA, THRESHOLD, freeze
from ranking_freeze_proposal import CANDIDATE, propose


def fixture():
    photos, scores = [], []
    for i in range(600):
        partition = "calibration" if i < 240 else "held-out" if i < 400 else "challenge"
        photos.append({
            "revisionId": f"private-revision-{i}",
            "partition": partition,
            "sampleKind": "representative" if i < 400 else "challenge",
            "review": {
                "qualityLevel": ("normal" if i < 238 else "unsure"),
                "notes": "sensitive private review",
            },
        })
        scores.append({
            "revisionId": f"private-revision-{i}",
            "rawScore": 0.4 + 0.003 * i,
            "errorCode": None,
        })
    benchmark = {
        "schemaVersion": 2,
        "benchmarkId": "private-benchmark",
        "proxyProfile": "profile-id=jpeg-1600-q78",
        "photos": photos,
    }
    export = {
        "schemaVersion": 1,
        "benchmarkId": "private-benchmark",
        "inputKind": "review-proxy",
        "higherIsBetter": True,
        "candidate": {"candidateId": CANDIDATE, "modelName": "topiq_nr"},
        "scores": scores,
    }
    return benchmark, export


class RankingFreezeTests(unittest.TestCase):
    def setUp(self):
        self.benchmark, self.export = fixture()
        self.proposal = propose(self.benchmark, self.export)
        # The artificial synthetic fixture has a different score-gap quantile
        # from the operator's real benchmark; tests patch the raw scores to
        # reconstruct the exact approved numeric gap where appropriate.

    def test_real_cutoff_is_never_approximated_or_rederived_as_approval(self):
        self.assertNotEqual(
            self.proposal["hypothesis"]["fixedScoreGapThreshold"], THRESHOLD)
        with self.assertRaisesRegex(ValueError, "not approved"):
            freeze(self.proposal, self.benchmark, self.export)

    def test_reject_mutated_calibration_label_and_scores(self):
        self.benchmark["photos"][0]["review"]["qualityLevel"] = "very-high"
        with self.assertRaisesRegex(ValueError, "drifted"):
            freeze(self.proposal, self.benchmark, self.export)
        self.benchmark, self.export = fixture()
        self.export["scores"][0]["rawScore"] += 0.03
        with self.assertRaisesRegex(ValueError, "drifted"):
            freeze(self.proposal, self.benchmark, self.export)

    def test_heldout_and_challenge_review_changes_do_not_enter_proposal(self):
        expected = copy.deepcopy(self.proposal)
        for photo in self.benchmark["photos"]:
            if photo["partition"] != "calibration":
                photo["review"] = {
                    "qualityLevel": "very-high",
                    "notes": "secret heldout data changed",
                }
        self.assertEqual(expected, propose(self.benchmark, self.export))
        with self.assertRaisesRegex(ValueError, "not approved"):
            freeze(self.proposal, self.benchmark, self.export)

    def test_incompatible_model_or_approval_drift_rejected(self):
        self.export["candidate"]["candidateId"] = "pyiqa-musiq-0.1.16"
        with self.assertRaises(ValueError):
            freeze(self.proposal, self.benchmark, self.export)
        self.benchmark, self.export = fixture()
        self.proposal["heldOutEvaluationAllowed"] = True
        with self.assertRaisesRegex(ValueError, "drifted"):
            freeze(self.proposal, self.benchmark, self.export)

    def test_predeclared_criteria_never_include_production_approval(self):
        self.assertEqual(CRITERIA["minimumDifferentTierSelectedAccuracy"], 0.80)
        self.assertEqual(CRITERIA["minimumAllPairDecisionCoverage"], 0.20)
        self.assertEqual(CRITERIA["minimumTwoOrMoreTierSelectedAccuracy"], 0.80)
        self.assertIs(CRITERIA["heldOutTuningOrAlternativeCandidateAllowed"], False)
        self.assertEqual(len(str(THRESHOLD)), len("0.16755089163780212"))


if __name__ == "__main__":
    unittest.main()
