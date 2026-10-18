"""Offline synthetic-only tests: no real held-out labels or score exports."""
import copy
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

from calibration_policy_draft import fingerprint
from heldout_ranking_one_shot import (
    APPROVAL_TEXT, AUTH_PROTOCOL, CLAIM_FILENAME, EVALUATION_PROTOCOL,
    HELDOUT_PHOTOS, PREFLIGHT_PROTOCOL, THRESHOLD, authorized,
    consume_once_claim, count_pairs, heldout_report, metrics, photo_bootstrap,
    preflight, research_decision, selected_photos,
)
from ranking_freeze import freeze
from ranking_freeze_proposal import propose
from test_ranking_freeze import fixture


def approval(verified):
    return {
        "schemaVersion": 1,
        "protocol": AUTH_PROTOCOL,
        "status": "explicit-maintainer-held-out-approval",
        "preflightSha256": fingerprint(verified),
        "approvalText": APPROVAL_TEXT,
        "oneTimeHeldOutEvaluationApproved": True,
        "productionModelApproved": False,
        "automaticVeryLowExclusionApproved": False,
    }


class HeldOutRankingOneShotTests(unittest.TestCase):
    def setUp(self):
        self.benchmark, self.scores = fixture()
        self.proposal = propose(self.benchmark, self.scores)
        self.frozen = freeze(self.proposal, self.benchmark, self.scores)
        self.sealed = preflight(
            self.benchmark, self.scores, self.proposal, self.frozen)

    def test_preflight_no_heldout_labels_and_frozen_flags(self):
        self.assertEqual(self.sealed["protocol"], PREFLIGHT_PROTOCOL)
        self.assertEqual(self.sealed["heldOutRepresentativePhotos"], HELDOUT_PHOTOS)
        self.assertEqual(self.sealed["fixedScoreGapThreshold"], THRESHOLD)
        self.assertFalse(self.sealed["heldOutEvaluationAllowed"])
        self.assertFalse(self.sealed["heldOutLabelsAccessed"])
        self.assertNotIn("private-revision-", json.dumps(self.sealed))
        baseline = copy.deepcopy(self.sealed)
        for photo in self.benchmark["photos"]:
            if photo["partition"] != "calibration":
                photo["review"] = {
                    "qualityLevel": "very-low", "notes": "sealed-label-secret"}
        self.assertEqual(baseline, preflight(
            self.benchmark, self.scores, self.proposal, self.frozen))
        self.assertNotIn("sealed-label-secret", str(self.sealed))

    def test_preflight_rejects_frozen_tampering_and_partition_sizes(self):
        self.frozen["fixedScoreGapThreshold"] += .01
        with self.assertRaisesRegex(ValueError, "attestation"):
            preflight(self.benchmark, self.scores, self.proposal, self.frozen)
        self.frozen = freeze(self.proposal, self.benchmark, self.scores)
        self.benchmark["photos"][300]["partition"] = "challenge"
        with self.assertRaisesRegex(ValueError, "membership"):
            preflight(self.benchmark, self.scores, self.proposal, self.frozen)

    def test_preflight_cohort_fingerprint_catches_swaps_in_separate_record(self):
        original = self.sealed
        self.benchmark["photos"][300]["partition"] = "challenge"
        self.benchmark["photos"][300]["sampleKind"] = "challenge"
        self.benchmark["photos"][410]["partition"] = "held-out"
        self.benchmark["photos"][410]["sampleKind"] = "representative"
        modified = preflight(
            self.benchmark, self.scores, self.proposal, self.frozen)
        self.assertNotEqual(
            original["heldOutCohortSha256"], modified["heldOutCohortSha256"])
        self.assertNotEqual(original, modified)

    def test_authorization_must_be_separate_exact_matched_and_nonproduction(self):
        good = approval(self.sealed)
        authorized(good, self.sealed)
        for key, value in [
            ("preflightSha256", "wrong-fingerprint"),
            ("approvalText", "implicit permission"),
            ("oneTimeHeldOutEvaluationApproved", False),
            ("productionModelApproved", True),
            ("automaticVeryLowExclusionApproved", True),
        ]:
            bad = dict(good, **{key: value})
            with self.subTest(key=key), self.assertRaises(ValueError):
                authorized(bad, self.sealed)
        with self.assertRaises(ValueError):
            authorized({}, self.sealed)
        with self.assertRaises(ValueError):
            authorized(dict(good, extra=True), self.sealed)

    def test_unrated_photos_excluded_from_accuracy_but_not_coverage(self):
        rows = [(0.0, 1), (THRESHOLD, 5), (0.0, None)]
        c = count_pairs(rows)
        self.assertEqual(c["allPairs"], 3)
        self.assertEqual(c["selectedPairs"], 2)
        self.assertEqual(c["abstainedPairs"], 1)
        self.assertEqual(c["unknownTierPairs"], 2)
        self.assertEqual(c["selectedDifferentTierPairs"], 1)
        self.assertEqual(c["correctDifferentTierPairs"], 1)
        self.assertEqual(c["selectedTwoOrMoreTierPairs"], 1)
        self.assertAlmostEqual(metrics(c)["allPairDecisionCoverage"], 2 / 3)

    def test_same_tier_selection_is_not_ordering_success(self):
        rows = [(0.0, 3), (0.5, 3), (0.8, 1), (0.9, 5)]
        c = count_pairs(rows)
        self.assertEqual(c["sameTierPairs"], 1)
        self.assertEqual(c["selectedSameTierPairs"], 1)
        self.assertEqual(c["allPairs"], 6)
        self.assertEqual(c["selectedPairs"] + c["abstainedPairs"], 6)
        self.assertEqual(
            c["correctTwoOrMoreTierPairs"] <= c["selectedTwoOrMoreTierPairs"],
            True)

    def test_undefined_metrics_fail_not_vacuously_pass(self):
        c = count_pairs([(0.3, 3), (0.4, 3)])
        d = research_decision(c, metrics(c))
        self.assertFalse(d["allResearchCriteriaMet"])
        self.assertIsNone(metrics(c)["differentTierOrderingAccuracy"])
        self.assertIsNone(metrics(c)["twoOrMoreTierOrderingAccuracy"])

    def test_report_is_aggregate_and_never_approves_deployment(self):
        photos = [p for p in self.benchmark["photos"]
                  if p["partition"] == "held-out"]
        for i, photo in enumerate(photos):
            photo["review"]["qualityLevel"] = ("high" if i % 2 else "low")
        rows = selected_photos(self.benchmark, self.scores)
        self.assertEqual(len(rows), 160)
        self.assertTrue(all(tier is not None for _, tier in rows))
        with patch("heldout_ranking_one_shot.photo_bootstrap", return_value={}):
            r = heldout_report(rows, self.sealed)
        self.assertEqual(r["protocol"], EVALUATION_PROTOCOL)
        self.assertEqual(r["counts"]["allPairs"], 12720)
        self.assertFalse(r["productionModelApproved"])
        self.assertFalse(r["automaticVeryLowExclusionApproved"])
        self.assertNotIn("private-revision-", json.dumps(r))
        self.assertNotIn("private-benchmark", json.dumps(r))
        self.assertNotIn("sensitive private review", json.dumps(r))

    def test_bootstrap_resamples_photos_with_fixed_seed(self):
        rows = [(0.05, 1), (0.35, 4), (0.55, 5), (0.8, 2)]
        a = photo_bootstrap(rows, repetitions=20, seed=44)
        b = photo_bootstrap(rows, repetitions=20, seed=44)
        self.assertEqual(a, b)
        self.assertEqual(a["repetitions"], 20)
        for value in a["endpoints"].values():
            self.assertEqual(
                value["definedReplicates"] + value["undefinedReplicates"], 20)

    def test_oneshot_claim_rejects_reuse_and_keeps_no_labels(self):
        with tempfile.TemporaryDirectory() as folder:
            frozen_file = Path(folder) / "frozen.json"
            frozen_file.write_text("{}", encoding="utf-8")
            auth = approval(self.sealed)
            consume_once_claim(frozen_file, self.sealed, auth)
            claim = frozen_file.with_name(CLAIM_FILENAME)
            self.assertTrue(claim.exists())
            content = claim.read_text(encoding="utf-8")
            self.assertNotIn("private-revision-", content)
            self.assertNotIn("sensitive private review", content)
            with self.assertRaises(ValueError):
                consume_once_claim(frozen_file, self.sealed, auth)


if __name__ == "__main__":
    unittest.main()
