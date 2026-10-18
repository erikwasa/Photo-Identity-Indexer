"""Synthetic-only tests for the approved, offline TOPIQ research freeze."""
import copy
import unittest

from ranking_freeze import CRITERIA, PROTOCOL, THRESHOLD, freeze
from ranking_freeze_proposal import CANDIDATE, propose


def fixture():
    """238 calibration scores in two groups give EXACT p75 gap THRESHOLD."""
    photos, scores = [], []
    for i in range(600):
        partition = "calibration" if i < 240 else "held-out" if i < 400 else "challenge"
        photos.append({
            "revisionId": f"private-revision-{i}",
            "partition": partition,
            "sampleKind": "representative" if i < 400 else "challenge",
            "review": {
                "qualityLevel": "normal" if i < 238 else "unsure",
                "notes": "sensitive private review",
            },
        })
        # 119 zeros and 119 exact cutoff scores: 14,161/28,203
        # gaps equal THRESHOLD, so nearest-rank p75 is THRESHOLD.
        raw = THRESHOLD if 119 <= i < 238 else 0.0
        scores.append({
            "revisionId": f"private-revision-{i}",
            "rawScore": raw,
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

    def test_exact_threshold_freezes_only_research_and_seals_heldout(self):
        self.assertEqual(
            self.proposal["hypothesis"]["fixedScoreGapThreshold"], THRESHOLD)
        frozen = freeze(self.proposal, self.benchmark, self.export)
        self.assertEqual(frozen["protocol"], PROTOCOL)
        self.assertTrue(frozen["researchHypothesisFrozen"])
        self.assertEqual(frozen["fixedScoreGapThreshold"], THRESHOLD)
        self.assertEqual(frozen["calibrationPairs"], 28203)
        self.assertFalse(frozen["heldOutEvaluationAllowed"])
        self.assertFalse(frozen["heldOutLabelsAccessed"])
        self.assertFalse(frozen["productionModelApproved"])
        self.assertFalse(frozen["automaticVeryLowExclusionApproved"])
        self.assertEqual(
            frozen["researchCriteria"]["minimumDifferentTierSelectedAccuracy"], .80)
        self.assertEqual(
            frozen["researchCriteria"]["minimumAllPairDecisionCoverage"], .20)
        self.assertEqual(
            frozen["researchCriteria"]["minimumTwoOrMoreTierSelectedAccuracy"], .80)
        self.assertEqual(frozen["proposalSha256"], frozen["proposalSha256"])
        self.assertNotIn("sensitive private review", str(frozen))
        self.assertNotIn("private-revision-", str(frozen))

    def test_threshold_shift_is_rejected_even_if_present_in_proposal(self):
        self.proposal["hypothesis"]["fixedScoreGapThreshold"] += 0.00000001
        with self.assertRaisesRegex(ValueError, "drifted"):
            freeze(self.proposal, self.benchmark, self.export)
        # Recompute a self-consistent but *different* calibration cutoff;
        # the approved exact constant must still reject it.
        for row in self.export["scores"]:
            if row["rawScore"] == THRESHOLD:
                row["rawScore"] = THRESHOLD + 0.01
        self.proposal = propose(self.benchmark, self.export)
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

    def test_heldout_and_challenge_review_changes_do_not_affect_freeze(self):
        expected = freeze(self.proposal, self.benchmark, self.export)
        for photo in self.benchmark["photos"]:
            if photo["partition"] != "calibration":
                photo["review"] = {
                    "qualityLevel": "very-high",
                    "notes": "secret heldout data changed",
                }
        self.assertEqual(expected, freeze(self.proposal, self.benchmark, self.export))

    def test_incompatible_model_and_approval_drift_rejected(self):
        self.export["candidate"]["candidateId"] = "pyiqa-musiq-0.1.16"
        with self.assertRaises(ValueError):
            freeze(self.proposal, self.benchmark, self.export)
        self.benchmark, self.export = fixture()
        self.proposal["heldOutEvaluationAllowed"] = True
        with self.assertRaisesRegex(ValueError, "drifted"):
            freeze(self.proposal, self.benchmark, self.export)

    def test_predeclared_criteria_include_no_production_permission(self):
        self.assertIs(CRITERIA["heldOutTuningOrAlternativeCandidateAllowed"], False)
        self.assertIs(CRITERIA["representativeHeldOutOnly"], True)


if __name__ == "__main__":
    unittest.main()
