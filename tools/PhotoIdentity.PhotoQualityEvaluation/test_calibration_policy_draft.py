"""Offline contract tests for calibration-only, non-frozen IQA mapping drafts."""
import copy
import json
import unittest

from calibration_policy_draft import calibration_draft


class CalibrationPolicyDraftTests(unittest.TestCase):
    def setUp(self):
        photos = []
        scores = []
        for i in range(75):
            if i < 15:
                tier, score = "low", 0.2 + i * 0.003
            elif i < 45:
                tier, score = "normal", 0.5 + i * 0.001
            elif i < 70:
                tier, score = "high", 0.7 + i * 0.002
            else:
                tier, score = "very-low", 0.05 + i * 0.0001
            photos.append({
                "revisionId": f"c-{i}",
                "partition": "calibration",
                "sampleKind": "representative",
                "review": {"qualityLevel": tier},
            })
            scores.append({"revisionId": f"c-{i}", "rawScore": score,
                           "errorCode": None})
        # These human labels are deliberately extreme secrets: never consult them.
        for i in range(10):
            photos.append({
                "revisionId": f"h-{i}",
                "partition": "held-out",
                "sampleKind": "representative",
                "review": {"qualityLevel": "very-high", "notes": "secret"},
            })
            scores.append({"revisionId": f"h-{i}", "rawScore": 0.2,
                           "errorCode": None})
        for i in range(10):
            photos.append({
                "revisionId": f"t-{i}",
                "partition": "challenge",
                "sampleKind": "challenge",
                "review": {"qualityLevel": "very-low", "notes": "secret"},
            })
            scores.append({"revisionId": f"t-{i}", "rawScore": 0.6,
                           "errorCode": None})
        self.benchmark = {
            "schemaVersion": 2,
            "benchmarkId": "fake-frozen",
            "proxyProfile": "profile-id=jpeg-1600-q78",
            "photos": photos,
        }
        self.export = {
            "schemaVersion": 1,
            "benchmarkId": "fake-frozen",
            "inputKind": "review-proxy",
            "higherIsBetter": True,
            "candidate": {
                "candidateId": "pyiqa-musiq-test",
                "modelName": "musiq",
                "modelVersion": "weights-not-yet-pinned",
            },
            "scores": scores,
        }

    def test_three_band_proposal_is_explicitly_not_frozen(self):
        draft = calibration_draft(self.benchmark, self.export)
        self.assertEqual(draft["status"], "research-draft-not-frozen")
        self.assertFalse(draft["heldOutEvaluationAllowed"])
        self.assertFalse(draft["productionModelApproved"])
        self.assertFalse(draft["automaticVeryLowExclusionApproved"])
        self.assertEqual(draft["mapping"]["anchorCounts"],
                         {"low": 15, "normal": 30, "high": 25})
        self.assertEqual(draft["calibrationOnlyDiagnostic"]["ratedScored"], 75)
        self.assertLess(*draft["mapping"]["fixedOrientedScoreThresholds"])
        self.assertEqual(sum(sum(r.values()) for r in
                             draft["calibrationOnlyDiagnostic"]["confusion"].values()), 75)
        raw = json.dumps(draft)
        self.assertNotIn("secret", raw)
        self.assertNotIn("h-1", raw)
        self.assertNotIn("c-1", raw)
        self.assertNotIn("t-1", raw)

    def test_human_labels_outside_calibration_do_not_affect_draft(self):
        a = calibration_draft(self.benchmark, self.export)
        for row in self.benchmark["photos"]:
            if row["partition"] != "calibration":
                row["review"]["qualityLevel"] = "low"
                row["review"]["notes"] = "changed-secret"
        b = calibration_draft(self.benchmark, self.export)
        self.assertEqual(a, b)

    def test_rejects_incomplete_candidate_export(self):
        self.export["scores"].pop()
        with self.assertRaises(ValueError):
            calibration_draft(self.benchmark, self.export)

    def test_rejects_nonproxy_input(self):
        self.export["inputKind"] = "originals"
        with self.assertRaisesRegex(ValueError, "review-proxy"):
            calibration_draft(self.benchmark, self.export)

    def test_non_monotone_calibration_is_not_misrepresented(self):
        for row in self.export["scores"]:
            if row["revisionId"].startswith("c-"):
                i = int(row["revisionId"].split("-")[1])
                if 45 <= i < 70:
                    row["rawScore"] = 0.1
        with self.assertRaisesRegex(ValueError, "not strictly monotonic"):
            calibration_draft(self.benchmark, self.export)

    def test_cache_discovery_cannot_attest_model_mapping(self):
        evidence = {"mode": "cache-discovery-unverified", "artifacts": []}
        with self.assertRaisesRegex(ValueError, "explicit checkpoint"):
            calibration_draft(self.benchmark, self.export, evidence)

    def test_explicit_checkpoint_still_unverified_license_mapping(self):
        evidence = {
            "mode": "explicit-candidate-checkpoints",
            "checkpoints": {"musiq": {
                "artifactName": "musiq.pth", "sha256": "a" * 64,
                "sizeBytes": 123}},
        }
        draft = calibration_draft(self.benchmark, self.export, evidence)
        self.assertEqual(draft["checkpoint"]["sha256"], "a" * 64)
        self.assertEqual(draft["checkpoint"]["licenseStatus"], "not-verified")
        self.assertFalse(draft["checkpoint"]["mappingVerified"])
        self.assertFalse(draft["heldOutEvaluationAllowed"])

    def test_checkpoint_does_not_silently_accept_other_model(self):
        evidence = {"mode": "explicit-candidate-checkpoints",
                    "checkpoints": {"arniqa": {"sha256": "b" * 64}}}
        with self.assertRaisesRegex(ValueError, "checkpoint missing"):
            calibration_draft(self.benchmark, self.export, evidence)


if __name__ == "__main__":
    unittest.main()
