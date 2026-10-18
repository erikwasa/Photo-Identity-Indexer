"""Offline synthetic guards for a held-out-sealed ranking freeze proposal."""
import copy
import hashlib
import json
import unittest

from ranking_freeze_proposal import (
    CANDIDATE, CALIBRATION_RATED_COUNT, PROTOCOL, propose,
)


class RankingFreezeProposalTests(unittest.TestCase):
    def setUp(self):
        photos = []
        scores = []
        labels = ("very-low", "low", "normal", "high", "very-high")
        for i in range(600):
            partition = ("calibration" if i < 240 else
                         "held-out" if i < 400 else "challenge")
            quality = labels[i % 5] if i < 238 else "unsure"
            photo = {
                "revisionId": f"secret-id-{i}",
                "partition": partition,
                "sampleKind": "representative" if i < 400 else "challenge",
                "review": {
                    "qualityLevel": quality,
                    "notes": "private-personal-note",
                },
            }
            photos.append(photo)
            scores.append({
                "revisionId": photo["revisionId"],
                "rawScore": (i % 33) * 0.009 + i * 0.000001,
                "errorCode": None,
            })
        self.benchmark = {
            "schemaVersion": 2,
            "benchmarkId": "private-benchmark-reference",
            "proxyProfile": "profile-id=jpeg-1600-q78",
            "photos": photos,
        }
        self.export = {
            "schemaVersion": 1,
            "benchmarkId": "private-benchmark-reference",
            "inputKind": "review-proxy",
            "higherIsBetter": True,
            "candidate": {
                "candidateId": CANDIDATE,
                "modelName": "topiq_nr",
                "modelVersion": "pyiqa 0.1.16 weights-not-yet-pinned",
            },
            "scores": scores,
        }

    def test_proposal_is_bounded_private_and_not_approved(self):
        report = propose(self.benchmark, self.export)
        self.assertEqual(report["protocol"], PROTOCOL)
        self.assertEqual(report["status"], "review-proposal-not-frozen")
        self.assertEqual(report["hypothesis"]["quantile"], 0.75)
        self.assertEqual(report["hypothesis"]["calibrationPhotos"],
                         CALIBRATION_RATED_COUNT)
        self.assertEqual(report["hypothesis"]["calibrationPairs"], 28203)
        self.assertGreater(report["hypothesis"]["fixedScoreGapThreshold"], 0)
        for key in ("researchHypothesisFrozen", "heldOutLabelsAccessed",
                    "heldOutEvaluationAllowed", "productionModelApproved",
                    "automaticVeryLowExclusionApproved",
                    "checkpointLoadedIdentityVerified",
                    "frameworkAndCheckpointLicenseApproved"):
            self.assertIs(report[key], False)
        data = json.dumps(report)
        for secret in ("private-personal-note", "secret-id-",
                       "private-benchmark-reference"):
            self.assertNotIn(secret, data.replace(
                '"benchmarkId": "private-benchmark-reference"', '"benchmarkId": "redacted"'))
        # Benchmark ID is allowed ONLY in private JSON, never in CLI summary.
        self.assertEqual(report["benchmarkId"], "private-benchmark-reference")

    def test_heldout_and_challenge_human_label_changes_have_no_effect(self):
        before = propose(self.benchmark, self.export)
        for row in self.benchmark["photos"]:
            if row["partition"] != "calibration":
                row["review"] = {"qualityLevel": "very-high",
                                 "notes": "mutated-heldout-secret"}
        after = propose(self.benchmark, self.export)
        self.assertEqual(before, after)

    def test_calibration_label_mutation_invalidates_review_fingerprint(self):
        before = propose(self.benchmark, self.export)
        self.benchmark["photos"][0]["review"]["qualityLevel"] = "high"
        after = propose(self.benchmark, self.export)
        self.assertNotEqual(before["calibrationReviewSha256"],
                            after["calibrationReviewSha256"])
        self.assertEqual(before["hypothesis"]["fixedScoreGapThreshold"],
                         after["hypothesis"]["fixedScoreGapThreshold"])

    def test_changing_review_eligibility_is_rejected(self):
        self.benchmark["photos"][0]["review"]["qualityLevel"] = "unsure"
        with self.assertRaisesRegex(ValueError, "238"):
            propose(self.benchmark, self.export)

    def test_export_must_be_complete_and_correct_model(self):
        self.export["scores"].pop()
        with self.assertRaises(ValueError):
            propose(self.benchmark, self.export)
        self.export["scores"].append({"revisionId": "secret-id-599",
                                       "rawScore": 0.4, "errorCode": None})
        self.export["candidate"]["candidateId"] = "pyiqa-musiq-0.1.16"
        with self.assertRaisesRegex(ValueError, "TOPIQ NR"):
            propose(self.benchmark, self.export)

    def test_wrong_input_kind_or_score_direction_rejected(self):
        self.export["inputKind"] = "originals"
        with self.assertRaisesRegex(ValueError, "TOPIQ NR"):
            propose(self.benchmark, self.export)
        self.export["inputKind"] = "review-proxy"
        self.export["higherIsBetter"] = False
        with self.assertRaisesRegex(ValueError, "TOPIQ NR"):
            propose(self.benchmark, self.export)

    def test_tampered_calibration_score_rejected(self):
        self.export["scores"][0]["rawScore"] = float("nan")
        with self.assertRaises(ValueError):
            propose(self.benchmark, self.export)


if __name__ == "__main__":
    unittest.main()
