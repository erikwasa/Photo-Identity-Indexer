"""Synthetic WI-0194 full-archive candidate preflight tests; no real images or labels."""
import copy
import json
import unittest

from redundancy_candidate_preflight import preflight
from redundancy_review_pairs import prepare_inventory
from test_redundancy_review_pairs import fixture


class RedundancyCandidatePreflightTests(unittest.TestCase):
    def test_preflight_is_aggregate_only_deterministic_and_never_freezes(self):
        data = fixture()
        actual = preflight(data)
        repeated = copy.deepcopy(data)
        repeated["photos"].reverse()
        repeated["pairs"].reverse()
        self.assertEqual(actual, preflight(repeated))
        self.assertEqual(actual["photoCount"], 100)
        self.assertEqual(actual["candidatePairs"], 149)
        self.assertEqual(actual["randomNegativeControls"], 50)
        self.assertEqual(
            actual["candidatePartitionCounts"]["crossPartitionKnownExactContentPairs"], 0)
        self.assertEqual(
            actual["candidatePartitionCounts"]["crossPartitionStrictDhashNearCopyPairs"], 0)
        self.assertGreater(actual["leakageFamilies"]["multiPhotoFamilies"], 0)
        self.assertFalse(actual["reviewSetFrozen"])
        self.assertFalse(actual["humanRedundancyLabelsAccessed"])
        self.assertFalse(actual["productionSuppressionApproved"])
        output = json.dumps(actual)
        for private in ("private-revision", "synthetic-redundancy-study",
                        "contentSha256", "leftRevisionId", "rightRevisionId"):
            self.assertNotIn(private, output)

    def test_unlinked_close_candidates_across_partitions_are_counted_not_frozen(self):
        data = fixture()
        inventory, pairs, membership, _ = prepare_inventory(data)
        keys = sorted(inventory)
        match = next((a, b) for a in keys for b in keys
                     if a < b and membership[a] != membership[b]
                     and (a, b) not in pairs)
        data["pairs"].append({
            "leftRevisionId": match[0],
            "rightRevisionId": match[1],
            "sourceKind": "retrieved",
            "dhashDistance": 6,
            "clipCosine": 0.95,
        })
        result = preflight(data)
        self.assertGreaterEqual(
            result["candidatePartitionCounts"]["crossPartitionDhashDistance5To8"], 1)
        self.assertGreaterEqual(
            result["candidatePartitionCounts"]["crossPartitionClipCosineAtLeast0_90"], 1)
        self.assertFalse(result["reviewSetFrozen"])

    def test_refuses_human_labels_even_as_extra_fields(self):
        data = fixture()
        data["pairs"][0]["reviewLabel"] = "redundant"
        with self.assertRaises(ValueError):
            preflight(data)

    def test_strict_near_copy_links_copartition_before_preflight(self):
        data = fixture()
        data["pairs"][3]["dhashDistance"] = 3
        result = preflight(data)
        self.assertGreater(result["strictDhashNearCopyCandidateLinks"], 0)
        self.assertEqual(
            result["candidatePartitionCounts"]["crossPartitionStrictDhashNearCopyPairs"], 0)


if __name__ == "__main__":
    unittest.main()
