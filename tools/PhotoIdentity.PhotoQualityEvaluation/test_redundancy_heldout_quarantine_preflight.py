"""Synthetic-only risk-quarantine sensitivity tests; no image/review access."""
import copy
import json
import unittest

from redundancy_heldout_quarantine_preflight import quarantine_scenarios
from redundancy_review_pairs import prepare_inventory
from test_redundancy_review_pairs import fixture


def augmented_fixture():
    evidence = fixture()
    inventory, pairs, membership, _ = prepare_inventory(evidence)
    candidates = [
        (a, b) for a in sorted(inventory) for b in sorted(inventory)
        if a < b and membership[a] != membership[b] and (a, b) not in pairs
    ]
    chosen = []
    heldout_ids = set()
    for a, b in candidates:
        heldout = a if membership[a] == "held-out" else b
        if heldout not in heldout_ids:
            chosen.append((a, b))
            heldout_ids.add(heldout)
        if len(chosen) == 3:
            break
    assert len(chosen) == 3
    for (a, b), dhash, cosine in zip(
        chosen, (6, 11, 30), (0.60, 0.60, 0.95)
    ):
        evidence["pairs"].append({
            "leftRevisionId": a,
            "rightRevisionId": b,
            "sourceKind": "retrieved",
            "dhashDistance": dhash,
            "clipCosine": cosine,
        })
    return evidence


class RedundancyHeldoutQuarantineTests(unittest.TestCase):
    def test_nested_risk_exclusions_and_fixed_photo_disjoint_baseline(self):
        report = quarantine_scenarios(augmented_fixture())
        runs = report["scenarios"]
        self.assertEqual(len(runs), 4)
        self.assertEqual(runs[0]["heldOutPhotosQuarantined"], 0)
        counts = [r["heldOutPhotosQuarantined"] for r in runs]
        self.assertEqual(counts, sorted(counts))
        self.assertGreaterEqual(counts[1], 1)
        self.assertGreaterEqual(counts[2], 2)
        self.assertGreaterEqual(counts[3], 3)
        self.assertEqual(runs[0]["heldOutCandidatePairsExcluded"], 0)
        for row in runs:
            self.assertEqual(
                row["heldOutPhotosRemaining"] + row["heldOutPhotosQuarantined"],
                report["heldOutPhotos"])
            self.assertEqual(
                row["heldOutCandidatePairsRemaining"] +
                row["heldOutCandidatePairsExcluded"],
                runs[0]["heldOutCandidatePairsRemaining"])
            self.assertTrue(set(row["heldOutBucketShortfalls"]).issubset(
                set(row["eligibleHeldOutBuckets"])))
        self.assertFalse(report["reviewSetFrozen"])
        self.assertFalse(report["heldOutReviewsAccessed"])
        self.assertFalse(report["productionSuppressionApproved"])

    def test_input_order_independence_and_aggregate_privacy(self):
        original = augmented_fixture()
        first = quarantine_scenarios(original)
        shuffled = copy.deepcopy(original)
        shuffled["photos"].reverse()
        shuffled["pairs"].reverse()
        self.assertEqual(first, quarantine_scenarios(shuffled))
        output = json.dumps(first)
        for secret in ("private-revision", "synthetic-redundancy-study",
                       "leftRevisionId", "rightRevisionId", "contentSha256"):
            self.assertNotIn(secret, output)

    def test_forbids_injected_human_labels(self):
        evidence = fixture()
        evidence["pairs"][0]["humanReviewLabel"] = "redundant"
        with self.assertRaises(ValueError):
            quarantine_scenarios(evidence)

    def test_zero_exact_controls_are_reported_not_created(self):
        evidence = fixture()
        report = quarantine_scenarios(evidence)
        self.assertEqual(report["genuineExactCopyControlsInCandidates"], 1)
        self.assertEqual(report["scenarios"][0]["heldOutPhotosQuarantined"], 0)
        self.assertFalse(report["humanLabelsAccessed"])


if __name__ == "__main__":
    unittest.main()
